using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>显式检查磁盘主场景的隔离副本；不保存场景、不进入游戏或修改用户配置。</summary>
public static class CardDesignAggregationVerification
{
    static Scene scene;
    static SceneConfigPanel owner;
    static CardDesignTabs tabs;
    static CardFaceBackgroundPanel background;
    static Canvas canvas;
    static Camera camera;
    static RenderTexture sceneTarget;
    static int frames, stage;
    static readonly StringBuilder report = new StringBuilder();
    static string Out => Path.GetFullPath(Path.Combine(Application.dataPath, "../../other/SceneSettings/MainAggregation/preview-edge-fix/output"));

    // A one-shot local test request allows capture without taking over the user's mouse.
    [InitializeOnLoadMethod]
    static void ConsumeCaptureRequest()
    {
        string request = Path.GetFullPath(Path.Combine(Application.dataPath, "../Temp/CardAggregationCapture.request"));
        if (!File.Exists(request)) return;
        File.WriteAllText(request+".status", "Queued after reload; playing="+Application.isPlaying+" updating="+EditorApplication.isUpdating);
        EditorApplication.update -= StartRequestedCapture;
        EditorApplication.update += StartRequestedCapture;
        EditorApplication.QueuePlayerLoopUpdate();
    }

    static void StartRequestedCapture()
    {
        if (EditorApplication.isCompiling || EditorApplication.isUpdating || Application.isPlaying) return;
        EditorApplication.update -= StartRequestedCapture;
        string request = Path.GetFullPath(Path.Combine(Application.dataPath, "../Temp/CardAggregationCapture.request"));
        if (!File.Exists(request)) return;
        Run();
        File.Delete(request);
    }

    [MenuItem("Tools/Mahjong/Scene Settings/Verify Card Aggregation")]
    public static void Run()
    {
        if (Application.isPlaying) throw new InvalidOperationException("Run this check outside Play mode.");
        Directory.CreateDirectory(Out);
        string previousError=Path.Combine(Out,"ERROR.txt"); if(File.Exists(previousError))File.Delete(previousError);
        report.Clear();
        try {
            scene = EditorSceneManager.OpenPreviewScene("Assets/Scenes/MainScene.unity");
            owner = Find<SceneConfigPanel>(); tabs = Find<CardDesignTabs>();
            background = Find<CardFaceBackgroundPanel>();
            canvas = All<Canvas>().First(c => c.name == "MainCanvas");
            camera = All<Camera>().First(c => c.name == "Main Camera");
            foreach (var root in scene.GetRootGameObjects())
                root.SetActive(root == canvas.transform.root.gameObject || root == camera.transform.root.gameObject || root.GetComponent<Light>() != null);
            foreach (Transform child in canvas.transform) child.gameObject.SetActive(child.name == "PageRoot" || child.name == "HeaderPanel");
            foreach (Transform child in canvas.transform.Find("PageRoot")) child.gameObject.SetActive(child == owner.transform);
            for (Transform p = owner.transform; p != null; p = p.parent) p.gameObject.SetActive(true);
            Invoke(Find<CardFaceConfigPanel>(), "Awake");
            Invoke(background, "Awake");
            Invoke(Find<CardBackConfigPanel>(), "Awake");
            Invoke(Find<CardEdgePanel>(), "Awake");
            Invoke(tabs, "Awake");
            Invoke(Find<CardDesignDetails>(), "Awake");
            Invoke(owner, "BindNavigation");
            camera.overrideSceneCullingMask = EditorSceneManager.GetSceneCullingMask(scene);
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = Color.black;
            canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.worldCamera = camera; canvas.planeDistance = 1;
            sceneTarget = new RenderTexture(1280,720,24,RenderTextureFormat.ARGB32);
            sceneTarget.Create(); camera.targetTexture = sceneTarget; camera.aspect = 1280f/720;
            canvas.GetComponent<CanvasScaler>().enabled=false;canvas.scaleFactor=1280f/1920;
            frames = 0; stage = -1;
            EditorApplication.update -= Tick; EditorApplication.update += Tick;
        } catch (Exception e) { Fail(e); }
    }

