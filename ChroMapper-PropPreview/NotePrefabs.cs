using System.Collections.Generic;
using System.Linq;
using Beatmap.Base;
using Beatmap.Base.Customs;
using Beatmap.Containers;
using Beatmap.Enums;
using Beatmap.Shared;
using SimpleJSON;
using UnityEngine;

namespace ChroMapper_PropPreview
{
    /// <summary>
    /// AssignObjectPrefab for notes, bombs and chains, on ChroMapper's note containers. Vivify's
    /// rules (Events/AssignObjectPrefab.cs, ObjectPrefab/**): per track and object kind there is a
    /// set of prefabs, starting as {vanilla}; loadMode Single empties it first, then the asset is
    /// added (a null asset, or one missing from the bundle, stands for the vanilla model). Every
    /// prefab in the set is attached to the note's model with its own local transform; the vanilla
    /// model stays only while the set still holds it. The prefab's renderers get the note's colour
    /// block, so its _Color and Noodle dissolve, as in the game (see Sync). A dot note uses
    /// anyDirectionAsset and nothing else. Vivify applies an assignment to notes alive at the
    /// event and to notes spawned later; here a note takes the assignment in effect at its own beat.
    /// Sabers are not previewed: ChroMapper shows none.
    /// </summary>
    public class NotePrefabs
    {
        private const string Marker = "PropPreview note prefab";
        private static readonly int ColorId = Shader.PropertyToID("_Color");
        private static readonly int CutoutId = Shader.PropertyToID("_Cutout");
        private static readonly int CutoutTexOffsetId = Shader.PropertyToID("_CutoutTexOffset");

        // The prefab renderers on each note model, by the model's colour block.
        private readonly Dictionary<MaterialPropertyBlockController, Dressing> dressings =
            new Dictionary<MaterialPropertyBlockController, Dressing>();

        // (kind, track) -> events in time order, each: (time, Single?, asset or null for vanilla).
        private readonly Dictionary<(string kind, string track), List<(float time, bool single, string? asset)>> events =
            new Dictionary<(string, string), List<(float, bool, string?)>>();

        private BundleLoader? bundle;

        public bool Empty => events.Count == 0;

        /// <summary>Containers dressed and prefab instances attached since the last Clear (for the log).</summary>
        public int Dressed, Attached;

        public void Clear(BundleLoader? loader)
        {
            events.Clear();
            bundle = loader;
            Dressed = Attached = 0;
        }

        public void Add(BaseCustomEvent ev)
        {
            var single = ev.Data["loadMode"]?.Value != "Additive";
            foreach (var kind in new[] { "colorNotes", "bombNotes", "burstSliders", "burstSliderElements" })
            {
                var block = ev.Data[kind];
                if (block == null || !block.IsObject) continue;
                foreach (var track in Tracks(block["track"]))
                {
                    AddSlot(kind, track, ev.JsonTime, single, block, "asset");
                    if (kind == "colorNotes") AddSlot("anyDirection", track, ev.JsonTime, single, block, "anyDirectionAsset");
                }
            }
        }

        // A missing field leaves the slot alone; JSON null means the vanilla model.
        private void AddSlot(string kind, string track, float time, bool single, JSONNode block, string field)
        {
            if (!block.HasKey(field)) return;
            var node = block[field];
            var asset = node != null && node.IsString ? node.Value : null;
            if (!events.TryGetValue((kind, track), out var list)) events[(kind, track)] = list = new List<(float, bool, string?)>();
            list.Add((time, single, asset));
            list.Sort((a, b) => a.time.CompareTo(b.time));
        }

        /// <summary>The prefabs (null = vanilla model) for this kind and these tracks at `time`.</summary>
        private List<GameObject?> At(string kind, IEnumerable<string> tracks, float time)
        {
            var set = new List<GameObject?>();
            var touched = false;
            foreach (var track in tracks)
            {
                if (!events.TryGetValue((kind, track), out var list)) continue;
                var slots = new List<GameObject?> { null };
                foreach (var (t, single, asset) in list)
                {
                    if (t > time) break;
                    touched = true;
                    if (single) slots.Clear();
                    var prefab = asset == null ? null : bundle?.Load<GameObject>(asset);
                    if (!slots.Contains(prefab)) slots.Add(prefab);
                }
                foreach (var s in slots) if (!set.Contains(s)) set.Add(s);
            }
            if (!touched) set.Add(null);
            return set;
        }

