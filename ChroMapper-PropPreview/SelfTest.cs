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

        private static void LogShader(string owner, Material m)
        {
            Log($"  material {m.name} on {owner}: shader {m.shader?.name ?? "null"}, "
                + $"supported={m.shader != null && m.shader.isSupported}, passes={m.passCount}, queue={m.renderQueue}");
            if (Environment.GetEnvironmentVariable("PROPPREVIEW_SELFTEST_PASSES") != "1" || m.shader == null) return;
            for (var i = 0; i < m.shader.passCount; i++)
                Log($"    pass {i} \"{m.shader.FindPassTagValue(i, new UnityEngine.Rendering.ShaderTagId("LightMode")).name}\""
                    + $" name {m.GetPassName(i)} enabled {m.GetShaderPassEnabled(m.GetPassName(i))}");
            Log($"    keywords {string.Join(" ", m.shaderKeywords)}");
            var stencil = new System.Collections.Generic.List<string>();
            for (var k = 0; k < m.shader.GetPropertyCount(); k++)
            {
                var prop = m.shader.GetPropertyName(k);
                if (prop.IndexOf("Stencil", StringComparison.OrdinalIgnoreCase) < 0) continue;
                var type = m.shader.GetPropertyType(k);
                stencil.Add($"{prop}={(type == UnityEngine.Rendering.ShaderPropertyType.Float || type == UnityEngine.Rendering.ShaderPropertyType.Range ? m.GetFloat(prop).ToString() : type.ToString())}");
            }
            if (stencil.Count > 0) Log($"    stencil {string.Join(" ", stencil)}");
            var textures = new System.Collections.Generic.List<string>();
            foreach (var name in m.GetTexturePropertyNames())
            {
                var tex = m.GetTexture(name);
                if (tex == null) continue;
                textures.Add($"{name}={tex.name} {(tex is Texture2D t2 ? t2.format.ToString() : tex.GetType().Name)} {tex.width}x{tex.height}");
            }
            var color = m.HasProperty("_Color") ? m.GetColor("_Color").ToString() : "-";
            Log($"    color {color} textures {string.Join(", ", textures)}");
        }

        private static void Log(string line) => Debug.Log($"PropPreview selftest: {line}");
    }
}
