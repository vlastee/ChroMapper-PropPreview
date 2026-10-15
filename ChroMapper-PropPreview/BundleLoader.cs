using System.Collections.Generic;
using System.IO;
using System.Linq;
using SimpleJSON;
using UnityEngine;

namespace ChroMapper_PropPreview
{
    /// <summary>The map's Vivify asset bundle, loaded for preview.</summary>
    public class BundleLoader
    {
        // Preferred first: a Linux build only for preview (OpenGL/Vulkan shaders), then the
        // game's Windows bundle, whose Direct3D 11 shaders only draw on Windows or under Proton.
        private static readonly string[] BundleNames = { "bundleLinux.vivify", "bundleWindows2021.vivify" };

        public readonly AssetBundle Bundle;
        public readonly string FileName;
        private readonly Dictionary<string, Object?> assets = new Dictionary<string, Object?>();
        private readonly HashSet<string> missing = new HashSet<string>();

        private BundleLoader(AssetBundle bundle, string fileName)
        {
            Bundle = bundle;
            FileName = fileName;
        }

        /// <summary>Loads the map's bundle, or returns null when the map has none.</summary>
        public static BundleLoader? ForMap(string mapDir)
        {
            var path = BundleNames.Select(n => Path.Combine(mapDir, n)).FirstOrDefault(File.Exists);
            if (path == null) return null;
            // No CRC check: the checksum in Info.dat belongs to the Windows bundle.
            var bundle = AssetBundle.LoadFromFile(path);
            if (bundle == null)
            {
                Debug.LogError($"PropPreview: could not load {path}");
                return null;
            }
            Debug.Log($"PropPreview: loaded {Path.GetFileName(path)} ({bundle.GetAllAssetNames().Length} assets)");
            return new BundleLoader(bundle, Path.GetFileName(path));
        }

        /// <summary>An asset by its bundle path ("assets/.../x.prefab", exact, as Vivify looks it up).</summary>
        public T? Load<T>(JSONNode? path) where T : Object
        {
            if (path == null || !path.IsString) return null;
            var name = path.Value;
            if (!assets.TryGetValue(name, out var asset))
            {
                asset = Bundle.LoadAsset<Object>(name);
                assets[name] = asset;
            }
            if (asset is T t) return t;
            if (missing.Add(name))
                Debug.LogWarning($"PropPreview: {FileName} has no {typeof(T).Name} \"{name}\"");
            return null;
        }

        public void Unload() => Bundle.Unload(true);
    }
}
