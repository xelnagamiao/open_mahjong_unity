using System;
using System.Collections;
using System.Collections.Generic;
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

/// <summary>Explicit, disposable main-scene UI capture. Never clicks a settings writer or saves the scene.</summary>
[InitializeOnLoad]
public static class TableSeamUiVerification
{
    [Serializable] private class Entry { public string name; public string preview; }
    [Serializable] private class Catalog { public Entry[] cloth; }
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static readonly StringBuilder report = new StringBuilder();
    private static readonly List<Sprite> rowSprites = new List<Sprite>();
    private static readonly List<GameObject> rowObjects = new List<GameObject>();
    private static readonly string[] PrefKeys = { "SelectedTableClothPath", "SelectedTableClothIsCustom", "SelectedTableSeam", "SelectedTableEdgePath", "SelectedTableEdgeIsCustom" };
    private static string prefsBefore;
    private static Scene scene;
    private static TableClothPanel panel;
    private static TableSeamSelector selector;
    private static Canvas canvas;
    private static Camera camera;
    private static RenderTexture target;
    private static ConfigManager previousConfig;
    private static IEnumerator previews;
    private static ResourceRequest pending;
    private static int settle;
    private static double deadline;
    private static Vector2 oldMin, oldMax;
    private static readonly FieldInfo ConfigInstance = typeof(ConfigManager).GetField("<Instance>k__BackingField", BindingFlags.Static | BindingFlags.NonPublic);
    private static string Out => Path.GetFullPath(Path.Combine(Application.dataPath, "../../other/Tablecloth/SeamLibrary/validation/ui"));
    private static string Request => Path.GetFullPath(Path.Combine(Application.dataPath, "../Temp/TableSeamUiVerification.request"));

    static TableSeamUiVerification() => EditorApplication.update += WatchRequest;

    private static void WatchRequest()
    {
        if (scene.IsValid() || EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
        if (!File.Exists(Request) || File.ReadAllText(Request).Trim() != "VERIFY") return;
        File.Delete(Request);
        Run();
    }

    [MenuItem("Tools/Mahjong/Scene Settings/Verify Separate Table Seams UI")]
    public static void Run()
    {
        if (Application.isPlaying || scene.IsValid()) return;
        Directory.CreateDirectory(Out);
        report.Clear();
        File.WriteAllText(Path.Combine(Out, "status.txt"), "RUNNING");
        prefsBefore = PrefSnapshot();
        previousConfig = ConfigManager.Instance;
        try
        {
            scene = EditorSceneManager.OpenPreviewScene("Assets/Scenes/MainScene.unity");
            panel = All<TableClothPanel>().Single();
            var settings = All<SceneConfigPanel>().Single();
            canvas = All<Canvas>().First(c => c.name == "MainCanvas");
            camera = All<Camera>().First(c => c.name == "Main Camera");
            foreach (var root in scene.GetRootGameObjects())
                root.SetActive(root == canvas.transform.root.gameObject || root == camera.transform.root.gameObject || root.GetComponent<Light>() != null);
            foreach (Transform child in canvas.transform)
                child.gameObject.SetActive(child.name == "PageRoot" || child.name == "HeaderPanel");
            foreach (Transform child in canvas.transform.Find("PageRoot")) child.gameObject.SetActive(child == settings.transform);
            foreach (Transform child in settings.transform)
                child.gameObject.SetActive(child == panel.transform || child.name == "NavigateBar");
            for (Transform parent = panel.transform; parent != null; parent = parent.parent) parent.gameObject.SetActive(true);
            ConfigInstance.SetValue(null, All<ConfigManager>().First());
            var scroll = panel.contentParent.GetComponentInParent<ScrollRect>(true).GetComponent<RectTransform>();
            oldMin = scroll.offsetMin; oldMax = scroll.offsetMax;
            selector = panel.gameObject.AddComponent<TableSeamSelector>();
            selector.Initialize(panel);
            Check(Mathf.Abs(scroll.offsetMax.y - oldMax.y + 148) < .01f, "selector reserves 148 units above the existing scroll view");
            Check(scroll.offsetMin == oldMin, "original footer and scroll bottom remain fixed");
            BuildRealRows();
            camera.overrideSceneCullingMask = EditorSceneManager.GetSceneCullingMask(scene);
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Color.black;
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = camera;
            canvas.planeDistance = 1;
            canvas.GetComponent<CanvasScaler>().enabled = false;
            canvas.scaleFactor = 1;
            target = new RenderTexture(1920, 1080, 24, RenderTextureFormat.ARGB32);
            target.Create(); camera.targetTexture = target; camera.aspect = 1920f / 1080;
            previews = (IEnumerator)typeof(TableSeamSelector).GetMethod("LoadPreviews", Private).Invoke(selector, null);
            pending = null; settle = 0;
            deadline = EditorApplication.timeSinceStartup + 90;
            EditorApplication.update += Tick;
        }
        catch (Exception error) { Finish(error); }
    }

    private static T[] All<T>() where T : Component => scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<T>(true)).ToArray();
    private static void Check(bool passed, string description)
    {
        if (!passed) throw new InvalidOperationException(description);
        report.AppendLine("PASS " + description);
    }