        /// <summary>Puts the right prefabs on a container ChroMapper has just spawned (or after an edit).</summary>
        public void Apply(ObjectContainer container)
        {
            switch (container)
            {
                case NoteContainer nc when nc.NoteData != null:
                    var note = nc.NoteData;
                    var kind = note.Type == 3 ? "bombNotes"
                        : note.Chains.Count > 0 ? "burstSliders"
                        : note.CutDirection == 8 ? "anyDirection"
                        : "colorNotes";
                    var set = Empty ? new List<GameObject?> { null } : At(kind, Tracks(note.CustomTrack), note.JsonTime);
                    Dress(nc.ModelController.transform, nc.ModelController.MpbController,
                        nc.ModelController.Renderers, nc.ArrowMpbController, set);
                    break;
                case ChainContainer cc when cc.ChainData != null:
                    var links = Empty ? new List<GameObject?> { null }
                        : At("burstSliderElements", Tracks(cc.ChainData.CustomTrack), cc.ChainData.JsonTime);
                    foreach (var node in cc.Nodes)
                        Dress(node.transform, node.ModelController.MpbController, node.ModelController.Renderers,
                            node.DotMpbController, node.gameObject.activeSelf ? links : new List<GameObject?> { null });
                    break;
            }
        }

        /// <summary>Removes earlier prefabs from `parent`, then attaches `set` and hides or shows the vanilla model.</summary>
        private void Dress(Transform parent, MaterialPropertyBlockController mpb, List<Renderer> vanilla,
            MaterialPropertyBlockController? extra, List<GameObject?> set)
        {
            foreach (Transform child in parent.Cast<Transform>().ToList())
                if (child.name == Marker) Object.Destroy(child.gameObject);
            var keepVanilla = set.Contains(null);
            Dressed++;
            foreach (var r in vanilla) if (r != null) r.enabled = keepVanilla;
            if (extra != null) extra.ShowRenderer(keepVanilla);
            var renderers = new List<Renderer>();
            foreach (var prefab in set)
            {
                if (prefab == null) continue;
                var go = Object.Instantiate(prefab, parent, false);
                TextRepair.Repair(go);
                go.name = Marker;
                Attached++;
                SetLayer(go.transform, parent.gameObject.layer);
                renderers.AddRange(go.GetComponentsInChildren<Renderer>(true));
            }
            if (renderers.Count > 0) dressings[mpb] = new Dressing(renderers.ToArray());
            else dressings.Remove(mpb);
        }

        /// <summary>
        /// Gives the prefab renderers the note's block as the game would: its _Color (alpha 1) and
        /// _CutoutTexOffset, and _Cutout only when it is not 0. In the game the renderers share the
        /// note's block, but Noodle skips a SetCutout that doesn't change the value, so a note that
        /// never dissolves has no _Cutout in it and its prefabs use their material's value (maps
        /// rely on that: 43a1f's glass notes are solid at _Cutout 1). ChroMapper writes 0 into the
        /// block whenever no dissolve is previewed, so the block is copied, per frame, without it.
        /// </summary>
        public void Sync()
        {
            List<MaterialPropertyBlockController>? gone = null;
            foreach (var kv in dressings)
            {
                if (kv.Key == null || kv.Value.Renderers.All(r => r == null))
                {
                    (gone ??= new List<MaterialPropertyBlockController>()).Add(kv.Key!);
                    continue;
                }
                kv.Value.Apply(kv.Key.Mpb);
            }
            if (gone != null) foreach (var key in gone) dressings.Remove(key);
        }

        private sealed class Dressing
        {
            public readonly Renderer[] Renderers;
            private readonly MaterialPropertyBlock block = new MaterialPropertyBlock();
            private bool applied;
            private Vector4? color, offset;
            private float cutout;

            public Dressing(Renderer[] renderers) => Renderers = renderers;

            public void Apply(MaterialPropertyBlock source)
            {
                Vector4? c = source.HasColor(ColorId) || source.HasVector(ColorId) ? source.GetVector(ColorId) : (Vector4?)null;
                Vector4? o = source.HasVector(CutoutTexOffsetId) ? source.GetVector(CutoutTexOffsetId) : (Vector4?)null;
                var cut = source.HasFloat(CutoutId) ? source.GetFloat(CutoutId) : 0;
                if (applied && c == color && o == offset && cut == cutout) return;
                (applied, color, offset, cutout) = (true, c, o, cut);
                block.Clear();
                // Alpha 1, as in the game: ColorNoteVisuals sets the note colour with its
                // _defaultColorAlpha (1 on every note prefab) and Chroma keeps that alpha, while
                // ChroMapper passes the map's (43a25's black notes are [0, 0, 0, 0]).
                if (c is Vector4 cv) block.SetVector(ColorId, new Vector4(cv.x, cv.y, cv.z, 1));
                if (o is Vector4 ov) block.SetVector(CutoutTexOffsetId, ov);
                if (cut != 0) block.SetFloat(CutoutId, cut);
                foreach (var r in Renderers) if (r != null) r.SetPropertyBlock(block);
            }
        }

        private static IEnumerable<string> Tracks(JSONNode? node)
        {
            if (node == null) yield break;
            if (node.IsString) yield return node.Value;
            else if (node.IsArray)
                foreach (var n in node.Children)
                    if (n.IsString) yield return n.Value;
        }

        private static void SetLayer(Transform t, int layer)
        {
            t.gameObject.layer = layer;
            foreach (Transform child in t) SetLayer(child, layer);
        }
    }
}
