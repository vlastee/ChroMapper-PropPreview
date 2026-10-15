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
    /// model stays only while the set still holds it. The prefab's renderers join the note's
    /// colour block, so they get its _Color and Noodle dissolve, as in the game. A dot note uses
    /// anyDirectionAsset and nothing else. Vivify applies an assignment to notes alive at the
    /// event and to notes spawned later; here a note takes the assignment in effect at its own beat.
    /// Sabers are not previewed: ChroMapper shows none.
    /// </summary>
    public class NotePrefabs
    {
        private const string Marker = "PropPreview note prefab";

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
            {
                if (child.name != Marker) continue;
                mpb.Remove(child.GetComponentsInChildren<Renderer>(true));
                Object.Destroy(child.gameObject);
            }
            var keepVanilla = set.Contains(null);
            Dressed++;
            foreach (var r in vanilla) if (r != null) r.enabled = keepVanilla;
            if (extra != null) extra.ShowRenderer(keepVanilla);
            foreach (var prefab in set)
            {
                if (prefab == null) continue;
                var go = Object.Instantiate(prefab, parent, false);
                TextRepair.Repair(go);
                go.name = Marker;
                Attached++;
                SetLayer(go.transform, parent.gameObject.layer);
                mpb.Add(go.GetComponentsInChildren<Renderer>(true));
            }
            mpb.ApplyChanges();
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