    private static void BuildRealRows()
    {
        foreach (Transform child in panel.contentParent) child.gameObject.SetActive(false);
        var manifest = Resources.Load<TextAsset>("TableSurfacePreviews/catalog");
        var entries = JsonUtility.FromJson<Catalog>(manifest.text).cloth;
        Check(entries.Length == 13, "catalog contains exactly 13 independent cloth backgrounds");
        var sourceType = typeof(TableSurfacePanel).GetNestedType("Source", BindingFlags.NonPublic);
        var createRow = typeof(TableSurfacePanel).GetMethod("CreateRow", Private);
        var original = panel.tableclothPrefab.GetComponent<TableCloth>().tableClothChoseImage;
        for (int i = 0; i < entries.Length; i++)
        {
            var source = Activator.CreateInstance(sourceType);
            sourceType.GetField("Path").SetValue(source, entries[i].name);
            sourceType.GetField("Preview").SetValue(source, entries[i].preview);
            sourceType.GetField("Revision").SetValue(source, entries[i].preview);
            var texture = Resources.Load<Texture2D>(entries[i].preview);
            Check(texture != null && texture.width <= 256 && texture.height <= 256, entries[i].name + " uses only a small preview");
            var row = createRow.Invoke(panel, new object[] { source, texture });
            var item = (GameObject)row.GetType().GetField("Item").GetValue(row);
            rowObjects.Add(item);
            rowSprites.Add((Sprite)row.GetType().GetField("Sprite").GetValue(row));
            var cloth = item.GetComponent<TableCloth>();
            cloth.RefreshSelection();
            Check(cloth.tableClothChoseImage.color == original.color && cloth.tableClothChoseImage.sprite == original.sprite &&
                cloth.tableClothChoseImage.transform.GetSiblingIndex() == original.transform.GetSiblingIndex(), entries[i].name + " retains the original orange selection frame");
            Check(item.transform.Find("TableSurfaceCaption") == null, entries[i].name + " has no combined-style caption");
        }
        // Show the original frame even if the user's saved custom cloth is outside this built-in-only capture.
        if (!rowObjects.Any(o => o.GetComponent<TableCloth>().tableClothChoseImage.gameObject.activeSelf))
        {
            rowObjects[0].GetComponent<TableCloth>().tableClothChoseImage.gameObject.SetActive(true);
            report.AppendLine("INFO orange cloth frame shown on first preview in disposable scene only; saved custom selection unchanged");
        }
    }

    private static void Tick()
    {
        try
        {
            if (EditorApplication.timeSinceStartup > deadline) throw new TimeoutException("UI preview loading timed out");
            if (pending != null && !pending.isDone) return;
            pending = null;
            if (previews != null)
            {
                if (previews.MoveNext()) { pending = previews.Current as ResourceRequest; return; }
                previews = null;
            }
            if (++settle < 12) { EditorApplication.QueuePlayerLoopUpdate(); return; }
            CheckUi();
            Capture("tablecloth-seams-1920.png", 1920, 1080);
            Capture("tablecloth-seams-1280.png", 1280, 720);
            var scroll = panel.contentParent.GetComponentInParent<ScrollRect>(true).GetComponent<RectTransform>();
            typeof(TableSeamSelector).GetMethod("OnDisable", Private).Invoke(selector, null);
            Check(scroll.offsetMin == oldMin && scroll.offsetMax == oldMax, "disabling selector restores original scroll geometry");
            selector.Initialize(panel);
            Check(Mathf.Abs(scroll.offsetMax.y - oldMax.y + 148) < .01f, "reopening does not accumulate vertical offsets");
            Check(PrefSnapshot() == prefsBefore, "all table selection preferences unchanged");
            Finish(null);
        }
        catch (Exception error) { Finish(error); }
    }

