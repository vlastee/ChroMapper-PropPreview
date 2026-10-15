using System.Collections.Generic;
using Beatmap.Animations;
using HarmonyLib;
using SimpleJSON;
using UnityEngine;

namespace ChroMapper_PropPreview
{
    /// <summary>
    /// Heck base providers in point definitions, which ChroMapper's own parsers
    /// (PointDataParsers) don't read: a value may be a provider name ("baseHeadPosition") that
    /// stands for all its components, or a swizzle of them ("baseHeadPosition.xy", ".s10" smoothing
    /// is ignored), mixed with numbers until the type's width is filled (Heck Values.cs,
    /// BaseProviderManager.cs). Modifier arrays (["opAdd", ...]) may follow, then the point's time.
    /// Without a player, providers have fixed values: the head where the game's desktop camera is
    /// (0, 1.7, 0) facing forward, hands beside it, colours from the map's scheme, neutral scores.
    /// </summary>
    public static class BaseProviders
    {
        public static void Patch(Harmony harmony)
        {
            var parsers = typeof(PointDataParsers);
            harmony.Patch(AccessTools.Method(parsers, nameof(PointDataParsers.ParseFloat)),
                new HarmonyMethod(typeof(BaseProviders), nameof(ParseFloat)));
            harmony.Patch(AccessTools.Method(parsers, nameof(PointDataParsers.ParseVector3)),
                new HarmonyMethod(typeof(BaseProviders), nameof(ParseVector3)));
            harmony.Patch(AccessTools.Method(parsers, nameof(PointDataParsers.ParseQuaternion)),
                new HarmonyMethod(typeof(BaseProviders), nameof(ParseQuaternion)));
            harmony.Patch(AccessTools.Method(parsers, nameof(PointDataParsers.ParseColor)),
                new HarmonyMethod(typeof(BaseProviders), nameof(ParseColor)));
        }

        // Harmony prefixes: returning false replaces ChroMapper's parser.
        private static bool ParseFloat(JSONArray data, ref int i, ref float __result)
        {
            __result = Values(data, ref i, 1)[0];
            return false;
        }

        private static bool ParseVector3(JSONArray data, ref int i, ref Vector3 __result)
        {
            var v = Values(data, ref i, 3);
            __result = new Vector3(v[0], v[1], v[2]);
            return false;
        }

        private static bool ParseQuaternion(JSONArray data, ref int i, ref Quaternion __result)
        {
            var v = Values(data, ref i, 3);
            __result = Quaternion.Euler(v[0], v[1], v[2]);
            return false;
        }

        private static bool ParseColor(JSONArray data, ref int i, ref Color __result)
        {
            var v = Values(data, ref i, 4);
            __result = new Color(v[0], v[1], v[2], v[3]);
            return false;
        }

        /// <summary>
        /// The point's first `width` components from data[i...] (numbers and providers), then any
        /// modifier arrays applied in order; i ends on the point's time.
        /// </summary>
        public static float[] Values(JSONArray data, ref int i, int width)
        {
            var result = new List<float>(width);
            while (result.Count < width && i < data.Count)
            {
                var node = data[i];
                if (node.IsString)
                {
                    result.AddRange(Provider(node.Value));
                    i++;
                }
                else if (node.IsNumber)
                {
                    result.Add(node.AsFloat);
                    i++;
                }
                else break;
            }
            while (result.Count < width) result.Add(result.Count > 0 && width == 4 && result.Count == 3 ? 1 : 0);
            if (result.Count > width) result.RemoveRange(width, result.Count - width);
            // Modifiers: [values..., "opX"] arrays right after the values.
            while (i < data.Count && data[i] is JSONArray modifier)
            {
                i++;
                var j = 0;
                var other = Values(modifier, ref j, width);
                var op = j < modifier.Count ? modifier[j].Value : "";
                for (var k = 0; k < width; k++)
                {
                    result[k] = op switch
                    {
                        "opAdd" => result[k] + other[k],
                        "opSub" => result[k] - other[k],
                        "opMul" => result[k] * other[k],
                        "opDiv" => other[k] != 0 ? result[k] / other[k] : result[k],
                        _ => result[k],
                    };
                }
            }
            return result.ToArray();
        }

        /// <summary>A provider's components, swizzled when the name has ".xyzw" parts.</summary>
        private static float[] Provider(string key)
        {
            var parts = key.Split('.');
            var values = Base(parts[0]);
            for (var p = 1; p < parts.Length; p++)
            {
                var part = parts[p];
                if (part.Length == 0 || part[0] == 's') continue; // smoothing: no effect when static
                var swizzled = new float[part.Length];
                for (var c = 0; c < part.Length; c++)
                {
                    var index = "xyzw".IndexOf(part[c]);
                    swizzled[c] = index >= 0 && index < values.Length ? values[index] : 0;
                }
                values = swizzled;
            }
            return values;
        }

        private static float[] Base(string name)
        {
            var scheme = PointDataParsers.ColorScheme;
            float[] C(Color c) => new[] { c.r, c.g, c.b, c.a };
            switch (name)
            {
                case "baseHeadPosition": case "baseHeadLocalPosition": return new[] { 0f, 1.7f, 0f };
                case "baseLeftHandPosition": case "baseLeftHandLocalPosition": return new[] { -0.3f, 1.2f, 0.3f };
                case "baseRightHandPosition": case "baseRightHandLocalPosition": return new[] { 0.3f, 1.2f, 0.3f };
                case "baseHeadRotation": case "baseHeadLocalRotation":
                case "baseLeftHandRotation": case "baseLeftHandLocalRotation":
                case "baseRightHandRotation": case "baseRightHandLocalRotation":
                    return new[] { 0f, 0f, 0f };
                case "baseHeadLocalScale": case "baseLeftHandLocalScale": case "baseRightHandLocalScale":
                    return new[] { 1f, 1f, 1f };
                case "baseCombo": case "baseMultipliedScore": case "baseModifiedScore":
                case "baseImmediateMaxPossibleMultipliedScore": case "baseImmediateMaxPossibleModifiedScore":
                case "baseSongTime":
                    return new[] { 0f };
                case "baseRelativeScore": case "baseMultiplier": return new[] { 1f };
                case "baseEnergy": return new[] { 0.5f };
                case "baseSongLength": return new[] { BeatSaberSongContainer.Instance?.LoadedSongLength ?? 0f };
                case "basePlayerHeight": return new[] { 1.8f };
            }
            if (scheme == null) return new[] { 1f, 1f, 1f, 1f };
            switch (name)
            {
                case "baseNote0Color": case "baseSaberAColor": return C(scheme.LeftNoteColor);
                case "baseNote1Color": case "baseSaberBColor": return C(scheme.RightNoteColor);
                case "baseEnvironmentColor0": return C(scheme.EnvironmentLeftColor);
                case "baseEnvironmentColor1": return C(scheme.EnvironmentRightColor);
                case "baseEnvironmentColorW": return C(scheme.EnvironmentWhiteColor);
                case "baseEnvironmentColor0Boost": return C(scheme.EnvironmentLeftBoostColor);
                case "baseEnvironmentColor1Boost": return C(scheme.EnvironmentRightBoostColor);
                case "baseEnvironmentColorWBoost": return C(scheme.EnvironmentWhiteBoostColor);
                case "baseObstaclesColor": return C(scheme.ObstacleColor);
                default: return new[] { 0f };
            }
        }
    }
}
