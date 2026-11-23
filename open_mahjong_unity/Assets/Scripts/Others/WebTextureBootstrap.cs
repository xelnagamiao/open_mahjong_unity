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
        string status;
        bool failed;
        [DllImport("__Internal")] static extern int OmUseWebTextureASTC(int linear);
        [DllImport("__Internal")] static extern void OmMountWebBundle(string url, string path, int size);
        [DllImport("__Internal")] static extern float OmWebBundleProgress();
        IEnumerator Start() {
            bool useAstc = OmUseWebTextureASTC(QualitySettings.activeColorSpace == ColorSpace.Linear ? 1 : 0) != 0;
            var files = useAstc ? content.astc : content.dxt;
            string profile = useAstc ? "ASTC" : "DXT";
            var bundles = new Dictionary<string, AssetBundle>();
            foreach (var file in files) {
                string url = Application.streamingAssetsPath.TrimEnd('/') + "/WebGLContent/" + file.file;
                string path = BundlePath(file);
                OmMountWebBundle(url, path, file.bytes);
                float progress;
                while ((progress = OmWebBundleProgress()) >= 0 && progress < 1) {
                    status = $"加载游戏资源 {bundles.Count + 1}/{files.Length}  {progress:P0}";
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
                    status = "游戏资源加载失败，点击重试";
                    failed = true;
                    Debug.LogError($"[WebTexture] {profile} failed to open {file.file}");
                    yield break;
                }
                bundles.Add(file.name, bundle);
            }
            ResourcesAPI.overrideAPI = new WebTextureResources(content.resources, bundles);
            status = "正在准备牌桌渲染";
            double started = Time.realtimeSinceStartupAsDouble;
            yield return PrepareTilePasses();
            Debug.Log($"[WebTexture] Tile rendering prepared in {(Time.realtimeSinceStartupAsDouble - started) * 1000:F0} ms");
            // TMP can initialize against the boot scene before the content is mounted.
            var settings = Resources.Load<TMP_Settings>("TMP Settings");
            typeof(TMP_Settings).GetField("s_Instance", BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, settings);
            status = "正在进入游戏";
            Debug.Log($"[WebTexture] {profile}, {files.Length} bundles, loading {content.mainScene}");
            yield return SceneManager.LoadSceneAsync(content.mainScene, LoadSceneMode.Single);
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

        void OnGUI() {
            var area = new Rect(0, Screen.height * .45f, Screen.width, 44);
            if (!failed) {
                GUI.Label(area, status, GUI.skin.GetStyle("box"));
            } else if (GUI.Button(area, status)) {
                failed = false;
                StartCoroutine(Start());
            }
        }
#endif
    }
}
#endif
