using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace OmUpdate {
    public static class ContentLoader {
        public static async Task Load(string directory, ContentIndex index) {
            var timing = System.Diagnostics.Stopwatch.StartNew();
            long previous = 0;
            void Stage(string name) {
                long now = timing.ElapsedMilliseconds;
                Debug.Log("[AndroidContent] startup " + name + " " + (now - previous) + "ms (total " + now + "ms)");
                previous = now;
            }
            // These DLLs were copied from the published framework's archived
            // stripped AOT output, never from an unrelated content-only build.
            var api = Type.GetType("HybridCLR.RuntimeApi, HybridCLR.Runtime");
            var mode = Type.GetType("HybridCLR.HomologousImageMode, HybridCLR.Runtime");
            if (api == null || mode == null) throw new InvalidOperationException("当前安装包未包含代码热更新运行环境。");
            var supplement = api.GetMethod("LoadMetadataForAOTAssembly", new[] { typeof(byte[]), mode });
            foreach (var name in index.metadata ?? Array.Empty<string>()) {
                var result = supplement.Invoke(null, new object[] { File.ReadAllBytes(ContentStore.SafePath(directory, "metadata/" + name)), Enum.ToObject(mode, 1) });
                int code = Convert.ToInt32(result);
                if (code != 0) throw new InvalidDataException("基础程序集元数据加载失败：" + name + " (" + code + ")");
            }
            Stage("metadata");
            var loaded = new List<Assembly>();
            foreach (var name in index.assemblies) loaded.Add(Assembly.Load(File.ReadAllBytes(ContentStore.SafePath(directory, "code/" + name))));
            Stage("assemblies");
            var bundles = new Dictionary<string, AssetBundle>();
            foreach (var name in index.bundles) {
                var request = AssetBundle.LoadFromFileAsync(ContentStore.SafePath(directory, name));
                while (!request.isDone) await Task.Yield();
                if (!request.assetBundle) throw new InvalidDataException("无法加载资源包：" + name);
                bundles.Add(name, request.assetBundle);
            }
            ContentResources.Initialize(index, bundles);
            Stage("bundles");
            var initializers = ResolveInitializers(loaded, index.initializers);
            Stage("initializer lookup");
            Initialize(initializers, RuntimeInitializeLoadType.SubsystemRegistration);
            Initialize(initializers, RuntimeInitializeLoadType.AfterAssembliesLoaded);
            Initialize(initializers, RuntimeInitializeLoadType.BeforeSplashScreen);
            Initialize(initializers, RuntimeInitializeLoadType.BeforeSceneLoad);
            Stage("before-scene callbacks");
            var load = SceneManager.LoadSceneAsync(index.scene, LoadSceneMode.Single);
            while (!load.isDone) await Task.Yield();
            Stage("scene");
            Initialize(initializers, RuntimeInitializeLoadType.AfterSceneLoad);
            Stage("after-scene callbacks");
        }
        static List<KeyValuePair<RuntimeInitializeLoadType, MethodInfo>> ResolveInitializers(IEnumerable<Assembly> assemblies, ContentInitializer[] index) {
            var result = new List<KeyValuePair<RuntimeInitializeLoadType, MethodInfo>>();
            if (index != null) {
                var byName = assemblies.ToDictionary(a => a.GetName().Name, StringComparer.Ordinal);
                foreach (var entry in index) {
                    if (entry == null || !Enum.IsDefined(typeof(RuntimeInitializeLoadType), entry.phase)
                        || !byName.TryGetValue(entry.assembly, out var assembly)) throw new InvalidDataException("无效的游戏启动回调索引。");
                    var type = assembly.GetType(entry.type, true);
                    var method = type.GetMethod(entry.method, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly,
                        null, Type.EmptyTypes, null);
                    if (method == null || method.ContainsGenericParameters) throw new InvalidDataException("无效的游戏启动回调：" + entry.type + "." + entry.method);
                    result.Add(new KeyValuePair<RuntimeInitializeLoadType, MethodInfo>((RuntimeInitializeLoadType)entry.phase, method));
                }
                return result;
            }
            // Older packages have no build-time index. Discover once, not once
            // per phase: broad reflection on interpreted game code is costly.
            foreach (var assembly in assemblies)
                foreach (var type in assembly.GetTypes())
                    foreach (var method in type.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)) {
                        var attr = method.GetCustomAttribute<RuntimeInitializeOnLoadMethodAttribute>();
                        if (attr != null) {
                            if (method.GetParameters().Length != 0) throw new InvalidDataException("无效的游戏启动回调：" + method);
                            result.Add(new KeyValuePair<RuntimeInitializeLoadType, MethodInfo>(attr.loadType, method));
                        }
                    }
            return result;
        }
        static void Initialize(List<KeyValuePair<RuntimeInitializeLoadType, MethodInfo>> methods, RuntimeInitializeLoadType phase) {
            foreach (var entry in methods) if (entry.Key == phase) entry.Value.Invoke(null, null);
        }
    }
}
