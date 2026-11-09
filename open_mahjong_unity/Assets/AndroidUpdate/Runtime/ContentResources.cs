using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace OmUpdate {
    // Used only by the Android build-copy adapter. Source Resources calls on
    // Windows/WebGL are left intact, including their asynchronous behavior.
    public static class ContentResources {
        static readonly Dictionary<string, List<ResourceEntry>> entries = new Dictionary<string, List<ResourceEntry>>(StringComparer.OrdinalIgnoreCase);
        static readonly Dictionary<string, AssetBundle> bundles = new Dictionary<string, AssetBundle>();
        public static void Initialize(ContentIndex index, Dictionary<string, AssetBundle> loaded) {
            entries.Clear(); bundles.Clear();
            foreach (var pair in loaded) bundles.Add(pair.Key, pair.Value);
            foreach (var entry in index.resources ?? Array.Empty<ResourceEntry>()) {
                if (!entries.TryGetValue(entry.key, out var list)) entries.Add(entry.key, list = new List<ResourceEntry>());
                list.Add(entry);
            }
        }
        static ResourceEntry Entry(string path, Type type) => entries.TryGetValue(path, out var list)
            ? list.FirstOrDefault(e => type == typeof(UnityEngine.Object) || (e.sprites && type == typeof(Sprite))
                || (Type.GetType(e.type, false) is Type actual && type.IsAssignableFrom(actual))) : null;
        public static UnityEngine.Object Load(string path, Type type) {
#if UNITY_EDITOR
            return UnityEngine.Resources.Load(path, type);
#else
            var entry = Entry(path, type);
            if (entry == null) return null;
            return bundles[entry.bundle].LoadAsset(entry.asset, type);
#endif
        }
        public static UnityEngine.Object Load(string path) => Load(path, typeof(UnityEngine.Object));
        public static T Load<T>(string path) where T : UnityEngine.Object => Load(path, typeof(T)) as T;
        public static T[] LoadAll<T>(string path) where T : UnityEngine.Object => LoadAll(path, typeof(T)).Cast<T>().ToArray();
        public static UnityEngine.Object[] LoadAll(string path) => LoadAll(path, typeof(UnityEngine.Object));
        public static UnityEngine.Object[] LoadAll(string path, Type type) {
#if UNITY_EDITOR
            return UnityEngine.Resources.LoadAll(path, type);
#else
            var list = new List<UnityEngine.Object>();
            foreach (var pair in entries) {
                if (pair.Key.Equals(path, StringComparison.OrdinalIgnoreCase)
                    || pair.Key.StartsWith(path.TrimEnd('/') + "/", StringComparison.OrdinalIgnoreCase)
                    || path.Length == 0) foreach (var entry in pair.Value) list.AddRange(bundles[entry.bundle].LoadAssetWithSubAssets(entry.asset, type));
            }
            return list.ToArray();
#endif
        }
        public static ContentRequest LoadAsync<T>(string path) where T : UnityEngine.Object => new ContentRequest(path, typeof(T));
        public static ContentRequest LoadAsync(string path) => new ContentRequest(path, typeof(UnityEngine.Object));
        public static ContentRequest LoadAsync(string path, Type type) => new ContentRequest(path, type);
        public static void UnloadAsset(UnityEngine.Object asset) => UnityEngine.Resources.UnloadAsset(asset);
        public static T[] FindObjectsOfTypeAll<T>() where T : UnityEngine.Object => UnityEngine.Resources.FindObjectsOfTypeAll<T>();
        public static AsyncOperation UnloadUnusedAssets() => UnityEngine.Resources.UnloadUnusedAssets();
        public sealed class ContentRequest : CustomYieldInstruction {
            readonly AssetBundleRequest request;
            public event Action<ContentRequest> completed {
                add {
                    if (value == null) return;
                    if (request == null || request.isDone) value(this);
                    else request.completed += _ => value(this);
                }
                remove { /* callers in the resource adapter use one-shot lambdas */ }
            }
            public ContentRequest(string path, Type type) {
                var entry = Entry(path, type);
                if (entry != null) request = bundles[entry.bundle].LoadAssetAsync(entry.asset, type);
            }
            public UnityEngine.Object asset => request?.asset;
            public bool isDone => request == null || request.isDone;
            public float progress => request?.progress ?? 1;
            public override bool keepWaiting => !isDone;
        }
    }
}
