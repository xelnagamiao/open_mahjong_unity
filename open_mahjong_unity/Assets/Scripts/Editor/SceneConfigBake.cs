#if UNITY_EDITOR
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// 把场景设置里能画进场景的东西写进 MainScene：隐藏废按钮、Toggle 底图、色块 Sprite、帮助文案、牌面预览叠加层。
/// 只菜单调用，禁止 InitializeOnLoad。验证通过后再删本工具。
/// </summary>
public static class SceneConfigBake
{
    const string UndoName = "烘焙场景设置绘制";
    const string SolidSpritePath = "Assets/UI/SceneConfig/UiSolid.png";

    static readonly string[] UnusedCardBackButtons =
    {
        "PickImageButton",
        "DropZone",
        "ClearImageButton",
    };

    static readonly string[] ModeToggleNames =
    {
        "BackEdgeModeIndependent",
        "BackEdgeModeFollowBack",
        "BackEdgeModeFollowFront",
        "FrontEdgeModeIndependent",
        "FrontEdgeModeFollowTableBg",
        "FrontEdgeModeFollowBackEdge",
    };

    [MenuItem("Tools/Game/烘焙场景设置绘制")]
    public static void BakeFromMenu()
    {
        int n = BakeOpenScene(save: false);
        EditorUtility.DisplayDialog(
            "场景设置绘制",
            n > 0
                ? "已写入引用与绘制（隐藏废按钮、Toggle 底图、色块、帮助文案、牌面叠加层）。\n请立刻 Ctrl+S 保存场景，进 Play 验证。"
                : "当前打开的场景里找不到场景设置面板。请先打开 MainScene。",
            "好的");
    }

    public static int BakeOpenScene(bool save)
    {
        GameObject sceneRoot = FindInOpenScenes("SceneConfigPanel")
            ?? FindByComponent<SceneConfigPanel>();
        if (sceneRoot == null && FindByComponent<CardEdgePanel>() == null) return 0;

        CardBackEdgePanelBinder.BindOpenScene(organize: false);

        Sprite solid = LoadSolidSprite();
        HideUnusedButtons();
        BakeModeToggles(solid);
        BakeSwatchImages(solid);
        BakeHelpTexts();
        int overlays = BakeFaceOverlays();
        EnsureDragReceiver();

        Scene scene = SceneManager.GetActiveScene();
        if (scene.IsValid()) EditorSceneManager.MarkSceneDirty(scene);
        if (save && scene.IsValid()) EditorSceneManager.SaveScene(scene);
        Debug.Log("[SceneConfigBake] 完成。FaceOverlay=" + overlays);
        return 1;
    }

    static Sprite LoadSolidSprite()
    {
        Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(SolidSpritePath);
        if (sprite != null) return sprite;
        Texture2D tex = AssetDatabase.LoadAssetAtPath<Texture2D>(SolidSpritePath);
        if (tex == null) return null;
        Object[] all = AssetDatabase.LoadAllAssetsAtPath(SolidSpritePath);
        for (int i = 0; i < all.Length; i++)
        {
            if (all[i] is Sprite s) return s;
        }
        return null;
    }

    static void HideUnusedButtons()
    {
        for (int i = 0; i < UnusedCardBackButtons.Length; i++)
        {
            Transform t = FindInOpenScenes(UnusedCardBackButtons[i])?.transform;
            if (t == null || !t.gameObject.activeSelf) continue;
            Undo.RecordObject(t.gameObject, UndoName);
            t.gameObject.SetActive(false);
            EditorUtility.SetDirty(t.gameObject);
        }
    }

    static void BakeModeToggles(Sprite solid)
    {
        for (int i = 0; i < ModeToggleNames.Length; i++)
        {
            GameObject go = FindInOpenScenes(ModeToggleNames[i]);
            Toggle toggle = go != null ? go.GetComponent<Toggle>() : null;
            if (toggle == null) continue;
            Undo.RecordObject(toggle, UndoName);
            SerializedObject so = new SerializedObject(toggle);
            so.Update();
            SerializedProperty transition = so.FindProperty("m_Transition");
            if (transition != null) transition.enumValueIndex = 0;
            SerializedProperty toggleTransition = so.FindProperty("toggleTransition");
            if (toggleTransition != null) toggleTransition.enumValueIndex = 0;
            Graphic target = toggle.targetGraphic;
            SerializedProperty graphic = so.FindProperty("m_Graphic");
            if (graphic != null && graphic.objectReferenceValue == target)
            {
                graphic.objectReferenceValue = null;
            }
            so.ApplyModifiedProperties();
            Image bg = target as Image;
            if (bg == null) bg = toggle.GetComponent<Image>();
            AssignSolidImage(bg, solid, SceneConfigUi.UnselectedBlueGray);
            EditorUtility.SetDirty(toggle);
        }
    }

