#if UNITY_EDITOR
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// 在 CardEdgePanel 上烘焙：正面边缘颜色模式 3 toggle + 正面贴图延伸 toggle。
/// 运行时不再创建任何 UI，因此面板里的元素必须存在于场景里。
/// 菜单：Tools/牌边设置/烘焙正面边缘 toggle 到场景
/// </summary>
public static class CardEdgePanelBaker
{
    private static readonly Color ButtonBg = new Color(0.17f, 0.21f, 0.30f, 1f);
    private static readonly Color Accent = new Color(0.28f, 0.48f, 0.92f, 1f);
    private static readonly Color LabelColor = new Color(0.82f, 0.85f, 0.90f, 1f);
    private static readonly Color SelectedColor = new Color(1f, 0.55f, 0f, 1f);
    private static readonly Color UnselectedColor = new Color(0.2f, 0.24f, 0.32f, 1f);

    private static Sprite _whiteSprite;
    private static TMP_FontAsset _tmpFont;

    [MenuItem("Tools/牌边设置/烘焙正面边缘 toggle 到场景")]
    public static void Bake()
    {
        bool ok = BakeCurrentScene();
        if (ok)
        {
            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
            AssetDatabase.SaveAssets();
        }
        if (!Application.isBatchMode)
        {
            EditorUtility.DisplayDialog(
                "牌边面板烘焙",
                ok ? "已补建正面边缘模式 toggle 与跟随 3D 牌面背景 toggle，请按 Ctrl+S 保存场景。" : "找不到 CardEdgePanel，无法烘焙。",
                "好的");
        }
        else if (!ok)
        {
            throw new System.Exception("CardEdgePanel bake failed: CardEdgePanel not found.");
        }
    }

    private static bool BakeCurrentScene()
    {
        CardEdgePanel panel = Object.FindFirstObjectByType<CardEdgePanel>();
        if (panel == null) return false;
        Transform root = panel.transform;

        RectTransform parent = root.Find("BackEdgeModeIndependent") as RectTransform;
        if (parent == null) return false;

        // 删除旧的前面边缘 toggle（幂等重建）
        DestroyIfExists(root, "FrontEdgeModeIndependent");
        DestroyIfExists(root, "FrontEdgeModeFollowTableBg");
        DestroyIfExists(root, "FrontEdgeModeFollowBackEdge");
        DestroyIfExists(root, "FrontTexFollowTableBg");
        DestroyIfExists(root, "FrontEdgeSwatches");

        Toggle template = panel.GetComponentInChildren<Toggle>(true);
        if (template == null) return false;

        Transform frontRow = NewRect("FrontEdgeRow", root);
        RectTransform templateRt = (RectTransform)template.transform;
        RectTransform rowRt = (RectTransform)frontRow;
        rowRt.anchorMin = templateRt.anchorMin;
        rowRt.anchorMax = templateRt.anchorMax;
        rowRt.pivot = templateRt.pivot;
        rowRt.anchoredPosition = templateRt.anchoredPosition + new Vector2(0f, -templateRt.sizeDelta.y - 12f);
        rowRt.sizeDelta = new Vector2(templateRt.sizeDelta.x, templateRt.sizeDelta.y);

        Toggle independent = CreateToggle(frontRow, "FrontEdgeModeIndependent", "正面独立", template);
        PlaceLeft((RectTransform)independent.transform, 0f, 0f, 220f, templateRt.sizeDelta.y);
        Toggle followTable = CreateToggle(frontRow, "FrontEdgeModeFollowTableBg", "正面跟随3D牌面背景", template);
        PlaceLeft((RectTransform)followTable.transform, 232f, 0f, 280f, templateRt.sizeDelta.y);
        Toggle followBack = CreateToggle(frontRow, "FrontEdgeModeFollowBackEdge", "正面跟随背面边缘", template);
        PlaceLeft((RectTransform)followBack.transform, 524f, 0f, 280f, templateRt.sizeDelta.y);

        Toggle extendFront = CreateToggle(frontRow, "FrontTexFollowTableBg", "独立设置：跟随 3D 牌面背景", template);
        PlaceLeft((RectTransform)extendFront.transform, 0f, -templateRt.sizeDelta.y - 12f, 600f, templateRt.sizeDelta.y);
        extendFront.group = null;

        SerializedObject so = new SerializedObject(panel);
        so.FindProperty("frontEdgeModeIndependent").objectReferenceValue = independent;
        so.FindProperty("frontEdgeModeFollowTableBg").objectReferenceValue = followTable;
        so.FindProperty("frontEdgeModeFollowBackEdge").objectReferenceValue = followBack;
        so.FindProperty("frontTexFollowTableBgToggle").objectReferenceValue = extendFront;
        so.ApplyModifiedPropertiesWithoutUndo();

        // 拖拽恢复（之前丢失）
        if (root.GetComponent<UIDragger>() == null)
        {
            root.gameObject.AddComponent<UIDragger>();
        }
        return true;
    }

    private static Toggle CreateToggle(Transform parent, string name, string label, Toggle template)
    {
        GameObject clone = Object.Instantiate(template.gameObject, parent, false);
        clone.name = name;
        TMP_Text tmp = clone.GetComponentInChildren<TMP_Text>(true);
        if (tmp != null)
        {
            tmp.text = label;
            tmp.enableAutoSizing = false;
            tmp.fontSize = 16;
        }
        Toggle toggle = clone.GetComponent<Toggle>();
        if (toggle != null)
        {
            toggle.onValueChanged = new Toggle.ToggleEvent();
        }
        Image bg = clone.GetComponent<Image>();
        if (bg != null)
        {
            bg.color = ButtonBg;
        }
        return toggle;
    }

    private static void DestroyIfExists(Transform root, string name)
    {
        Transform t = root.Find(name);
        if (t != null) Object.DestroyImmediate(t.gameObject);
    }

    private static RectTransform NewRect(string name, Transform parent)
    {
        GameObject go = new GameObject(name, typeof(RectTransform));
        RectTransform rt = (RectTransform)go.transform;
        rt.SetParent(parent, false);
        return rt;
    }

    private static void PlaceLeft(RectTransform rt, float x, float y, float w, float h)
    {
        rt.anchorMin = new Vector2(0f, 1f);
        rt.anchorMax = new Vector2(0f, 1f);
        rt.pivot = new Vector2(0f, 1f);
        rt.anchoredPosition = new Vector2(x, -y);
        rt.sizeDelta = new Vector2(w, h);
    }

    private static Sprite WhiteSprite()
    {
        if (_whiteSprite == null)
        {
            _whiteSprite = Sprite.Create(
                Texture2D.whiteTexture,
                new Rect(0f, 0f, 1f, 1f),
                new Vector2(0.5f, 0.5f));
        }
        return _whiteSprite;
    }

    private static TMP_FontAsset TmpFont()
    {
        if (_tmpFont != null) return _tmpFont;
        _tmpFont = Resources.Load<TMP_FontAsset>("font/Chinese/AlibabaPuHuiTi/AlibabaPuHuiTi-3-55-Regular SDF");
        if (_tmpFont == null) _tmpFont = TMP_Settings.defaultFontAsset;
        return _tmpFont;
    }
}
#endif
