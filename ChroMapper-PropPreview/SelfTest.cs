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
