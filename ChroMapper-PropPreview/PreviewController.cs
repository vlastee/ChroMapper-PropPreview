using System;
using System.Collections.Generic;
using System.Linq;
using Beatmap.Animations;
using Beatmap.Base;
using Beatmap.Base.Customs;
using Beatmap.Containers;
using Beatmap.Enums;
using SimpleJSON;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace ChroMapper_PropPreview
{
    /// <summary>
    /// Shows the map's Vivify events at the current song time in the map editor:
    /// InstantiatePrefab / DestroyObject (prefab instances, moved by Heck tracks through
    /// ChroMapper's own track animation), SetMaterialProperty, SetGlobalProperty and
    /// SetRenderingSettings (renderSettings), SetCameraProperty (CameraPreview), AssignObjectPrefab
    /// (NotePrefabs), SetAnimatorProperty (Animators), and CreateScreenTexture, CreateCamera and
    /// Blit (PostProcessing). Everything is recomputed from the song time, so
    /// scrubbing works in both directions. Any edit to the custom events rebuilds the timeline.
    /// </summary>
    [DefaultExecutionOrder(10000)] // after ChroMapper's track animators have written the poses
    public class PreviewController : MonoBehaviour
    {
        private class Instance
        {
            public GameObject Object = null!;  // the prefab instance, shown while it exists
            public GameObject Holder = null!;  // its always-active parent: pose and track link
            public float Spawn;
            public float Despawn = float.PositiveInfinity;
            public ObjectAnimator? Animator;
            public List<string> Tracks = new List<string>();
            public PrefabClock? Clock;
            public string? OwnTrack; // an AssignTrackParent child track, evaluated by ParentedTracks
            public (Vector3 pos, Quaternion rot, Vector3 scale) SpawnLocal;
        }

        private class MaterialProperty
        {
            public Material Material = null!;
            public string Id = "";
            public string Type = "";
            public readonly Timeline Timeline = new Timeline();
            public object? Original;
        }

        private BeatmapRuntimeContext context = null!;
        private TracksManager tracksManager = null!;
        private BundleLoader? bundle;
        private string? bundleDir;
        private List<BaseCustomEvent>? events;
        private bool dirty = true;

        private readonly List<Instance> instances = new List<Instance>();
        private readonly Dictionary<string, Instance> ids = new Dictionary<string, Instance>(); // last instance per id
        private readonly List<MaterialProperty> materialProperties = new List<MaterialProperty>();
        private readonly List<MaterialProperty> globalProperties = new List<MaterialProperty>();
        private readonly Dictionary<string, Timeline> renderSettings = new Dictionary<string, Timeline>();
        private RenderingSnapshot? renderOriginal;
        private readonly GameBaseline baseline = new GameBaseline();
        private readonly CameraPreview camera = new CameraPreview();
        private readonly ParentedTracks parented = new ParentedTracks();
        private readonly NotePrefabs notePrefabs = new NotePrefabs();
        private readonly Animators animators = new Animators();
        private readonly PostProcessing post;
        private float lastTime = float.NegativeInfinity;

        /// <summary>The controller of the open map editor, for the plugin's ObjectLoaded hook.</summary>
        public static PreviewController? Current { get; private set; }

        public PreviewController() => post = new PostProcessing(camera, TrackObjects);

        private CustomEventGridContainer? grid;

        private void Start()
        {
            LoadInitialMap.OnLevelLoaded += MarkDirty;
            LoadedDifficultySelectController.OnLoadedDifficultyChanged += MarkDirty;
            // Switching modes resets ChroMapper's animation tracks (and AnimateTrack only reaches
            // them in Preview/Playing with Animations on), so prefabs re-attach to their tracks.
            UIMode.OnUIModeSwitched += OnUIMode;
            BeatmapActionContainer.OnActionCreated += OnAction;
            BeatmapActionContainer.OnActionUndo += OnAction;
            BeatmapActionContainer.OnActionRedo += OnAction;
            Camera.onPreCull += OnCameraPreCull;
            Camera.onPreRender += OnCameraPreRender;
            Camera.onPostRender += OnCameraPostRender;
            Current = this;
        }

        private void OnDestroy()
        {
            LoadInitialMap.OnLevelLoaded -= MarkDirty;
            LoadedDifficultySelectController.OnLoadedDifficultyChanged -= MarkDirty;
            UIMode.OnUIModeSwitched -= OnUIMode;
            BeatmapActionContainer.OnActionCreated -= OnAction;
            BeatmapActionContainer.OnActionUndo -= OnAction;
            BeatmapActionContainer.OnActionRedo -= OnAction;
            Camera.onPreCull -= OnCameraPreCull;
            Camera.onPreRender -= OnCameraPreRender;
            Camera.onPostRender -= OnCameraPostRender;
            if (Current == this) Current = null;
            if (grid != null)
            {
                grid.OnObjectSpawned -= OnCustomEvent;
                grid.OnObjectDeleted -= OnCustomEvent;
            }
            Clear();
            bundle?.Unload();
        }

        private void MarkDirty() => dirty = true;
        private void OnAction(BeatmapAction _) => dirty = true;
        private void OnUIMode(UIModeType _) => dirty = true;
        private void OnCustomEvent(BaseCustomEvent _) => dirty = true;

        private void LateUpdate()
        {
            var map = BeatSaberSongContainer.Instance?.Map;
            if (map == null) return;
            if (!ReferenceEquals(map.CustomEvents, events)) dirty = true;
            if (dirty)
            {
                dirty = false;
                try
                {
                    Rebuild(map);
                }
                catch (Exception e)
                {
                    Debug.LogException(e);
                }
            }
            notePrefabs.Sync();
            if (context == null || context.Atsc == null) return;
            Apply(context.Atsc.CurrentJsonTime);
        }

        // ---- building ----

        private void Clear()
        {
            if (instances.Count > 0) Debug.Log($"PropPreview: clearing {instances.Count} prefab instances");
            foreach (var inst in instances)
            {
                if (inst.Animator != null && tracksManager != null)
                    foreach (var t in inst.Tracks) tracksManager.GetAnimationTrack(t).RemoveChild(inst.Animator);
                if (inst.Holder != null) Destroy(inst.Holder);
            }
            instances.Clear();
            ids.Clear();
            foreach (var p in materialProperties) SetMaterial(p, p.Original);
            foreach (var p in globalProperties) SetGlobal(p, p.Original);
            materialProperties.Clear();
            globalProperties.Clear();
            renderSettings.Clear();
            renderOriginal?.Restore();
            renderOriginal = null;
            baseline.Restore();
            camera.Clear();
            animators.Clear();
            post.Clear();
            if (!notePrefabs.Empty)
            {
                notePrefabs.Clear(null);
                DressNotes();
            }
        }

        private void Rebuild(BaseDifficulty map)
        {
            var clock = System.Diagnostics.Stopwatch.StartNew();
            Clear();
            events = map.CustomEvents;
            context = FindAnyObjectByType<BeatmapRuntimeContext>();
            tracksManager = FindAnyObjectByType<TracksManager>();
            if (grid == null)
            {
                grid = BeatmapObjectContainerCollection.GetCollectionForType<CustomEventGridContainer>(ObjectType.CustomEvent);
                if (grid != null)
                {
                    grid.OnObjectSpawned += OnCustomEvent;
                    grid.OnObjectDeleted += OnCustomEvent;
                }
            }

            var dir = BeatSaberSongContainer.Instance.Info.Directory;
            if (dir != bundleDir)
            {
                bundle?.Unload();
                bundle = BundleLoader.ForMap(dir);
                bundleDir = dir;
            }
            if (bundle == null || context == null || tracksManager == null) return;
            notePrefabs.Clear(bundle);

            var vivify = events.Where(e => e.Data != null).OrderBy(e => e.JsonTime).ToList();
            parented.Clear(map.PointDefinitions);
            foreach (var ev in vivify)
                if (ev.Type == "AssignTrackParent" || ev.Type == "AnimateTrack") parented.Add(ev);
            var v2 = map.MajorVersion == 2;
            var live = new Dictionary<string, Instance>();
            foreach (var ev in vivify)
            {
                switch (ev.Type)
                {
                    case "InstantiatePrefab":
                        var inst = Instantiate(ev, v2);
                        if (inst == null) break;
                        instances.Add(inst);
                        var id = ev.Data["id"];
                        if (id != null && id.IsString) live[id.Value] = ids[id.Value] = inst;
                        break;
                    case "DestroyObject":
                        foreach (var name in Strings(ev.Data["id"]))
                        {
                            if (!live.TryGetValue(name, out var target)) continue;
                            target.Despawn = ev.JsonTime;
                            live.Remove(name);
                        }
                        break;
                    case "SetMaterialProperty":
                        var material = bundle.Load<Material>(ev.Data["asset"]);
                        if (material == null) break;
                        AddProperties(ev, materialProperties, material);
                        break;
                    case "SetGlobalProperty":
                        AddProperties(ev, globalProperties, null);
                        break;
                    case "SetRenderingSettings":
                        AddRenderSettings(ev);
                        break;
                    case "SetCameraProperty":
                        camera.Add(ev.JsonTime, ev.Data);
                        break;
                    case "AssignObjectPrefab":
                        notePrefabs.Add(ev);
                        break;
                    case "SetAnimatorProperty":
                        var animatorId = ev.Data["id"];
                        if (animatorId != null && animatorId.IsString && live.TryGetValue(animatorId.Value, out var owner))
                            animators.Add(ev, owner.Object);
                        break;
                    case "CreateScreenTexture":
                        post.AddScreenTexture(ev.JsonTime, ev.Data);
                        break;
                    case "CreateCamera":
                        post.AddCamera(ev.JsonTime, ev.Data);
                        break;
                    case "Blit":
                        var blitMaterial = bundle.Load<Material>(ev.Data["asset"]);
                        post.AddBlit(ev.JsonTime, ev.Data, blitMaterial);
                        if (blitMaterial != null) AddProperties(ev, materialProperties, blitMaterial);
                        break;
                }
            }
            if (renderSettings.Count > 0) renderOriginal = RenderingSnapshot.Take();
            DressNotes();
            if (!notePrefabs.Empty)
                Debug.Log($"PropPreview: note prefabs on {notePrefabs.Attached} of {notePrefabs.Dressed} containers on screen");
            Debug.Log($"PropPreview: {instances.Count} prefabs, {materialProperties.Count} material "
                + $"properties, {globalProperties.Count} globals, {renderSettings.Count} render settings "
                + $"from {vivify.Count} events in {clock.ElapsedMilliseconds} ms");
        }

        /// <summary>Called for every note, chain (and other) container ChroMapper spawns.</summary>
        public void ContainerLoaded(ObjectContainer container) => notePrefabs.Apply(container);

        // Re-dresses the note and chain containers already on screen.
        private void DressNotes()
        {
            foreach (var type in new[] { ObjectType.Note, ObjectType.Chain })
            {
                var collection = BeatmapObjectContainerCollection.GetCollectionForType(type);
                if (collection == null) continue;
                foreach (var c in collection.LoadedContainers.Values.ToList()) notePrefabs.Apply(c);
            }
        }

        // Prefab instances on a track that exist at the current time (for camera culling).
        private IEnumerable<GameObject> TrackObjects(string track) =>
            instances.Where(i => i.Object != null && i.Object.activeSelf && i.Tracks.Contains(track)).Select(i => i.Object);

        // The holder takes the parent tracks' world transform; the prefab root takes its own
        // track's values where the track sets them: local* relative to the parents, position and
        // rotation as world values (Vivify's TransformController).
        private void FollowOwnTrack(Instance inst, float time)
        {
            if (inst.Holder == null || inst.Object == null) return;
            var parentWorld = parented.ParentWorld(inst.OwnTrack!, time);
            var h = inst.Holder.transform;
            h.SetPositionAndRotation(parentWorld.GetColumn(3), parentWorld.rotation);
            h.localScale = parentWorld.lossyScale;
            var v = parented.Values(inst.OwnTrack!, time);
            var t = inst.Object.transform;
            t.localScale = v.scale ?? inst.SpawnLocal.scale;
            if (v.localRot is Quaternion lr) t.localRotation = lr;
            else if (v.rot is Quaternion r) t.rotation = r;
            else t.localRotation = inst.SpawnLocal.rot;
            if (v.localPos is Vector3 lp) t.localPosition = lp;
            else if (v.pos is Vector3 p) t.position = p;
            else t.localPosition = inst.SpawnLocal.pos;
        }

        private Instance? Instantiate(BaseCustomEvent ev, bool v2)
        {
            var prefab = bundle!.Load<GameObject>(ev.Data["asset"]);
            if (prefab == null) return null;
            // The prefab sits in an always-active holder at the origin, which carries the track
            // link (ChroMapper's ObjectAnimator leaves its tracks when its object is deactivated
            // and never rejoins them); only the prefab is shown and hidden. The event's fields,
            // the track and the prefab's own Animator all write the prefab root, as in Vivify.
            var holder = new GameObject($"PropPreview {ev.Data["asset"].Value}");
            var go = UnityEngine.Object.Instantiate(prefab, holder.transform, false);
            TextRepair.Repair(go);
            var t = go.transform;
            var env = context.Descriptor;
            if (env != null)
            {
                SceneManager.MoveGameObjectToScene(holder, env.gameObject.scene);
                SetLayer(holder.transform, env.gameObject.layer);
            }
            // localPosition wins over position, localRotation over rotation (Vivify TransformData).
            if (Vector3Of(ev.Data["localPosition"]) is Vector3 lp) t.localPosition = lp;
            else if (Vector3Of(ev.Data["position"]) is Vector3 p) t.position = p;
            if (Vector3Of(ev.Data["localRotation"]) is Vector3 lr) t.localRotation = Quaternion.Euler(lr);
            else if (Vector3Of(ev.Data["rotation"]) is Vector3 r) t.rotation = Quaternion.Euler(r);
            if (Vector3Of(ev.Data["scale"]) is Vector3 s) t.localScale = s;

            var inst = new Instance { Object = go, Holder = holder, Spawn = ev.JsonTime };
            var songBpmTime = BeatSaberSongContainer.Instance.Map.JsonTimeToSongBpmTime(ev.JsonTime) ?? ev.JsonTime;
            var clock = new PrefabClock(go, context.Atsc.GetSecondsFromBeat(songBpmTime));
            if (!clock.Empty) inst.Clock = clock;
            inst.Tracks.AddRange(Strings(ev.Data["track"]));
            if (inst.Tracks.Count > 0 && parented.HasParent(inst.Tracks[0]))
            {
                // On an AssignTrackParent child track: evaluated here (ParentedTracks).
                inst.OwnTrack = inst.Tracks[0];
                inst.SpawnLocal = (t.localPosition, t.localRotation, t.localScale);
            }
            else if (inst.Tracks.Count > 0)
            {
                // A track overwrites the pose with its own values, as Vivify's TransformController does.
                var oa = holder.AddComponent<ObjectAnimator>();
                oa.Context = context;
                oa.TracksManager = tracksManager;
                AttachToEnvironmentObject(oa, t, inst.Tracks[0], v2); // targets the prefab root
                foreach (var extra in inst.Tracks.Skip(1)) oa.AddParent(extra);
                inst.Animator = oa;
            }
            go.SetActive(false);
            return inst;
        }

        /// <summary>
        /// ObjectAnimator.AttachToEnvironmentObject(target, track, v2[, more]): ChroMapper 0.14 builds
        /// before 996 take three arguments, later ones a fourth (the box light, none here). Called
        /// by reflection so the plugin runs on both; the extra arguments get their defaults or null.
        /// </summary>
        private static void AttachToEnvironmentObject(ObjectAnimator oa, Transform target, string track, bool v2)
        {
            attachToEnvironment ??= typeof(ObjectAnimator).GetMethods()
                .Where(m => m.Name == nameof(ObjectAnimator.AttachToEnvironmentObject) && m.GetParameters().Length >= 3)
                .OrderBy(m => m.GetParameters().Length).First();
            var parameters = attachToEnvironment.GetParameters();
            var args = new object?[parameters.Length];
            args[0] = target;
            args[1] = track;
            args[2] = v2;
            for (var i = 3; i < args.Length; i++)
                args[i] = parameters[i].HasDefaultValue ? parameters[i].DefaultValue : null;
            attachToEnvironment.Invoke(oa, args);
        }

        private static System.Reflection.MethodInfo? attachToEnvironment;

        private void AddProperties(BaseCustomEvent ev, List<MaterialProperty> into, Material? material)
        {
            var props = ev.Data["properties"];
            if (props == null || !props.IsArray) return;
            foreach (var prop in props.Children)
            {
                string id = prop["id"], type = prop["type"];
                if (string.IsNullOrEmpty(id) || string.IsNullOrEmpty(type)) continue;
                var target = into.FirstOrDefault(x => x.Material == material && x.Id == id);
                if (target == null)
                {
                    target = new MaterialProperty { Material = material!, Id = id, Type = type };
                    target.Original = material != null ? ReadMaterial(material, id, type) : ReadGlobal(id, type);
                    into.Add(target);
                }
                var entry = Entry(ev, prop["value"], type == "Color" || type == "Vector" ? 4 : 1);
                // Vivify bug kept: an animated keyword with a duration never applies.
                if (type == "Keyword" && entry.Points != null && entry.Duration > 0) continue;
                target.Timeline.Add(entry);
            }
        }

        private void AddRenderSettings(BaseCustomEvent ev)
        {
            var settings = ev.Data["renderSettings"];
            if (settings != null && settings.IsObject)
                foreach (var kv in settings.AsObject)
                {
                    if (!RenderingSnapshot.Width.TryGetValue(kv.Key, out var width)) continue;
                    if (!renderSettings.TryGetValue(kv.Key, out var timeline))
                        renderSettings[kv.Key] = timeline = new Timeline();
                    timeline.Add(Entry(ev, kv.Value, width));
                }
            var quality = ev.Data["qualitySettings"];
            if (quality != null && quality.IsObject)
                foreach (var kv in quality.AsObject)
                {
                    if (!RenderingSnapshot.Quality.Contains(kv.Key)) continue;
                    var key = "quality:" + kv.Key;
                    if (!renderSettings.TryGetValue(key, out var timeline))
                        renderSettings[key] = timeline = new Timeline();
                    timeline.Add(Entry(ev, kv.Value, 1));
                }
        }

        private static Timeline.Entry Entry(BaseCustomEvent ev, JSONNode value, int width)
        {
            var duration = ev.Data["duration"];
            var entry = new Timeline.Entry
            {
                Time = ev.JsonTime,
                Duration = duration != null && duration.IsNumber ? duration.AsFloat : 0,
                Easing = Easings.Get(ev.Data["easing"]),
            };
            if (value != null && value.IsArray) entry.Points = Points.Parse(value, width);
            else entry.Static = value;
            return entry;
        }

        // ---- applying ----

        private void Apply(float time)
        {
            foreach (var inst in instances)
            {
                var on = time >= inst.Spawn && time < inst.Despawn;
                if (inst.Object != null && inst.Object.activeSelf != on) inst.Object.SetActive(on);
                if (inst.OwnTrack != null && on) FollowOwnTrack(inst, time);
            }
            foreach (var p in materialProperties) SetMaterial(p, Value(p, time));
            foreach (var p in globalProperties) SetGlobal(p, Value(p, time));
            camera.Evaluate(time);
            var playing = context.Atsc.IsPlaying;
            var seconds = context.Atsc.CurrentSeconds;
            foreach (var inst in instances)
                inst.Clock?.Sync(seconds, () => animators.Apply(time, playing));
            animators.Apply(time, playing);
            post.Evaluate(time, lastTime, playing);
            lastTime = time;
            camera.Skybox = null;
            if (renderOriginal != null) renderOriginal.Restore(updateAmbient: false);
            // The game's own rendering state first (GameBaseline), then the map's settings.
            if (bundle != null) baseline.Apply();
            if (renderOriginal != null)
            {
                foreach (var kv in renderSettings)
                {
                    var v = kv.Value.Vector(time, out var s);
                    if (kv.Key == "skybox")
                    {
                        if (s != null && s.IsString) camera.Skybox = bundle?.Load<Material>(s);
                        continue;
                    }
                    if (kv.Key == "sun")
                    {
                        // The first directional light in the prefab instance with that id.
                        if (s != null && s.IsString && ids.TryGetValue(s.Value, out var sunInst) && sunInst.Object != null)
                            RenderSettings.sun = sunInst.Object.GetComponentsInChildren<Light>(true)
                                .FirstOrDefault(l => l.type == LightType.Directional);
                        continue;
                    }
                    if (kv.Key.StartsWith("quality:"))
                    {
                        var q = kv.Key.Substring(8);
                        if (v is Vector4 qv) RenderingSnapshot.SetQuality(q, qv.x);
                        else if (s != null && (s.IsNumber || s.IsBoolean)) RenderingSnapshot.SetQuality(q, s.IsBoolean ? (s.AsBool ? 1 : 0) : s.AsFloat);
                        continue;
                    }
                    if (v is Vector4 vec) RenderingSnapshot.Set(kv.Key, vec);
                    else if (s != null && s.IsNumber) RenderingSnapshot.Set(kv.Key, new Vector4(s.AsFloat, 0, 0, 0));
                }
            }
            if (bundle != null) RenderingSnapshot.UpdateAmbient();
        }

        private object? Value(MaterialProperty p, float time)
        {
            var v = p.Timeline.Vector(time, out var s);
            if (v == null && s == null && p.Timeline.At(time, out _) == null) return p.Original;
            switch (p.Type)
            {
                case "Float":
                    if (v is Vector4 f) return f.x;
                    return s != null && s.IsNumber ? s.AsFloat : p.Original;
                case "Color": return v is Vector4 c ? (Color)c : p.Original;
                case "Vector": return v ?? p.Original;
                case "Keyword": return v is Vector4 k ? k.x >= 1 : s != null && s.IsBoolean ? s.AsBool : p.Original;
                case "Texture": return s != null && s.IsString ? bundle?.Load<Texture>(s) : p.Original;
                default: return p.Original;
            }
        }

        private static object? ReadMaterial(Material m, string id, string type)
        {
            switch (type)
            {
                case "Float": return m.HasProperty(id) ? m.GetFloat(id) : 0f;
                case "Color": return m.HasProperty(id) ? m.GetColor(id) : Color.clear;
                case "Vector": return m.HasProperty(id) ? m.GetVector(id) : Vector4.zero;
                case "Texture": return m.HasProperty(id) ? m.GetTexture(id) : null;
                case "Keyword": return m.IsKeywordEnabled(id);
                default: return null;
            }
        }

        private static object? ReadGlobal(string id, string type)
        {
            switch (type)
            {
                case "Float": return Shader.GetGlobalFloat(id);
                case "Color": return Shader.GetGlobalColor(id);
                case "Vector": return Shader.GetGlobalVector(id);
                case "Texture": return Shader.GetGlobalTexture(id);
                case "Keyword": return Shader.IsKeywordEnabled(id);
                default: return null;
            }
        }

        private static void SetMaterial(MaterialProperty p, object? value)
        {
            var m = p.Material;
            if (m == null || value == null && p.Type != "Texture") return;
            switch (p.Type)
            {
                case "Float": m.SetFloat(p.Id, (float)value!); break;
                case "Color": m.SetColor(p.Id, (Color)value!); break;
                case "Vector": m.SetVector(p.Id, (Vector4)value!); break;
                case "Texture": m.SetTexture(p.Id, value as Texture); break;
                case "Keyword":
                    if ((bool)value!) m.EnableKeyword(p.Id);
                    else m.DisableKeyword(p.Id);
                    break;
            }
        }

        private static void SetGlobal(MaterialProperty p, object? value)
        {
            if (value == null && p.Type != "Texture") return;
            switch (p.Type)
            {
                case "Float": Shader.SetGlobalFloat(p.Id, (float)value!); break;
                case "Color": Shader.SetGlobalColor(p.Id, (Color)value!); break;
                case "Vector": Shader.SetGlobalVector(p.Id, (Vector4)value!); break;
                case "Texture": Shader.SetGlobalTexture(p.Id, value as Texture); break;
                case "Keyword":
                    if ((bool)value!) Shader.EnableKeyword(p.Id);
                    else Shader.DisableKeyword(p.Id);
                    break;
            }
        }

        private void OnCameraPreCull(Camera cam) => post.OnPreCull(cam);

        private void OnCameraPreRender(Camera cam)
        {
            if (bundle != null && camera.IsViewCamera(cam) && context != null && context.Atsc != null)
                baseline.OnPreRender(cam, context.Atsc.CurrentSeconds);
            camera.OnPreRender(cam);
            post.OnPreRender(cam);
        }

        private void OnCameraPostRender(Camera cam)
        {
            post.OnPostRender(cam);
            baseline.OnPostRender(cam);
        }

        // ---- JSON helpers ----

        private static IEnumerable<string> Strings(JSONNode? node)
        {
            if (node == null) yield break;
            if (node.IsString) yield return node.Value;
            else if (node.IsArray)
                foreach (var n in node.Children)
                    if (n.IsString) yield return n.Value;
        }

        private static Vector3? Vector3Of(JSONNode? node) =>
            node != null && node.IsArray && node.Count >= 3
                ? new Vector3(node[0].AsFloat, node[1].AsFloat, node[2].AsFloat)
                : (Vector3?)null;

        private static void SetLayer(Transform t, int layer)
        {
            t.gameObject.layer = layer;
            foreach (Transform child in t) SetLayer(child, layer);
        }
    }
}
