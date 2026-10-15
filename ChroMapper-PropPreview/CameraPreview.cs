using System;
using System.Collections.Generic;
using System.Linq;
using SimpleJSON;
using UnityEngine;

namespace ChroMapper_PropPreview
{
    /// <summary>
    /// SetCameraProperty, and the properties a CreateCamera event gives its camera. Vivify's rules
    /// (Events/SetCameraProperty.cs, Controllers/CameraPropertyController.cs): an event targets
    /// its `id` ("_Main" when there is none: the player's camera, here ChroMapper's view camera);
    /// each property keeps the last value an event gave it, instantly, and null puts the camera's
    /// own value back; depthTextureMode adds to the camera's own mode. The skybox only shows with
    /// clearFlags Skybox, as in the game. While the map sets clearFlags on the main camera,
    /// ChroMapper's fog backdrop is hidden, since it would paint over the map's sky or colour.
    /// </summary>
    public class CameraPreview
    {
        private const int BloomfogSkyLayer = 29;
        public const string MainCamera = "_Main";

        /// <summary>A camera's properties at some time; null = the camera's own value.</summary>
        public class State
        {
            public CameraClearFlags? ClearFlags;
            public Color? BackgroundColor;
            public DepthTextureMode? DepthTextureMode;
            /// <summary>Tracks whose objects the camera shows only (whitelist) or hides.</summary>
            public string[]? CullingTracks;
            public bool CullingWhitelist;
            public bool? BloomPrePass;

            public bool Any => ClearFlags != null || BackgroundColor != null || DepthTextureMode != null
                || CullingTracks != null;
        }

        private readonly List<(float time, string id, JSONNode props)> events = new List<(float, string, JSONNode)>();

        /// <summary>The skybox from SetRenderingSettings, drawn when clearFlags is Skybox.</summary>
        public Material? Skybox;

        /// <summary>The main camera's state at the time last evaluated.</summary>
        public State Main { get; private set; } = new State();

        private CameraManager? cameraManager;
        private Camera? touched;
        private (CameraClearFlags flags, Color background, int mask, DepthTextureMode depth) original;

        public void Clear()
        {
            events.Clear();
            Main = new State();
            Restore();
        }

        /// <summary>A SetCameraProperty event, or a CreateCamera event's own properties.</summary>
        public void Add(float time, JSONNode data)
        {
            var id = data["id"];
            var props = data["properties"];
            if (props == null || !props.IsObject) return;
            events.Add((time, id != null && id.IsString ? id.Value : MainCamera, props));
            events.Sort((a, b) => a.time.CompareTo(b.time));
        }

        public void Evaluate(float time) => Main = StateOf(MainCamera, time);

        public State StateOf(string id, float time)
        {
            var state = new State();
            foreach (var (t, eid, props) in events)
            {
                if (t > time) break;
                if (eid != id) continue;
                foreach (var kv in props.AsObject)
                {
                    var v = kv.Value;
                    switch (kv.Key)
                    {
                        case "clearFlags": state.ClearFlags = ParseClearFlags(v); break;
                        case "backgroundColor":
                            state.BackgroundColor = v != null && v.IsArray && v.Count >= 3
                                ? new Color(v[0].AsFloat, v[1].AsFloat, v[2].AsFloat, v.Count > 3 ? v[3].AsFloat : 1)
                                : (Color?)null;
                            break;
                        case "depthTextureMode": state.DepthTextureMode = ParseDepthMode(v); break;
                        case "bloomPrePass": state.BloomPrePass = v != null && v.IsBoolean ? v.AsBool : (bool?)null; break;
                        case "culling":
                            if (v == null || !v.IsObject)
                            {
                                state.CullingTracks = null;
                                break;
                            }
                            state.CullingTracks = Tracks(v["track"]).ToArray();
                            state.CullingWhitelist = v["whitelist"] != null && v["whitelist"].AsBool;
                            break;
                    }
                }
            }
            return state;
        }

        public bool IsViewCamera(Camera cam)
        {
            if (cameraManager == null) cameraManager = UnityEngine.Object.FindAnyObjectByType<CameraManager>();
            return cameraManager != null && cameraManager.SelectedCameraController != null
                && cam == cameraManager.SelectedCameraController.Camera;
        }

        public void OnPreRender(Camera cam)
        {
            if (!IsViewCamera(cam)) return;
            var s = Main;
            if (s.ClearFlags == null && s.BackgroundColor == null && s.DepthTextureMode == null)
            {
                Restore();
                return;
            }
            if (touched != cam)
            {
                Restore();
                touched = cam;
                original = (cam.clearFlags, cam.backgroundColor, cam.cullingMask, cam.depthTextureMode);
            }
            cam.clearFlags = s.ClearFlags ?? original.flags;
            cam.backgroundColor = s.BackgroundColor ?? original.background;
            cam.depthTextureMode = original.depth | (s.DepthTextureMode ?? DepthTextureMode.None);
            // Culling (layer 22) may have changed the mask since; keep its bit as it is.
            var mask = s.ClearFlags != null ? original.mask & ~(1 << BloomfogSkyLayer) : original.mask;
            cam.cullingMask = (mask & ~(1 << PostProcessing.CullingLayer)) | (cam.cullingMask & (1 << PostProcessing.CullingLayer));
            ApplySky(cam, s.ClearFlags == CameraClearFlags.Skybox ? Skybox : null);
        }

        /// <summary>Gives `cam` a Skybox component with `material`, or turns it off.</summary>
        public static void ApplySky(Camera cam, Material? material)
        {
            var sky = cam.GetComponent<Skybox>();
            if (material != null)
            {
                if (sky == null) sky = cam.gameObject.AddComponent<Skybox>();
                sky.material = material;
                sky.enabled = true;
            }
            else if (sky != null)
            {
                sky.enabled = false;
            }
        }

        public void Restore()
        {
            if (touched == null) return;
            touched.clearFlags = original.flags;
            touched.backgroundColor = original.background;
            touched.cullingMask = original.mask;
            touched.depthTextureMode = original.depth;
            ApplySky(touched, null);
            touched = null;
        }

        public static CameraClearFlags? ParseClearFlags(JSONNode? v)
        {
            if (v == null || !v.IsString) return null;
            return Enum.TryParse(v.Value, out CameraClearFlags f) ? f : (CameraClearFlags?)null;
        }

        private static DepthTextureMode? ParseDepthMode(JSONNode? v)
        {
            if (v == null || !v.IsArray) return null;
            var mode = DepthTextureMode.None;
            foreach (var n in v.Children)
                if (n.IsString && Enum.TryParse(n.Value, out DepthTextureMode m)) mode |= m;
            return mode;
        }

        public static IEnumerable<string> Tracks(JSONNode? node)
        {
            if (node == null) yield break;
            if (node.IsString) yield return node.Value;
            else if (node.IsArray)
                foreach (var n in node.Children)
                    if (n.IsString) yield return n.Value;
        }
    }
}
