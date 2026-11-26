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

        static bool IsComponent(Type type) => type != null && typeof(Component).IsAssignableFrom(type);

        ResourceEntry Find(string path, Type type) {
            if (!entries.TryGetValue(path, out var list)) return null;
            if (list.Count == 1) return list[0];
            Type lookup = IsComponent(type) ? typeof(GameObject) : type;
            return list.Find(entry => lookup == typeof(Object) || (entry.sprites && lookup == typeof(Sprite))
                || (Type.GetType(entry.type, false) is Type actual && lookup.IsAssignableFrom(actual)));
        }

        protected override Object Load(string path, Type systemTypeInstance) {
            var entry = Find(path, systemTypeInstance);
            if (entry == null) return base.Load(path, systemTypeInstance);
            if (IsComponent(systemTypeInstance))
                return (bundles[entry.bundle].LoadAsset(entry.asset, typeof(GameObject)) as GameObject)?.GetComponent(systemTypeInstance);
            return bundles[entry.bundle].LoadAsset(entry.asset, systemTypeInstance);
        }

        protected override ResourceRequest LoadAsync(string path, Type systemTypeInstance) {
            var entry = Find(path, systemTypeInstance);
            return entry == null ? base.LoadAsync(path, systemTypeInstance) : bundles[entry.bundle].LoadAssetAsync(entry.asset, systemTypeInstance);
        }

        protected override Object[] LoadAll(string path, Type systemTypeInstance) {
            var found = new List<Object>();
            string prefix = path.TrimEnd('/') + "/";
            bool component = IsComponent(systemTypeInstance);
            foreach (var pair in entries) {
                if (path.Length != 0 && !pair.Key.Equals(path, StringComparison.OrdinalIgnoreCase)
                    && !pair.Key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;
                foreach (var entry in pair.Value) {
                    if (component) {
                        var go = bundles[entry.bundle].LoadAsset(entry.asset, typeof(GameObject)) as GameObject;
                        var comp = go != null ? go.GetComponent(systemTypeInstance) : null;
                        if (comp != null) found.Add(comp);
                    } else found.AddRange(bundles[entry.bundle].LoadAssetWithSubAssets(entry.asset, systemTypeInstance));
                }
            }
            return found.Count == 0 ? base.LoadAll(path, systemTypeInstance) : found.ToArray();
        }
    }
}
#endif