    static T[] All<T>() where T : Component => scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<T>(true)).ToArray();
    static T Find<T>() where T : Component => All<T>().Single();
    static void Invoke(object obj, string method) => obj.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(obj, null);
    static void Check(bool ok, string message) { if (!ok) throw new Exception(message); report.AppendLine("PASS " + message); }
    static Transform FaceRoot => owner.transform.Find("FaceDesignPanel");
    static Transform CardRoot => owner.transform.Find("Card3DDesignPanel");
    static void Navigation(string name) => owner.transform.Find("NavigateBar/" + name).GetComponent<Button>().onClick.Invoke();
    static void Tab(Transform root, int index) => (root.Find("Header/Tab_" + index) ?? root.Find("Tab_" + index)).GetComponent<Button>().onClick.Invoke();
    internal static void ReleasePreviewSprites()
    {
        // The production component destroys runtime sprites. In this editor-only preview,
        // release these temporary objects before calling that unchanged runtime method.
        foreach (string name in new[] { "handBgSprite", "cardBackSprite", "tableBgSprite" }) {
            var field = typeof(CardFaceBackgroundPanel).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
            var sprite = field.GetValue(background) as Sprite;
            if (sprite) UnityEngine.Object.DestroyImmediate(sprite);
            field.SetValue(background, null);
        }
    }

    static void Tick()
    {
        if (++frames < 20) return; frames = 0;
        try {
            if (stage == -1) { Navigation("CardFaceButton"); stage = 0; return; }
            if (stage == 0) {
                Check(FaceRoot.gameObject.activeSelf && !CardRoot.gameObject.activeSelf, "sidebar opens only the face design group");
                var face = Find<CardFaceConfigPanel>();
                Check(FaceRoot.Find("Header").GetComponentsInChildren<Button>().Length == 2 && FaceRoot.Find("Header/Tab_2") == null, "face header retains two tabs and no 3D background settings tab");
                Check(CardRoot.Find("Tab_2") != null && CardRoot.Find("TableFacePage") != null, "original 3D background tab and settings page belong to 3D card design");
                Check(face.GetComponentsInChildren<TMP_Dropdown>(true).Any(d => d.name == "CustomPackDropdown"), "native custom-pack dropdown added beside original builtin packs");
                Check(face.transform.Find("StandardPacks/UploadButton") != null || face.GetComponentsInChildren<Button>(true).Any(b => b.transform.parent.name == "StandardPacks" && b.GetComponentInChildren<TMP_Text>()?.text == "+"), "original upload action moved to the plus button in the pack row");
                Check(face.gameObject.activeInHierarchy, "original face controller is visible");
                var grids = face.GetComponentsInChildren<GridLayoutGroup>(true).Where(g => g.name.EndsWith("PreviewContent")).ToArray();
                Check(grids.Length == 2 && grids.All(g => g.constraintCount == 9 && g.spacing == new Vector2(8,8)), "original 9-column grids and original 8px spacing retained");
                Check(grids[0].GetComponentsInChildren<CardFacePreviewSlot>(true).Length == 46 && grids[1].GetComponentsInChildren<CardFacePreviewSlot>(true).Length == 126, "all standard and Hongque preview slots retained");
                Check(grids.All(g => g.GetComponentsInChildren<TMP_Text>(true).Length == 0), "no tile numbers, suit labels or sample gallery decorations");
                Check(face.GetComponentsInChildren<CardFacePreviewSlot>().All(s => s.image.color == Color.white), "official hand faces use white untinted original images");
                Check(ConfigManager.DefaultTableFaceColor == ConfigManager.DefaultTableFaceFallbackColor && ConfigManager.DefaultTableFaceColor.r > .95f, "original gray-white face defaults retained without beige");
                Capture("01_main_face_design",1920,1080); Capture("01_main_face_design_1280",1280,720);
                Navigation("CardFaceButton");
                Check(face.gameObject.activeInHierarchy, "repeated sidebar clicks keep the original open-panel behavior");
                Tab(FaceRoot,1);
            } else if (stage == 1) {
                Check(FaceRoot.Find("HandImagesPage").gameObject.activeInHierarchy && !CardRoot.Find("TableFacePage").gameObject.activeInHierarchy, "hand image tab shows only original hand background/back controls");
                Capture("02_main_hand_images",1920,1080);
                ReleasePreviewSprites(); Tab(CardRoot,2);
            } else if (stage == 2) {
                Check(CardRoot.Find("TableFacePage").gameObject.activeInHierarchy, "3D face tab retains image upload, preview, RGB and original mode controls");
                var image=(Image)typeof(CardFaceBackgroundPanel).GetField("tableBgPreview",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(background);
                Check(image.transform.IsChildOf(CardRoot.Find("TableFacePage")), "original 3D face background image preview remains bound");
                Check(background.gameObject.activeInHierarchy && !FaceRoot.gameObject.activeSelf, "shared background controller stays active for original upload and drag callbacks");
                Check(!CardRoot.Find("Actual3DPreview").gameObject.activeSelf, "background page shows its original model preview without the back/edge duplicate");
                Capture("03_main_3d_face",1920,1080);
                ReleasePreviewSprites(); owner.RefreshPage();
                Check(CardRoot.Find("TableFacePage").gameObject.activeInHierarchy, "refresh preserves the selected inner tab");
                Navigation("CardBackButton");
            } else if (stage == 3) {
                Check(CardRoot.gameObject.activeSelf && !FaceRoot.gameObject.activeSelf && Find<CardBackConfigPanel>().gameObject.activeInHierarchy, "3D card sidebar opens original back controls and hides face group");
                Capture("04_main_3d_back",1920,1080);
                Tab(CardRoot,1);
            } else if (stage == 4) {
                var edge = Find<CardEdgePanel>();
                Check(edge.gameObject.activeInHierarchy && !Find<CardBackConfigPanel>().gameObject.activeInHierarchy, "edge tab switches between original panels");
                Check(edge.GetComponentsInChildren<Toggle>(true).Length == 6, "all six original mode toggles retained");
                var front = edge.transform.Find("FrontEdgeDesign") as RectTransform;
                var rear = edge.transform.Find("BackEdgeDesign") as RectTransform;
                Check(front.anchoredPosition.y == rear.anchoredPosition.y && front.anchoredPosition.x < rear.anchoredPosition.x,
                    "reviewed front/back edge columns restored side by side");
                var handPage = FaceRoot.Find("HandImagesPage");
                Check(handPage.Find("HandBackgroundDesign") != null && handPage.Find("HandBackDesign") != null,
                    "reviewed two complete hand image columns restored");
                Check(handPage.Find("HandCompositePreview").GetComponentsInChildren<CardFacePreviewSlot>(true).Length == 5,
                    "hand image composition preview restored with original slots");
                Check(All<CardDesignModelPreview>().Length == 2, "reviewed model preview regions restored");
                Capture("05_main_edges",1920,1080); Capture("05_main_edges_1280",1280,720);
                Navigation("HideButton");
                Check(!FaceRoot.gameObject.activeSelf && !CardRoot.gameObject.activeSelf, "hide panel closes both complete groups");
                Check(!background.gameObject.activeInHierarchy, "hide panel also disables the shared background upload controller");
                Navigation("CardFaceButton"); Navigation("CharacterButton");
                Check(!FaceRoot.gameObject.activeSelf && !CardRoot.gameObject.activeSelf, "other sidebar entries close the aggregated groups");
                Navigation("CardBackButton"); Tab(CardRoot,0); Tab(CardRoot,0);
                Check(Find<CardBackConfigPanel>().gameObject.activeInHierarchy, "repeated tab clicks do not toggle the selected page off");
                Check(All<MonoBehaviour>().All(c => c == null || !c.GetType().Name.StartsWith("LabSurface") && c.GetType().Name != "SettingsLabController"), "no prototype controller or replacement skin imported into main scene");
                report.Append(CardModelAndEdgeChecks.Run(edge, Find<CardBackConfigPanel>(), background, tabs, Capture, Out));
                Navigation("CardFaceButton");
                report.Append(CardFaceRevisionChecks.Run(Find<CardFaceConfigPanel>(), Capture));
                File.WriteAllText(Path.Combine(Out,"checks.txt"),report.ToString());
                Cleanup(); Debug.Log("CARD_AGGREGATION_VERIFIED " + Out); return;
            }
            stage++;
        } catch (Exception e) { Fail(e); }
    }

    static void Capture(string name,int width,int height)
    {
        width=1280;height=720;
        // Reset the offscreen Canvas batches as in the standalone scene renderer.
        // Temporarily close/reopen the native popup using Editor-safe disposal.
        var expanded = canvas.GetComponentsInChildren<TMP_Dropdown>().Where(d => d.IsExpanded).ToArray();
        foreach (var dropdown in expanded) CardFaceRevisionChecks.CloseEditorDropdown(dropdown);
        canvas.gameObject.SetActive(false); canvas.gameObject.SetActive(true);
        foreach (var dropdown in expanded) dropdown.Show();
        Find<CardDesignDetails>().RefreshPreviews();
        foreach (var preview in All<CardDesignModelPreview>()) preview.RenderPreview();
        var rt=sceneTarget;
        Canvas.ForceUpdateCanvases();
        foreach (var t in canvas.GetComponentsInChildren<TMP_Text>()) t.ForceMeshUpdate();
        Canvas.ForceUpdateCanvases();
        RenderPipeline.SubmitRenderRequest(camera,new UniversalRenderPipeline.SingleCameraRequest { destination=rt });
        // The first render updates the camera-sized Canvas batch. Rebuild once at
        // that final size so the exported image does not contain stale UI batches.
        foreach (var graphic in canvas.GetComponentsInChildren<Graphic>()) graphic.SetAllDirty();
        Canvas.ForceUpdateCanvases();
        RenderPipeline.SubmitRenderRequest(camera,new UniversalRenderPipeline.SingleCameraRequest { destination=rt });
        var old = RenderTexture.active; RenderTexture.active = rt;
        var png = new Texture2D(width,height,TextureFormat.RGBA32,false); png.ReadPixels(new Rect(0,0,width,height),0,0); png.Apply();
        File.WriteAllBytes(Path.Combine(Out,name+".png"),png.EncodeToPNG());
        RenderTexture.active = old;
        UnityEngine.Object.DestroyImmediate(png);
    }
    static void Fail(Exception error) { File.WriteAllText(Path.Combine(Out,"ERROR.txt"),error.ToString()); Cleanup(); Debug.LogException(error); }
    static void Cleanup() {
        EditorApplication.update -= Tick;
        if (scene.IsValid()) { ReleasePreviewSprites(); EditorSceneManager.ClosePreviewScene(scene); }
        if (sceneTarget != null) { sceneTarget.Release(); UnityEngine.Object.DestroyImmediate(sceneTarget); sceneTarget = null; }
    }
}
