using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using TMPro;

// Explicit disk-preview review. Never enters Play mode, saves a scene or submits auth.
[InitializeOnLoad]
public static class AuthUiReview {
    static readonly string Output = Path.GetFullPath(Path.Combine(Application.dataPath, "../../.om_workspace/20260915-auth-ui"));
    static AuthUiReview() { EditorApplication.update += Poll; }
    static void Poll() {
        string request = Path.Combine(Output, "review.request");
        if (EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode || !File.Exists(request)) return;
        string prefix = File.ReadAllText(request).Trim(); File.Delete(request);
        try { Run(prefix); } catch (Exception e) { File.WriteAllText(Path.Combine(Output, "review-error.txt"), e.ToString()); }
    }
    [MenuItem("Tools/Mahjong/Auth UI/Review Login and Register")]
    public static void Review() { Run("review"); }
    static void Run(string prefix) {
        Directory.CreateDirectory(Output);
        Scene scene = EditorSceneManager.OpenPreviewScene("Assets/Scenes/MainScene.unity");
        RenderTexture target = null;
        try {
            var all = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Transform>(true)).ToArray();
            var owner = all.Select(t => t.GetComponent<LoginPanel>()).First(x => x != null);
            var canvas = owner.GetComponentInParent<Canvas>();
            var camera = all.Select(t => t.GetComponent<Camera>()).First(x => x != null && x.name == "Main Camera");
            foreach (var root in scene.GetRootGameObjects()) root.SetActive(root == canvas.transform.root.gameObject || root == camera.transform.root.gameObject);
            for (Transform p = owner.transform; p.parent != null; p = p.parent) {
                p.gameObject.SetActive(true);
                foreach (Transform sibling in p.parent) if (sibling != p) sibling.gameObject.SetActive(false);
                if (p.parent == canvas.transform) break;
            }
            canvas.gameObject.SetActive(true);
            camera.gameObject.SetActive(true);
            camera.overrideSceneCullingMask = EditorSceneManager.GetSceneCullingMask(scene);
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = Color.black;
            canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.worldCamera = camera; canvas.planeDistance = 1;
            canvas.GetComponent<CanvasScaler>().enabled = false;
            typeof(LoginPanel).GetMethod("InitializeTabs",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(owner,null);
            AssertForm(owner,false);
            var inputs = owner.GetComponentsInChildren<TMP_InputField>(true);
            var originalText = inputs.Select(x => x.text).ToArray();
            foreach (var input in inputs) input.SetTextWithoutNotify("tab-review-value");
            var labels = new[]{(Button)Field(owner,"loginTabButton"),(Button)Field(owner,"registerTabButton")}
                .Select(x => x.GetComponentInChildren<TMP_Text>(true)).ToArray();
            var colors = labels.Select(x => x.color).ToArray();
            // Check actual button listeners, including repeated clicks and navigation while offline.
            typeof(LoginPanel).GetMethod("SetAuthButtons",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(owner,new object[]{false});
            foreach (bool show in new[]{false,true,true,false,false,true,false}) {
                Switch(owner,show);
                AssertForm(owner,show);
                if (inputs.Any(x => x.text != "tab-review-value")) throw new Exception("Switching tabs changed form input");
                if (labels.Where((x,i) => x.color != colors[i]).Any()) throw new Exception("Switching tabs changed authored text colors");
            }
            for (int i=0;i<inputs.Length;i++) inputs[i].SetTextWithoutNotify(originalText[i]);
            typeof(LoginPanel).GetMethod("SetAuthButtons",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(owner,new object[]{true});
            foreach (int width in new[] {1920,1280}) {
                int height = width * 9 / 16;
                target = new RenderTexture(width,height,24,RenderTextureFormat.ARGB32); target.Create();
                camera.targetTexture = target; camera.aspect = (float)width/height; canvas.scaleFactor = width/1920f;
                Switch(owner,false); Capture(canvas,camera,target,Path.Combine(Output,prefix+"-login-"+width+".png"));
                Switch(owner,true); Capture(canvas,camera,target,Path.Combine(Output,prefix+"-register-"+width+".png"));
                AssertForm(owner,true);
                camera.targetTexture = null; target.Release(); UnityEngine.Object.DestroyImmediate(target); target = null;
            }
            File.WriteAllText(Path.Combine(Output,prefix+"-review.txt"),"Rendered the authored forms at 1920x1080 and 1280x720. Button listeners, initial login, repeated switching, offline navigation, form visibility, input retention and authored colors passed. No scene saved or auth submitted.\n");
        } finally {
            if (target != null) { target.Release(); UnityEngine.Object.DestroyImmediate(target); }
            EditorSceneManager.ClosePreviewScene(scene);
        }
    }
    static object Field(object owner,string name) => owner.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic).GetValue(owner);
    static void Switch(LoginPanel owner,bool show) {
        ((Button)Field(owner,show ? "registerTabButton" : "loginTabButton")).onClick.Invoke();
    }
    static void AssertForm(LoginPanel owner,bool showRegistration) {
        var container = (GameObject)Field(owner,"formContainer");
        var login = (GameObject)Field(owner,"loginContent");
        var register = (GameObject)Field(owner,"registerForm");
        if (!container.activeInHierarchy || login.activeInHierarchy == showRegistration || register.activeInHierarchy != showRegistration)
            throw new Exception("Auth form visibility is incorrect");
        foreach (string field in new[]{"loginTabButton","registerTabButton"}) {
            var button = (Button)Field(owner,field);
            if (!button.gameObject.activeInHierarchy || !button.interactable) throw new Exception("Auth tab is hidden or disabled: " + field);
        }
        if (((GameObject)Field(owner,"loginTabUnderline")).activeSelf == showRegistration ||
            ((GameObject)Field(owner,"registerTabUnderline")).activeSelf != showRegistration)
            throw new Exception("Selected tab underline is incorrect");
    }
    static void Capture(Canvas canvas,Camera camera,RenderTexture target,string path) {
        canvas.gameObject.SetActive(false); canvas.gameObject.SetActive(true);
        for (int i=0;i<2;i++) {
            Canvas.ForceUpdateCanvases();
            foreach (var text in canvas.GetComponentsInChildren<TMP_Text>()) text.ForceMeshUpdate(true, true);
            foreach (var graphic in canvas.GetComponentsInChildren<Graphic>()) graphic.SetAllDirty();
            Canvas.ForceUpdateCanvases();
            RenderPipeline.SubmitRenderRequest(camera,new UniversalRenderPipeline.SingleCameraRequest { destination=target });
        }
        var previous = RenderTexture.active; RenderTexture.active = target;
        var texture = new Texture2D(target.width,target.height,TextureFormat.RGBA32,false);
        texture.ReadPixels(new Rect(0,0,target.width,target.height),0,0);texture.Apply();
        File.WriteAllBytes(path,texture.EncodeToPNG());
        RenderTexture.active = previous; UnityEngine.Object.DestroyImmediate(texture);
    }
}
