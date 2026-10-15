using System;
using System.Collections.Generic;
using SimpleJSON;
using UnityEngine;

namespace ChroMapper_PropPreview
{
    /// <summary>
    /// The values one property (a material property, a shader global or a rendering setting) takes
    /// over the song, from the events that set it. Vivify's rules (SetMaterialProperty.cs): the
    /// latest event that has started wins; a static value applies at once (its duration does not
    /// make a transition); a point definition plays over the event's duration from its own first
    /// point (not from the current value), with the event's easing on the clock, and an event with
    /// no duration, or one already over, shows its last point.
    /// </summary>
    public class Timeline
    {
        public class Entry
        {
            public float Time;
            public float Duration;
            public Func<float, float> Easing = Easings.Linear;
            /// <summary>A static value (number, bool, string), or null when Points is set.</summary>
            public JSONNode? Static;
            public Points? Points;
        }

        private readonly List<Entry> entries = new List<Entry>();

        public void Add(Entry e)
        {
            var i = entries.FindLastIndex(x => x.Time <= e.Time);
            entries.Insert(i + 1, e);
        }

        /// <summary>The entry in effect at `time` (JSON beats) and how far it has played (0..1).</summary>
        public Entry? At(float time, out float progress)
        {
            progress = 1;
            // The last entry that has started (entries are in time order): binary search, since
            // a map can have hundreds of thousands of events on one property.
            int lo = 0, hi = entries.Count - 1, at = -1;
            while (lo <= hi)
            {
                var mid = (lo + hi) / 2;
                if (entries[mid].Time <= time)
                {
                    at = mid;
                    lo = mid + 1;
                }
                else hi = mid - 1;
            }
            var found = at < 0 ? null : entries[at];
            // Durations are in beats here; Vivify converts them at the BPM where the event starts.
            if (found != null && found.Duration > 0)
                progress = found.Easing(Mathf.Clamp01((time - found.Time) / found.Duration));
            return found;
        }

        /// <summary>A point-definition entry's value at `time`; null before the first entry.</summary>
        public Vector4? Vector(float time, out JSONNode? staticValue)
        {
            var e = At(time, out var progress);
            staticValue = e?.Static;
            if (e?.Points == null) return null;
            return e.Points.Evaluate(progress);
        }
    }
}
