#if UNITY_WEBGL || UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEngine;
using Object = UnityEngine.Object;

namespace OmUpdate {
    // The game's existing Resources calls, including calls inside packages,
    // retain their native ResourceRequest, completion and unload semantics.
    public sealed class WebTextureResources : ResourcesAPI {
        readonly Dictionary<string, List<ResourceEntry>> entries = new Dictionary<string, List<ResourceEntry>>(StringComparer.OrdinalIgnoreCase);
        readonly Dictionary<string, AssetBundle> bundles;

        public WebTextureResources(ResourceEntry[] resources, Dictionary<string, AssetBundle> bundles) {
            this.bundles = bundles;
            foreach (var entry in resources) {
                if (!entries.TryGetValue(entry.key, out var list)) entries[entry.key] = list = new List<ResourceEntry>();
                list.Add(entry);
            }
        }

        ResourceEntry Find(string path, Type type) {
            if (!entries.TryGetValue(path, out var list)) return null;
            if (list.Count == 1) return list[0];
            return list.Find(entry => type == typeof(Object) || (entry.sprites && type == typeof(Sprite))
                || (Type.GetType(entry.type, false) is Type actual && type.IsAssignableFrom(actual)));
        }

        protected override Object Load(string path, Type systemTypeInstance) {
            var entry = Find(path, systemTypeInstance);
            return entry == null ? base.Load(path, systemTypeInstance) : bundles[entry.bundle].LoadAsset(entry.asset, systemTypeInstance);
        }

        protected override ResourceRequest LoadAsync(string path, Type systemTypeInstance) {
            var entry = Find(path, systemTypeInstance);
            return entry == null ? base.LoadAsync(path, systemTypeInstance) : bundles[entry.bundle].LoadAssetAsync(entry.asset, systemTypeInstance);
        }

        protected override Object[] LoadAll(string path, Type systemTypeInstance) {
            var found = new List<Object>();
            string prefix = path.TrimEnd('/') + "/";
            foreach (var pair in entries) {
                if (path.Length != 0 && !pair.Key.Equals(path, StringComparison.OrdinalIgnoreCase)
                    && !pair.Key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;
                foreach (var entry in pair.Value)
                    found.AddRange(bundles[entry.bundle].LoadAssetWithSubAssets(entry.asset, systemTypeInstance));
            }
            return found.Count == 0 ? base.LoadAll(path, systemTypeInstance) : found.ToArray();
        }
    }
}
#endif
