#if UNITY_WEBGL || UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace OmUpdate {
    public sealed class WebTextureBootstrap : MonoBehaviour {
        public WebTextureManifest content;
#if OM_WEB_TEXTURE_PROFILES && UNITY_WEBGL && !UNITY_EDITOR
        [DllImport("__Internal")] static extern int OmUseWebTextureASTC(int linear);
        [DllImport("__Internal")] static extern void OmMountWebBundle(string url, string path, int size);
        [DllImport("__Internal")] static extern float OmWebBundleProgress();
        [DllImport("__Internal")] static extern void OmNotifyWebTextureProgress(int completed, int total, float progress, int phase);
        IEnumerator Start() {
            int selected = OmUseWebTextureASTC(QualitySettings.activeColorSpace == ColorSpace.Linear ? 1 : 0);
            if (selected < 0) {
                OmNotifyWebTextureProgress(0, 0, 0, 4);
                Debug.LogError("[WebTexture] Invalid preloaded texture profile.");
                yield break;
            }
            bool useAstc = selected != 0;
            var files = useAstc ? content.astc : content.dxt;
            string profile = useAstc ? "ASTC" : "DXT";
            var bundles = new Dictionary<string, AssetBundle>();
            OmNotifyWebTextureProgress(0, files.Length, 0, 0);
            foreach (var file in files) {
                string url = Application.streamingAssetsPath.TrimEnd('/') + "/WebGLContent/" + file.file;
                string path = BundlePath(file);
                OmMountWebBundle(url, path, file.bytes);
                float progress;
                while ((progress = OmWebBundleProgress()) >= 0 && progress < 1) {
                    OmNotifyWebTextureProgress(bundles.Count, files.Length, progress, 0);
                    yield return null;
                }
                AssetBundle bundle = null;
                if (progress >= 1) {
                    var request = AssetBundle.LoadFromFileAsync(path, file.crc);
                    yield return request;
                    bundle = request.assetBundle;
                }
                if (!bundle) {
                    foreach (var loaded in bundles.Values) loaded.Unload(true);
                    foreach (var item in files) File.Delete(BundlePath(item));
                    OmNotifyWebTextureProgress(bundles.Count, files.Length, 0, 4);
                    Debug.LogError($"[WebTexture] {profile} failed to open {file.file}");
                    yield break;
                }
                bundles.Add(file.name, bundle);
                OmNotifyWebTextureProgress(bundles.Count, files.Length, 1, 0);
            }
            ResourcesAPI.overrideAPI = new WebTextureResources(content.resources, bundles);
            OmNotifyWebTextureProgress(files.Length, files.Length, 1, 1);
            double started = Time.realtimeSinceStartupAsDouble;
            yield return PrepareTilePasses();
            Debug.Log($"[WebTexture] Tile rendering prepared in {(Time.realtimeSinceStartupAsDouble - started) * 1000:F0} ms");
            // TMP can initialize against the boot scene before the content is mounted.
            var settings = Resources.Load<TMP_Settings>("TMP Settings");
            typeof(TMP_Settings).GetField("s_Instance", BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, settings);
            OmNotifyWebTextureProgress(files.Length, files.Length, 1, 2);
            Debug.Log($"[WebTexture] {profile}, {files.Length} bundles, loading {content.mainScene}");
            int total = files.Length;
            var load = SceneManager.LoadSceneAsync(content.mainScene, LoadSceneMode.Single);
            if (load == null) {
                OmNotifyWebTextureProgress(total, total, 0, 4);
                yield break;
            }
            // Single unloads this bootstrap and stops its coroutines. The operation
            // owns the callback and can report readiness after the main scene activates.
            load.completed += _ => OmNotifyWebTextureProgress(total, total, 1, 3);
        }

        // WebGL specializes instanced programs for the size of the draw. Warm the two
        // common replay batch sizes on the loading screen, using the actual bundled
        // materials. No table, tile textures or persistent render targets are needed.
        static IEnumerator PrepareTilePasses() {
            if (!SystemInfo.supportsInstancing) yield break;
            var material = Resources.Load<Material>("Materials/Tiles/3DTile");
            var mesh = Resources.GetBuiltinResource<Mesh>("Cube.fbx");
            if (!material || !material.enableInstancing || !mesh) yield break;
            var target = RenderTexture.GetTemporary(4, 4, 16, RenderTextureFormat.ARGB32);
            try {
                var matrices = new Matrix4x4[32];
                for (int i = 0; i < matrices.Length; i++) matrices[i] = Matrix4x4.identity;
                using (var commands = new UnityEngine.Rendering.CommandBuffer { name = "Prepare table tile rendering" }) {
                    foreach (var passName in new[] { "ForwardLit", "TileOutlineHull" }) {
                        int pass = material.FindPass(passName);
                        if (pass < 0) continue;
                        foreach (int count in new[] { 2, 32 }) {
                            commands.Clear();
                            commands.SetRenderTarget(target);
                            commands.ClearRenderTarget(true, true, Color.clear);
                            commands.DrawMeshInstanced(mesh, 0, material, pass, matrices, count);
                            var previous = RenderTexture.active;
                            try { Graphics.ExecuteCommandBuffer(commands); }
                            finally { RenderTexture.active = previous; }
                            yield return null;
                        }
                    }
                }
            } finally {
                RenderTexture.ReleaseTemporary(target);
            }
        }

        static string BundlePath(WebTextureManifest.Bundle file) => "/om-web-" + file.name + ".bundle";
#endif
    }
}
#endif
