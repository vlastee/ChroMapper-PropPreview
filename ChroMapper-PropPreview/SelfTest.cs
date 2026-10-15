using System;
using System.Linq;
using UnityEngine;

namespace ChroMapper_PropPreview
{
    /// <summary>
    /// Unattended check that ChroMapper can use a bundle: with PROPPREVIEW_SELFTEST=&lt;bundle
    /// path&gt;, the plugin loads it at startup, logs every asset and whether each material's shader
    /// runs on this graphics API, then quits ChroMapper. Lines start with "PropPreview selftest:".
    /// </summary>
    public static class SelfTest
    {
        public static bool Run()
        {
            var path = Environment.GetEnvironmentVariable("PROPPREVIEW_SELFTEST");
            if (string.IsNullOrEmpty(path)) return false;
            try
            {
                Log($"Unity {Application.unityVersion}, {SystemInfo.graphicsDeviceType}, bundle {path}");
                var bundle = AssetBundle.LoadFromFile(path);
                if (bundle == null)
                {
                    Log("FAIL: AssetBundle.LoadFromFile returned null");
                    return true;
                }
                foreach (var name in bundle.GetAllAssetNames())
                {
                    var asset = bundle.LoadAsset(name);
                    Log($"asset {name}: {(asset == null ? "null" : asset.GetType().Name)}");
                    if (asset is Material m) LogShader(name, m);
                    if (asset is GameObject go)
                    {
                        foreach (var text in go.GetComponentsInChildren<TMPro.TMP_Text>(true))
                        {
                            var font = text.font;
                            Log($"  text \"{text.text}\" on {text.name}: font {(font == null ? "null" : font.name)}"
                                + $", font material {(font == null || font.material == null ? "null" : font.material.shader?.name)}"
                                + $", atlas {(font == null || font.atlasTexture == null ? "null" : font.atlasTexture.name)}"
                                + $", shared material {(text.fontSharedMaterial == null ? "null" : text.fontSharedMaterial.shader?.name + " tex " + (text.fontSharedMaterial.HasProperty("_MainTex") ? text.fontSharedMaterial.GetTexture("_MainTex")?.name ?? "null" : "-"))}"
                                + $", atlas format {(font?.atlasTexture == null ? "-" : font.atlasTexture.format.ToString() + " " + font.atlasTexture.width + "x" + font.atlasTexture.height)}"
                                + $", glyphs {(font == null ? 0 : font.glyphTable?.Count ?? -1)}, characters {(font == null ? 0 : font.characterTable?.Count ?? -1)}");
                        }
                        foreach (var r in go.GetComponentsInChildren<Renderer>(true))
                        foreach (var rm in r.sharedMaterials.Where(x => x != null))
                            LogShader($"{name} / {r.name}", rm);
                    }
                }
                bundle.Unload(true);
                Log("done");
            }
            catch (Exception e)
            {
                Log($"FAIL: {e}");
            }
            finally
            {
                Application.Quit();
            }
            return true;
        }

        private static void LogShader(string owner, Material m) =>
            Log($"  material {m.name} on {owner}: shader {m.shader?.name ?? "null"}, "
                + $"supported={m.shader != null && m.shader.isSupported}, passes={m.passCount}");

        private static void Log(string line) => Debug.Log($"PropPreview selftest: {line}");
    }
}