    static void BakeSwatchImages(Sprite solid)
    {
        string[] prefixes = { "Swatch", "SideSwatch", "BackEdgeSwatch", "FrontEdgeSwatch" };
        for (int p = 0; p < prefixes.Length; p++)
        {
            for (int i = 0; i < SceneConfigUi.PresetColors.Length; i++)
            {
                GameObject go = FindInOpenScenes(prefixes[p] + i);
                if (go == null) break;
                Image image = go.GetComponent<Image>();
                AssignSolidImage(image, solid, SceneConfigUi.PresetColors[i]);
            }
        }
    }

    static void AssignSolidImage(Image image, Sprite solid, Color color)
    {
        if (image == null) return;
        Undo.RecordObject(image, UndoName);
        if (solid != null && image.sprite == null)
        {
            image.sprite = solid;
            image.type = Image.Type.Simple;
        }
        image.color = color;
        EditorUtility.SetDirty(image);
    }

    static void BakeHelpTexts()
    {
        CardFaceConfigPanel face = Object.FindFirstObjectByType<CardFaceConfigPanel>(FindObjectsInactive.Include);
        if (face != null)
        {
            TMP_Text help = HelpTextOf(face, "helpText");
            WriteHelp(help, CardFaceConfigPanel.FormatHelp);
        }
        CardFaceBackgroundPanel bg = Object.FindFirstObjectByType<CardFaceBackgroundPanel>(FindObjectsInactive.Include);
        if (bg != null)
        {
            TMP_Text help = HelpTextOf(bg, "helpText");
            WriteHelp(help, CardFaceBackgroundPanel.FormatHelp);
        }
    }

    static TMP_Text HelpTextOf(Object panel, string field)
    {
        SerializedObject so = new SerializedObject(panel);
        SerializedProperty p = so.FindProperty(field);
        return p != null ? p.objectReferenceValue as TMP_Text : null;
    }

    static void WriteHelp(TMP_Text help, string text)
    {
        if (help == null || string.IsNullOrEmpty(text)) return;
        Undo.RecordObject(help, UndoName);
        help.text = text;
        EditorUtility.SetDirty(help);
    }

    static int BakeFaceOverlays()
    {
        CardFacePreviewSlot[] slots = Object.FindObjectsByType<CardFacePreviewSlot>(
            FindObjectsInactive.Include, FindObjectsSortMode.None);
        int count = 0;
        for (int i = 0; i < slots.Length; i++)
        {
            if (EnsureOverlay(slots[i])) count++;
        }
        return count;
    }

    static bool EnsureOverlay(CardFacePreviewSlot slot)
    {
        if (slot == null) return false;
        Transform existing = slot.transform.Find("FaceOverlay");
        Image overlay;
        if (existing != null)
        {
            overlay = existing.GetComponent<Image>();
        }
        else
        {
            var go = new GameObject("FaceOverlay", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            Undo.RegisterCreatedObjectUndo(go, UndoName);
            go.layer = slot.gameObject.layer;
            RectTransform rt = (RectTransform)go.transform;
            rt.SetParent(slot.transform, false);
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            overlay = go.GetComponent<Image>();
            overlay.raycastTarget = false;
            overlay.preserveAspect = true;
            overlay.enabled = false;
            go.SetActive(false);
        }
        if (overlay == null) return false;
        SerializedObject so = new SerializedObject(slot);
        so.Update();
        SerializedProperty p = so.FindProperty("overlay");
        if (p != null && p.objectReferenceValue != overlay)
        {
            p.objectReferenceValue = overlay;
            so.ApplyModifiedProperties();
            EditorUtility.SetDirty(slot);
            return true;
        }
        so.ApplyModifiedProperties();
        return existing == null;
    }

    static void EnsureDragReceiver()
    {
        if (Object.FindFirstObjectByType<CardBackEditorDragReceiver>(FindObjectsInactive.Include) != null) return;
        SceneConfigPanel panel = Object.FindFirstObjectByType<SceneConfigPanel>(FindObjectsInactive.Include);
        if (panel == null) return;
        Undo.AddComponent<CardBackEditorDragReceiver>(panel.gameObject);
    }

    static GameObject FindByComponent<T>() where T : Component
    {
        T c = Object.FindFirstObjectByType<T>(FindObjectsInactive.Include);
        return c != null ? c.gameObject : null;
    }

    static GameObject FindInOpenScenes(string name)
    {
        for (int i = 0; i < SceneManager.sceneCount; i++)
        {
            Scene scene = SceneManager.GetSceneAt(i);
            if (!scene.isLoaded) continue;
            GameObject[] roots = scene.GetRootGameObjects();
            for (int r = 0; r < roots.Length; r++)
            {
                Transform found = FindDeep(roots[r].transform, name);
                if (found != null) return found.gameObject;
            }
        }
        return null;
    }

    static Transform FindDeep(Transform root, string name)
    {
        if (root == null || string.IsNullOrEmpty(name)) return null;
        if (root.name == name) return root;
        for (int i = 0; i < root.childCount; i++)
        {
            Transform nested = FindDeep(root.GetChild(i), name);
            if (nested != null) return nested;
        }
        return null;
    }
}
#endif