    private static void CheckUi()
    {
        Canvas.ForceUpdateCanvases();
        var choices = panel.transform.Find("TableSeamSelector/Choices");
        Check(choices != null && choices.childCount == 8, "eight independent seam buttons exist");
        var original = panel.tableclothPrefab.GetComponent<TableCloth>().tableClothChoseImage;
        int selected = ConfigManager.Instance.GetSelectedTableSeam();
        for (int i = 0; i < 8; i++)
        {
            var item = choices.GetChild(i);
            var label = item.GetComponentInChildren<TMP_Text>(true);
            label.ForceMeshUpdate(true);
            Check(label.text == TableSurfaceNames.SeamDisplayName(i - 1), "button " + (i - 1) + " has the expected Chinese label");
            Check(!label.isTextOverflowing && label.textInfo.lineCount == 1, label.text + " fits on one line");
            foreach (char c in label.text.Where(c => !char.IsWhiteSpace(c)))
                Check(label.font.HasCharacter(c, true, true), "font includes U+" + ((int)c).ToString("X4"));
            var frame = item.Find("TableClothChoseImage").GetComponent<Image>();
            Check(frame.color == original.color && frame.sprite == original.sprite && frame.transform.GetSiblingIndex() == original.transform.GetSiblingIndex(), "seam " + (i - 1) + " clones the original orange selection frame");
            Check(frame.gameObject.activeSelf == (i - 1 == selected), "seam " + (i - 1) + " selection follows the saved independent index");
            Check(item.GetComponent<TableCloth>() == null, "seam " + (i - 1) + " has no cloth-selection component");
            if (i > 0) Check(item.Find("TableClothImage/Preview").GetComponent<Image>().sprite != null, "seam " + (i - 1) + " asynchronous preview completed");
        }
        report.AppendLine("INFO runtime production UI built and preview coroutine driven by Editor updates; settings buttons were not clicked");
    }

    private static void Capture(string name, int width, int height)
    {
        camera.targetTexture = null;
        target.Release(); UnityEngine.Object.DestroyImmediate(target);
        target = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32);
        target.Create(); camera.targetTexture = target;
        canvas.scaleFactor = width / 1920f;
        canvas.gameObject.SetActive(false); canvas.gameObject.SetActive(true);
        Canvas.ForceUpdateCanvases();
        foreach (var text in canvas.GetComponentsInChildren<TMP_Text>()) text.ForceMeshUpdate();
        RenderPipeline.SubmitRenderRequest(camera, new UniversalRenderPipeline.SingleCameraRequest { destination = target });
        foreach (var graphic in canvas.GetComponentsInChildren<Graphic>()) graphic.SetAllDirty();
        Canvas.ForceUpdateCanvases();
        RenderPipeline.SubmitRenderRequest(camera, new UniversalRenderPipeline.SingleCameraRequest { destination = target });
        var previous = RenderTexture.active;
        try
        {
            RenderTexture.active = target;
            var image = new Texture2D(width, height, TextureFormat.RGBA32, false);
            image.ReadPixels(new Rect(0, 0, width, height), 0, 0); image.Apply();
            File.WriteAllBytes(Path.Combine(Out, name), image.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(image);
        }
        finally { RenderTexture.active = previous; }
    }

    private static string PrefSnapshot() => string.Join("\n", PrefKeys.Select(k => k + ":" + PlayerPrefs.HasKey(k) + ":" +
        (k.EndsWith("Path", StringComparison.Ordinal) ? PlayerPrefs.GetString(k, "") : PlayerPrefs.GetInt(k, int.MinValue).ToString())));

    private static void Finish(Exception error)
    {
        EditorApplication.update -= Tick;
        (previews as IDisposable)?.Dispose(); previews = null; pending = null;
        foreach (var item in rowObjects) if (item != null) UnityEngine.Object.DestroyImmediate(item);
        foreach (var sprite in rowSprites) if (sprite != null) UnityEngine.Object.DestroyImmediate(sprite);
        rowObjects.Clear(); rowSprites.Clear();
        if (selector != null) UnityEngine.Object.DestroyImmediate(selector);
        if (scene.IsValid()) EditorSceneManager.ClosePreviewScene(scene);
        scene = default;
        ConfigInstance.SetValue(null, previousConfig);
        if (target != null) { target.Release(); UnityEngine.Object.DestroyImmediate(target); target = null; }
        if (error != null) report.AppendLine("FAIL " + error);
        File.WriteAllText(Path.Combine(Out, "checks.txt"), report.ToString());
        File.WriteAllText(Path.Combine(Out, "status.txt"), error == null ? "PASS" : "FAIL");
        if (error == null) Debug.Log("TABLE_SEAM_UI_VERIFIED " + Out); else Debug.LogException(error);
    }
}
