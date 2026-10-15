using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace ChroMapper_PropPreview
{
    /// <summary>
    /// The RenderSettings that Vivify's SetRenderingSettings can change (its renderSettings
    /// category), captured so the preview can put ChroMapper's own values back. The skybox is not
    /// here: ChroMapper's bloomfog owns RenderSettings.skybox, so the preview draws a Vivify sky
    /// with a Skybox component on the camera instead.
    /// </summary>
    public class RenderingSnapshot
    {
        /// <summary>Settings by Vivify key, with how many numbers each value has.</summary>
        public static readonly Dictionary<string, int> Width = new Dictionary<string, int>
        {
            ["ambientIntensity"] = 1, ["flareFadeSpeed"] = 1, ["flareStrength"] = 1,
            ["fogDensity"] = 1, ["fogStartDistance"] = 1, ["fogEndDistance"] = 1,
            ["haloStrength"] = 1, ["reflectionIntensity"] = 1,
            ["fog"] = 1, ["ambientMode"] = 1, ["fogMode"] = 1, ["defaultReflectionMode"] = 1,
            ["defaultReflectionResolution"] = 1, ["reflectionBounces"] = 1,
            ["ambientLight"] = 4, ["ambientSkyColor"] = 4, ["ambientEquatorColor"] = 4,
            ["ambientGroundColor"] = 4, ["fogColor"] = 4, ["subtractiveShadowColor"] = 4,
            ["skybox"] = 1,
        };

        private float ambientIntensity, flareFadeSpeed, flareStrength, fogDensity, fogStart, fogEnd,
            haloStrength, reflectionIntensity;
        private bool fog;
        private AmbientMode ambientMode;
        private FogMode fogMode;
        private DefaultReflectionMode reflectionMode;
        private int reflectionResolution, reflectionBounces;
        private Color ambientLight, ambientSky, ambientEquator, ambientGround, fogColor, shadowColor;

        public static RenderingSnapshot Take() => new RenderingSnapshot
        {
            ambientIntensity = RenderSettings.ambientIntensity,
            flareFadeSpeed = RenderSettings.flareFadeSpeed,
            flareStrength = RenderSettings.flareStrength,
            fogDensity = RenderSettings.fogDensity,
            fogStart = RenderSettings.fogStartDistance,
            fogEnd = RenderSettings.fogEndDistance,
            haloStrength = RenderSettings.haloStrength,
            reflectionIntensity = RenderSettings.reflectionIntensity,
            fog = RenderSettings.fog,
            ambientMode = RenderSettings.ambientMode,
            fogMode = RenderSettings.fogMode,
            reflectionMode = RenderSettings.defaultReflectionMode,
            reflectionResolution = RenderSettings.defaultReflectionResolution,
            reflectionBounces = RenderSettings.reflectionBounces,
            ambientLight = RenderSettings.ambientLight,
            ambientSky = RenderSettings.ambientSkyColor,
            ambientEquator = RenderSettings.ambientEquatorColor,
            ambientGround = RenderSettings.ambientGroundColor,
            fogColor = RenderSettings.fogColor,
            shadowColor = RenderSettings.subtractiveShadowColor,
        };

        public void Restore()
        {
            RenderSettings.ambientIntensity = ambientIntensity;
            RenderSettings.flareFadeSpeed = flareFadeSpeed;
            RenderSettings.flareStrength = flareStrength;
            RenderSettings.fogDensity = fogDensity;
            RenderSettings.fogStartDistance = fogStart;
            RenderSettings.fogEndDistance = fogEnd;
            RenderSettings.haloStrength = haloStrength;
            RenderSettings.reflectionIntensity = reflectionIntensity;
            RenderSettings.fog = fog;
            RenderSettings.ambientMode = ambientMode;
            RenderSettings.fogMode = fogMode;
            RenderSettings.defaultReflectionMode = reflectionMode;
            RenderSettings.defaultReflectionResolution = reflectionResolution;
            RenderSettings.reflectionBounces = reflectionBounces;
            RenderSettings.ambientLight = ambientLight;
            RenderSettings.ambientSkyColor = ambientSky;
            RenderSettings.ambientEquatorColor = ambientEquator;
            RenderSettings.ambientGroundColor = ambientGround;
            RenderSettings.fogColor = fogColor;
            RenderSettings.subtractiveShadowColor = shadowColor;
        }

        /// <summary>Sets one setting; enums and ints are truncated, fog is on when nonzero (as Vivify).</summary>
        public static void Set(string key, Vector4 v)
        {
            var c = (Color)v;
            switch (key)
            {
                case "ambientIntensity": RenderSettings.ambientIntensity = v.x; break;
                case "flareFadeSpeed": RenderSettings.flareFadeSpeed = v.x; break;
                case "flareStrength": RenderSettings.flareStrength = v.x; break;
                case "fogDensity": RenderSettings.fogDensity = v.x; break;
                case "fogStartDistance": RenderSettings.fogStartDistance = v.x; break;
                case "fogEndDistance": RenderSettings.fogEndDistance = v.x; break;
                case "haloStrength": RenderSettings.haloStrength = v.x; break;
                case "reflectionIntensity": RenderSettings.reflectionIntensity = v.x; break;
                case "fog": RenderSettings.fog = v.x != 0; break;
                case "ambientMode": RenderSettings.ambientMode = (AmbientMode)(int)v.x; break;
                case "fogMode": RenderSettings.fogMode = (FogMode)(int)v.x; break;
                case "defaultReflectionMode": RenderSettings.defaultReflectionMode = (DefaultReflectionMode)(int)v.x; break;
                case "defaultReflectionResolution": RenderSettings.defaultReflectionResolution = (int)v.x; break;
                case "reflectionBounces": RenderSettings.reflectionBounces = (int)v.x; break;
                case "ambientLight": RenderSettings.ambientLight = c; break;
                case "ambientSkyColor": RenderSettings.ambientSkyColor = c; break;
                case "ambientEquatorColor": RenderSettings.ambientEquatorColor = c; break;
                case "ambientGroundColor": RenderSettings.ambientGroundColor = c; break;
                case "fogColor": RenderSettings.fogColor = c; break;
                case "subtractiveShadowColor": RenderSettings.subtractiveShadowColor = c; break;
            }
        }
    }
}
