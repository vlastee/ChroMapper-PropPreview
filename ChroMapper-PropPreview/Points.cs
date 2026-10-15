using System;
using System.Collections.Generic;
using SimpleJSON;
using UnityEngine;

namespace ChroMapper_PropPreview
{
    /// <summary>
    /// A Heck point definition as Vivify reads it for property values: either one flat point
    /// ([v...], time 0) or [[v..., time, "easeX"?, "lerpHSV"?], ...] with times in 0..1. Before
    /// the first point the first value holds, after the last the last value holds; each point's
    /// easing shapes the segment that ends at it (Heck PointDefinition.cs).
    /// </summary>
    public class Points
    {
        private readonly struct Point
        {
            public readonly Vector4 Value;
            public readonly float Time;
            public readonly Func<float, float> Easing;
            public readonly bool Hsv;

            public Point(Vector4 value, float time, Func<float, float> easing, bool hsv)
            {
                Value = value;
                Time = time;
                Easing = easing;
                Hsv = hsv;
            }
        }

        private readonly Point[] points;

        /// <summary>Values per point (1 for floats, 4 for colours and vectors).</summary>
        public readonly int Width;

        private Points(Point[] points, int width)
        {
            this.points = points;
            Width = width;
        }

        /// <summary>Parses `node` as points of `width` values; null when it is not a point array.</summary>
        public static Points? Parse(JSONNode node, int width)
        {
            if (node == null || !node.IsArray || node.Count == 0) return null;
            var rows = node[0].IsArray ? node.AsArray : WrapFlat(node.AsArray);
            var list = new List<Point>();
            foreach (var row in rows.Children)
            {
                if (!row.IsArray) continue;
                // Values may be numbers or Heck base providers ("baseHeadPosition.xy").
                var i = 0;
                var values = BaseProviders.Values(row.AsArray, ref i, width);
                var value = Vector4.zero;
                for (var k = 0; k < width; k++) value[k] = values[k];
                var time = i < row.Count && row[i].IsNumber ? row[i].AsFloat : 0f;
                Func<float, float> easing = Easings.Linear;
                var hsv = false;
                for (var k = i + 1; k < row.Count; k++)
                {
                    if (!row[k].IsString) continue;
                    var s = row[k].Value;
                    if (s == "lerpHSV") hsv = true;
                    else if (s.StartsWith("ease")) easing = Easings.Get(s);
                }
                list.Add(new Point(value, time, easing, hsv));
            }
            return list.Count == 0 ? null : new Points(list.ToArray(), width);
        }

        private static JSONArray WrapFlat(JSONArray flat)
        {
            var row = new JSONArray();
            foreach (var v in flat.Children) row.Add(v);
            row.Add(0);
            var rows = new JSONArray();
            rows.Add(row);
            return rows;
        }

        public Vector4 Evaluate(float t)
        {
            if (points.Length == 1 || t <= points[0].Time) return points[0].Value;
            var last = points[points.Length - 1];
            if (t >= last.Time) return last.Value;
            var i = 1;
            while (points[i].Time < t) i++;
            var a = points[i - 1];
            var b = points[i];
            var span = b.Time - a.Time;
            var f = span <= 0 ? 1 : b.Easing((t - a.Time) / span);
            return b.Hsv ? LerpHsv(a.Value, b.Value, f) : Vector4.LerpUnclamped(a.Value, b.Value, f);
        }

        private static Vector4 LerpHsv(Vector4 a, Vector4 b, float f)
        {
            Color.RGBToHSV(a, out var h1, out var s1, out var v1);
            Color.RGBToHSV(b, out var h2, out var s2, out var v2);
            var c = Color.HSVToRGB(Mathf.LerpUnclamped(h1, h2, f), Mathf.LerpUnclamped(s1, s2, f),
                Mathf.LerpUnclamped(v1, v2, f), false);
            c.a = Mathf.LerpUnclamped(a.w, b.w, f);
            return c;
        }
    }

    /// <summary>Heck's easings (Animation/Easings.cs): easeLinear, easeStep, ease{In,Out,InOut}X.</summary>
    public static class Easings
    {
        public static float Linear(float t) => t;

        public static Func<float, float> Get(string? name)
        {
            if (string.IsNullOrEmpty(name) || name == "easeLinear") return Linear;
            if (name == "easeStep") return t => t >= 1 ? 1 : 0;
            Func<float, float>? inFn = null;
            string rest;
            if (name!.StartsWith("easeInOut")) rest = name.Substring(9);
            else if (name.StartsWith("easeIn")) rest = name.Substring(6);
            else if (name.StartsWith("easeOut")) rest = name.Substring(7);
            else return Linear;
            switch (rest)
            {
                case "Sine": inFn = t => 1 - Mathf.Cos(t * Mathf.PI / 2); break;
                case "Quad": inFn = t => t * t; break;
                case "Cubic": inFn = t => t * t * t; break;
                case "Quart": inFn = t => t * t * t * t; break;
                case "Quint": inFn = t => t * t * t * t * t; break;
                case "Expo": inFn = t => t <= 0 ? 0 : Mathf.Pow(2, 10 * t - 10); break;
                case "Circ": inFn = t => 1 - Mathf.Sqrt(1 - t * t); break;
                case "Back": inFn = t => 2.70158f * t * t * t - 1.70158f * t * t; break;
                case "Elastic":
                    inFn = t => t <= 0 ? 0 : t >= 1 ? 1
                        : -Mathf.Pow(2, 10 * t - 10) * Mathf.Sin((t * 10 - 10.75f) * (2 * Mathf.PI / 3));
                    break;
                case "Bounce": inFn = t => 1 - BounceOut(1 - t); break;
            }
            if (inFn == null) return Linear;
            var f = inFn;
            if (name.StartsWith("easeInOut"))
                return t => t < 0.5f ? f(2 * t) / 2 : 1 - f(2 - 2 * t) / 2;
            if (name.StartsWith("easeOut")) return t => 1 - f(1 - t);
            return f;
        }

        private static float BounceOut(float t)
        {
            const float n = 7.5625f, d = 2.75f;
            if (t < 1 / d) return n * t * t;
            if (t < 2 / d) return n * (t -= 1.5f / d) * t + 0.75f;
            if (t < 2.5 / d) return n * (t -= 2.25f / d) * t + 0.9375f;
            return n * (t -= 2.625f / d) * t + 0.984375f;
        }
    }
}
