using UnityEngine;
using UnityEngine.Rendering;

namespace ChroMapper_PropPreview
{
    /// <summary>
    /// The rendering state Beat Saber gives Vivify content before a map changes anything, read
    /// from the game (1.40.8): the gameplay scenes have no Unity lights, light probes or
    /// reflection probes; the active scene (GameCore) has fog off, a flat black ambient and a
    /// reflection that is effectively black; the only quality level has no pixel lights, no
    /// shadows and no realtime reflection probes; the main camera renders without HDR. ChroMapper's
    /// editor scene lights, reflects and renders differently, so lit shaders (Standard, Poiyomi)
    /// came out brighter and flatter. While a map with a bundle is previewed this baseline is set
    /// every frame before the map's own SetRenderingSettings, and ChroMapper's values come back after.
    /// </summary>
    public class GameBaseline
    {
        private Cubemap? black;
        private Saved? saved;
        private Camera? ldrCamera;
        private bool ldrCameraHdr;

        private class Saved
        {
            public bool Fog;
            public AmbientMode AmbientMode;
            public Color Ambient, Sky, Equator, Ground;
            public float AmbientIntensity, ReflectionIntensity;
            public DefaultReflectionMode ReflectionMode;
            public Texture? Reflection;
            public int PixelLights;
            public ShadowQuality Shadows;
            public bool RealtimeProbes;
        }

        public void Apply()
        {
            if (black == null)
            {
                black = new Cubemap(1, TextureFormat.RGBA32, false) { name = "PropPreview black reflection" };
                for (var f = 0; f < 6; f++) black.SetPixel((CubemapFace)f, 0, 0, Color.clear);
                black.Apply();
            }
            saved ??= new Saved
            {
                Fog = RenderSettings.fog,
                AmbientMode = RenderSettings.ambientMode,
                Ambient = RenderSettings.ambientLight,
                Sky = RenderSettings.ambientSkyColor,
                Equator = RenderSettings.ambientEquatorColor,
                Ground = RenderSettings.ambientGroundColor,
                AmbientIntensity = RenderSettings.ambientIntensity,
                ReflectionIntensity = RenderSettings.reflectionIntensity,
                ReflectionMode = RenderSettings.defaultReflectionMode,
                Reflection = RenderSettings.customReflectionTexture,
                PixelLights = QualitySettings.pixelLightCount,
                Shadows = QualitySettings.shadows,
                RealtimeProbes = QualitySettings.realtimeReflectionProbes,
            };
            RenderSettings.fog = false;
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = Color.black;
            RenderSettings.ambientSkyColor = Color.black;
            RenderSettings.ambientEquatorColor = Color.black;
            RenderSettings.ambientGroundColor = Color.black;
            RenderSettings.ambientIntensity = 1;
            RenderSettings.reflectionIntensity = 1;
            RenderSettings.defaultReflectionMode = DefaultReflectionMode.Custom;
            RenderSettings.customReflectionTexture = black;
            QualitySettings.pixelLightCount = 0;
            QualitySettings.shadows = ShadowQuality.Disable;
            QualitySettings.realtimeReflectionProbes = false;
        }

        /// <summary>
        /// While the view camera renders: no HDR, like the game's (LDR, values clamped to 0..1), a
        /// depth texture, and the globals the game sets for shaders (MainEffectCore's base colour
        /// boost, SongTimeToShaderWriter, RandomValueToShader), which bundle shaders may read.
        /// </summary>
        public void OnPreRender(Camera cam, float songSeconds)
        {
            if (ldrCamera != cam)
            {
                RestoreCamera();
                ldrCamera = cam;
                ldrCameraHdr = cam.allowHDR;
            }
            cam.allowHDR = false;
            cam.depthTextureMode |= DepthTextureMode.Depth;
            Shader.SetGlobalFloat(BaseColorBoost, 1);
            Shader.SetGlobalFloat(BaseColorBoostThreshold, 0);
            Shader.SetGlobalVector(SongTime, new Vector4(songSeconds / 20, songSeconds, songSeconds * 2, songSeconds * 3));
            Shader.SetGlobalFloat(GlobalRandomValue, Random.value);
        }

        // The game's keywords (DEPTH_TEXTURE_ENABLED, MAIN_EFFECT_ENABLED, ENABLE_BLOOM_FOG) are
        // not set: ChroMapper's own shaders are copies of the game's and react to them too, and the
        // comparison with the game came out no closer with them.
        public void OnPostRender(Camera cam)
        {
        }

        private static readonly int BaseColorBoost = Shader.PropertyToID("_BaseColorBoost");
        private static readonly int BaseColorBoostThreshold = Shader.PropertyToID("_BaseColorBoostThreshold");
        private static readonly int SongTime = Shader.PropertyToID("_SongTime");
        private static readonly int GlobalRandomValue = Shader.PropertyToID("_GlobalRandomValue");

        public void Restore()
        {
            RestoreCamera();
            if (saved == null) return;
            RenderSettings.fog = saved.Fog;
            RenderSettings.ambientMode = saved.AmbientMode;
            RenderSettings.ambientLight = saved.Ambient;
            RenderSettings.ambientSkyColor = saved.Sky;
            RenderSettings.ambientEquatorColor = saved.Equator;
            RenderSettings.ambientGroundColor = saved.Ground;
            RenderSettings.ambientIntensity = saved.AmbientIntensity;
            RenderSettings.reflectionIntensity = saved.ReflectionIntensity;
            RenderSettings.defaultReflectionMode = saved.ReflectionMode;
            RenderSettings.customReflectionTexture = saved.Reflection;
            QualitySettings.pixelLightCount = saved.PixelLights;
            QualitySettings.shadows = saved.Shadows;
            QualitySettings.realtimeReflectionProbes = saved.RealtimeProbes;
            saved = null;
            RenderingSnapshot.UpdateAmbient();
        }

        private void RestoreCamera()
        {
            if (ldrCamera != null) ldrCamera.allowHDR = ldrCameraHdr;
            ldrCamera = null;
        }
    }
}
