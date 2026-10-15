using System.Collections.Generic;
using Beatmap.Base.Customs;
using SimpleJSON;
using UnityEngine;

namespace ChroMapper_PropPreview
{
    /// <summary>
    /// Heck track parenting (AssignTrackParent) for prefabs, evaluated here. In the game a parent
    /// track gets its own object, the child track's objects are parented under it, and the child
    /// track's values become their local transform (Heck ParentObject, Vivify TransformController).
    /// ChroMapper's track animation splits a child track's rotation and position over two nested
    /// objects (the position ends up rotated) and doesn't carry the values over to directly attached
    /// objects, so for prefabs on child tracks the chain is computed from the map's AnimateTrack
    /// events: each track's transform from position/localPosition, rotation/localRotation and scale
    /// (the latest started event per property, played over its duration), composed parent first.
    /// On the prefab itself, as in Vivify, position and rotation are world values and only
    /// localPosition and localRotation are relative to the parents.
    /// </summary>
    public class ParentedTracks
    {
        private readonly Dictionary<string, string> parentOf = new Dictionary<string, string>();
        private readonly Dictionary<(string track, string prop), Timeline> props =
            new Dictionary<(string, string), Timeline>();
        private Dictionary<string, JSONArray> named = new Dictionary<string, JSONArray>();

        private static readonly string[] Props = { "position", "localPosition", "rotation", "localRotation", "scale" };

        public void Clear(Dictionary<string, JSONArray>? pointDefinitions)
        {
            parentOf.Clear();
            props.Clear();
            named = pointDefinitions ?? new Dictionary<string, JSONArray>();
        }

        public bool HasParent(string track) => parentOf.ContainsKey(track);

        public void Add(BaseCustomEvent ev)
        {
            if (ev.Type == "AssignTrackParent")
            {
                var parent = ev.Data["parentTrack"];
                if (parent == null || !parent.IsString) return;
                foreach (var child in CameraPreview.Tracks(ev.Data["childrenTracks"]))
                    if (child != parent.Value) parentOf[child] = parent.Value;
                return;
            }
            if (ev.Type != "AnimateTrack") return;
            var duration = ev.Data["duration"];
            foreach (var track in CameraPreview.Tracks(ev.Data["track"]))
            foreach (var prop in Props)
            {
                var value = ev.Data[prop];
                if (value == null) continue;
                if (value.IsString && named.TryGetValue(value.Value, out var def)) value = def;
                var entry = new Timeline.Entry
                {
                    Time = ev.JsonTime,
                    Duration = duration != null && duration.IsNumber ? duration.AsFloat : 0,
                    Easing = Easings.Get(ev.Data["easing"]),
                    Points = Points.Parse(value, 3),
                };
                if (entry.Points == null) continue;
                if (!props.TryGetValue((track, prop), out var timeline)) props[(track, prop)] = timeline = new Timeline();
                timeline.Add(entry);
            }
        }

        /// <summary>A track's own transform values at `time` (null parts: not animated).</summary>
        public (Vector3? pos, Quaternion? rot, Vector3? scale) Local(string track, float time)
        {
            var v = Values(track, time);
            return (v.localPos ?? v.pos, v.localRot ?? v.rot, v.scale);
        }

        /// <summary>
        /// The values split as Vivify applies them (TransformController): localPosition and
        /// localRotation are relative to the parent; position and rotation are world values.
        /// </summary>
        public (Vector3? localPos, Vector3? pos, Quaternion? localRot, Quaternion? rot, Vector3? scale) Values(string track, float time)
        {
            Vector3? V(string prop) => props.TryGetValue((track, prop), out var tl) && tl.Vector(time, out _) is Vector4 v
                ? new Vector3(v.x, v.y, v.z) : (Vector3?)null;
            Quaternion? Q(string prop) => V(prop) is Vector3 e ? Quaternion.Euler(e) : (Quaternion?)null;
            return (V("localPosition"), V("position"), Q("localRotation"), Q("rotation"), V("scale"));
        }

        /// <summary>The world transform of the parents of `track` (identity when it has none).</summary>
        public Matrix4x4 ParentWorld(string track, float time)
        {
            var m = Matrix4x4.identity;
            var chain = new List<string>();
            var t = track;
            while (parentOf.TryGetValue(t, out var parent) && !chain.Contains(parent) && chain.Count < 16)
            {
                chain.Add(parent);
                t = parent;
            }
            for (var i = chain.Count - 1; i >= 0; i--)
            {
                var (pos, rot, scale) = Local(chain[i], time);
                m *= Matrix4x4.TRS(pos ?? Vector3.zero, rot ?? Quaternion.identity, scale ?? Vector3.one);
            }
            return m;
        }
    }
}
