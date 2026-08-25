#if UNITY_EDITOR
using System.Collections.Generic;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// 仅菜单或一次性 runner 调用：挂载 CardBackPanel / EdgeSection 子控件，
/// 并把散落色块收进布局组。保持世界坐标；不把模式 Toggle 放进会挤扁的 Layout。
/// 禁止 InitializeOnLoad / sceneOpened 自动跑。
/// </summary>
public static class CardBackEdgePanelBinder
{
    const string UndoName = "挂载牌背与牌边面板";

    [MenuItem("Tools/Game/挂载牌背与牌边面板")]
    public static void BindFromMenu()
    {
        int n = BindOpenScene(organize: false);
        EditorUtility.DisplayDialog(
            "牌背 / 牌边面板",
            n > 0
                ? "已挂载 Inspector 引用。\n请立刻 Ctrl+S 保存场景。绘制请用「Tools/Game/烘焙场景设置绘制」。"
                : "当前打开的场景里找不到 CardBackPanel 或 EdgeSection。请先打开 MainScene。",
            "好的");
    }

    public static int BindOpenScene(bool organize = true)
    {
        GameObject backGo = FindInOpenScenes("CardBackPanel");
        GameObject edgeGo = FindInOpenScenes("EdgeSection");
        if (backGo == null && edgeGo == null) return 0;

        Scene scene = default;
        int count = 0;
        if (backGo != null)
        {
            Undo.RegisterFullObjectHierarchyUndo(backGo, UndoName);
            if (organize) OrganizeCardBack(backGo.transform);
            BindCardBack(backGo.GetComponent<CardBackConfigPanel>());
            EditorUtility.SetDirty(backGo);
            scene = backGo.scene;
            count++;
        }
        if (edgeGo != null)
        {
            Undo.RegisterFullObjectHierarchyUndo(edgeGo, UndoName);
            if (organize) OrganizeEdge(edgeGo.transform);
            BindEdge(edgeGo.GetComponent<CardEdgePanel>());
            EditorUtility.SetDirty(edgeGo);
            scene = edgeGo.scene;
            count++;
        }
        if (count > 0 && scene.IsValid())
        {
            EditorSceneManager.MarkSceneDirty(scene);
        }
        return count;
    }

    static void OrganizeCardBack(Transform root)
    {
        Transform swatchHost = FindDirectParentOf(root, "Swatch0");
        if (swatchHost != null)
        {
            Rename(swatchHost, "SwatchRow");
            List<RectTransform> swatches = CollectNumbered("Swatch", swatchHost, 10);
            ApplyGridLayoutMatching(swatchHost as RectTransform, swatches, 5);
        }

        Transform colorHost = FindDirectParentOf(root, "HexInput")
            ?? FindDirectParentOf(root, "RSlider");
        if (colorHost != null && colorHost != root)
        {
            Rename(colorHost, "ColorSliders");
            Transform rgb = FindDirectParentOf(colorHost, "RSlider");
            if (rgb != null && rgb != colorHost)
            {
                Rename(rgb, "RgbRows");
                Rename(FindDirectParentOf(rgb, "RSlider"), "RRow");
                Rename(FindDirectParentOf(rgb, "GSlider"), "GRow");
                Rename(FindDirectParentOf(rgb, "BSlider"), "BRow");
            }
            GroupNamedSiblings(colorHost, "HexRow", false,
                "HexLabel", "HexInput", "HexApply");
        }

        Transform imageHost = FindDirectParentOf(root, "RestoreButton");
        if (imageHost != null && imageHost != root)
        {
            Rename(imageHost, "ImageActions");
        }
    }

    static void OrganizeEdge(Transform root)
    {
        DisableModeRowLayouts(root);
        HideIfExists(root, "FrontTexFollowTableBg");
        HideIfExists(root, "FrontTexFollowTableBgToEdge");

        List<RectTransform> frontSwatches = CollectNumbered("SideSwatch", root, 10);
        RectTransform frontSwatchRow = GroupRects(root, "FrontSwatchRow", frontSwatches, horizontal: true);

        // 还原按钮单独绘制在固定坐标，不收进 HEX 行，避免被挪走。
        RectTransform frontHexRow = GroupNamedSiblings(root, "FrontHexRow", false,
            "SideHexInput", "SideHexApply");

        Transform existingSection = FindDeep(root, "FrontEdgeSection");
        bool sectionReady = existingSection != null
            && frontSwatchRow != null
            && frontSwatchRow.parent == existingSection;
        if (!sectionReady)
        {
            Transform preview = FindDeep(root, "FrontSidePreview");
            RectTransform frontSection = EnsureChild(root, "FrontEdgeSection");
            var frontColorItems = new List<RectTransform>();
            if (preview is RectTransform previewRt) frontColorItems.Add(previewRt);
            if (frontSwatchRow != null) frontColorItems.Add(frontSwatchRow);
            if (frontHexRow != null) frontColorItems.Add(frontHexRow);
            PlaceRowOverWorldBounds(frontSection, root, frontColorItems);
            MoveUnderKeepingWorld(frontSection, preview as RectTransform);
            MoveUnderKeepingWorld(frontSection, frontSwatchRow);
            MoveUnderKeepingWorld(frontSection, frontHexRow);
            PlaceAfterSibling(frontSection, FindDeep(root, "FrontEdgeRow"));
            existingSection = frontSection;
        }
        if (existingSection != null) EnsureCanvasGroup(existingSection.gameObject);

        Transform backSection = FindDeep(root, "BackEdgeSection");
        if (backSection != null)
        {
            List<RectTransform> backSwatches = CollectNumbered("BackEdgeSwatch", backSection, 10);
            if (backSwatches.Count == 0)
            {
                backSwatches = CollectNumbered("BackEdgeSwatch", root, 10);
            }
            GroupRects(backSection, "BackSwatchRow", backSwatches, horizontal: true);
            GroupNamedSiblings(backSection, "BackHexRow", false,
                "BackEdgeHexInput", "BackEdgeHexApply");
            EnsureCanvasGroup(backSection.gameObject);
        }
    }

    static void DisableModeRowLayouts(Transform root)
    {
        string[] names = { "FrontEdgeRow", "BackEdgeRow" };
        for (int i = 0; i < names.Length; i++)
        {
            Transform row = FindDeep(root, names[i]);
            if (row == null) continue;
            LayoutGroup[] groups = row.GetComponents<LayoutGroup>();
            for (int g = 0; g < groups.Length; g++)
            {
                if (groups[g] == null || !groups[g].enabled) continue;
                Undo.RecordObject(groups[g], UndoName);
                groups[g].enabled = false;
                EditorUtility.SetDirty(groups[g]);
            }
        }
    }

    static void BindCardBack(CardBackConfigPanel panel)
    {
        if (panel == null) return;
        Transform root = panel.transform;
        SerializedObject so = new SerializedObject(panel);
        so.Update();
        SetRef(so, "previewImage", FindComp<Image>(root, "PreviewImage"));
        SetRef(so, "sliderR", FindComp<Slider>(root, "RSlider"));
        SetRef(so, "sliderG", FindComp<Slider>(root, "GSlider"));
        SetRef(so, "sliderB", FindComp<Slider>(root, "BSlider"));
        SetRef(so, "valueR", FindComp<TMP_Text>(root, "RValue"));
        SetRef(so, "valueG", FindComp<TMP_Text>(root, "GValue"));
        SetRef(so, "valueB", FindComp<TMP_Text>(root, "BValue"));
        SetRef(so, "hexInput", FindComp<TMP_InputField>(root, "HexInput"));
        SetRef(so, "hexApplyButton", FindComp<Button>(root, "HexApply"));
        SetRef(so, "restoreButton", FindComp<Button>(root, "RestoreButton"));
        SetRef(so, "pickImageButton", FindComp<Button>(root, "PickImageButton"));
        SetRef(so, "dropZoneButton", FindComp<Button>(root, "DropZone"));
        SetRef(so, "clearImageButton", FindComp<Button>(root, "ClearImageButton"));
        SetButtonArray(so, "colorSwatches", CollectNumberedButtons(root, "Swatch", 10));
        so.ApplyModifiedProperties();
        EditorUtility.SetDirty(panel);
    }

    static void BindEdge(CardEdgePanel panel)
    {
        if (panel == null) return;
        Transform root = panel.transform;
        SerializedObject so = new SerializedObject(panel);
        so.Update();
        Image frontPreview = FindComp<Image>(root, "FrontSidePreview");
        SetRef(so, "sidePreview", frontPreview);
        SetRef(so, "frontSidePreview", frontPreview);
        SetRef(so, "backSidePreview", FindComp<Image>(root, "BackSidePreview"));
        SetRef(so, "sideHexInput", FindComp<TMP_InputField>(root, "SideHexInput"));
        SetRef(so, "sideHexApplyButton", FindComp<Button>(root, "SideHexApply"));
        SetRef(so, "frontEdgeHexInput",
            FindComp<TMP_InputField>(root, "FrontEdgeHexInput")
            ?? FindComp<TMP_InputField>(root, "SideHexInput"));
        SetRef(so, "frontEdgeHexApplyButton",
            FindComp<Button>(root, "FrontEdgeHexApply")
            ?? FindComp<Button>(root, "SideHexApply"));
        SetRef(so, "backEdgeHexInput", FindComp<TMP_InputField>(root, "BackEdgeHexInput"));
        SetRef(so, "backEdgeHexApplyButton", FindComp<Button>(root, "BackEdgeHexApply"));
        SetRef(so, "restoreFrontEdgeButton", FindComp<Button>(root, "RestoreFrontEdgeButton"));
        SetRef(so, "restoreBackEdgeButton", FindComp<Button>(root, "RestoreBackEdgeButton"));
        SetRef(so, "backEdgeModeIndependent", FindComp<Toggle>(root, "BackEdgeModeIndependent"));
        SetRef(so, "backEdgeModeFollowBack", FindComp<Toggle>(root, "BackEdgeModeFollowBack"));
        SetRef(so, "backEdgeModeFollowFront", FindComp<Toggle>(root, "BackEdgeModeFollowFront"));
        SetRef(so, "frontEdgeModeIndependent", FindComp<Toggle>(root, "FrontEdgeModeIndependent"));
        SetRef(so, "frontEdgeModeFollowTableBg", FindComp<Toggle>(root, "FrontEdgeModeFollowTableBg"));
        SetRef(so, "frontEdgeModeFollowBackEdge", FindComp<Toggle>(root, "FrontEdgeModeFollowBackEdge"));
        CanvasGroup backGroup = FindComp<CanvasGroup>(root, "BackEdgeSection");
        CanvasGroup frontGroup = FindComp<CanvasGroup>(root, "FrontEdgeSection");
        SetRef(so, "backEdgeSectionGroup", backGroup);
        SetRef(so, "frontEdgeSectionGroup", frontGroup);
        SetButtonArray(so, "sideSwatches", CollectNumberedButtons(root, "SideSwatch", 10));
        SetButtonArray(so, "backEdgeSwatches", CollectNumberedButtons(root, "BackEdgeSwatch", 10));
        SetButtonArray(so, "frontEdgeSwatches", CollectNumberedButtons(root, "FrontEdgeSwatch", 10));
        so.ApplyModifiedProperties();
        EditorUtility.SetDirty(panel);
    }

    static RectTransform GroupNamedSiblings(Transform searchRoot, string rowName, bool addLayout, params string[] names)
    {
        var rects = new List<RectTransform>();
        Transform parent = null;
        for (int i = 0; i < names.Length; i++)
        {
            Transform t = FindDeep(searchRoot, names[i]);
            if (t == null) continue;
            var rt = t as RectTransform;
            if (rt == null) continue;
            if (parent == null) parent = t.parent;
            if (t.parent == parent) rects.Add(rt);
        }
        if (parent == null || rects.Count == 0) return null;
        return GroupRects(parent, rowName, rects, horizontal: addLayout);
    }

    static RectTransform GroupRects(Transform parent, string rowName, List<RectTransform> items, bool horizontal)
    {
        if (parent == null || items == null || items.Count == 0) return parent != null ? parent.Find(rowName) as RectTransform : null;
        RectTransform row = EnsureChild(parent, rowName);
        for (int i = 0; i < items.Count; i++)
        {
            MoveUnderKeepingWorld(row, items[i]);
            items[i].SetSiblingIndex(i);
        }
        FitToChildren(row);
        if (horizontal)
        {
            ApplyHorizontalLayoutMatching(row, items);
        }
        return row;
    }

    static void ApplyHorizontalLayoutMatching(RectTransform row, List<RectTransform> items)
    {
        if (row == null || items == null || items.Count < 2) return;
        Vector3[] saved = SnapshotWorld(items);
        float spacing = AverageGap(items);
        HorizontalLayoutGroup layout = row.GetComponent<HorizontalLayoutGroup>();
        if (layout == null) layout = Undo.AddComponent<HorizontalLayoutGroup>(row.gameObject);
        layout.childAlignment = TextAnchor.MiddleCenter;
        layout.childControlWidth = false;
        layout.childControlHeight = false;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = false;
        layout.spacing = spacing;
        layout.padding = new RectOffset(0, 0, 0, 0);
        LayoutRebuilder.ForceRebuildLayoutImmediate(row);
        if (Drifted(items, saved, 0.75f))
        {
            RestoreWorld(items, saved);
            layout.enabled = false;
        }
    }

    static void ApplyGridLayoutMatching(RectTransform row, List<RectTransform> items, int columns)
    {
        if (row == null || items == null || items.Count < 2) return;
        Vector3[] saved = SnapshotWorld(items);
        Vector2 cell = items[0].rect.size;
        if (cell.x < 1f) cell = items[0].sizeDelta;
        float spaceX = 14f;
        float spaceY = 10f;
        if (items.Count >= 2)
        {
            spaceX = Mathf.Max(0f, Mathf.Abs(items[1].anchoredPosition.x - items[0].anchoredPosition.x) - cell.x);
        }
        if (items.Count > columns)
        {
            spaceY = Mathf.Max(0f, Mathf.Abs(items[0].anchoredPosition.y - items[columns].anchoredPosition.y) - cell.y);
        }
        GridLayoutGroup grid = row.GetComponent<GridLayoutGroup>();
        if (grid == null) grid = Undo.AddComponent<GridLayoutGroup>(row.gameObject);
        grid.cellSize = cell;
        grid.spacing = new Vector2(spaceX, spaceY);
        grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        grid.constraintCount = columns;
        grid.startCorner = GridLayoutGroup.Corner.UpperLeft;
        grid.startAxis = GridLayoutGroup.Axis.Horizontal;
        grid.childAlignment = TextAnchor.MiddleCenter;
        grid.padding = new RectOffset(0, 0, 0, 0);
        LayoutRebuilder.ForceRebuildLayoutImmediate(row);
        if (Drifted(items, saved, 0.75f))
        {
            RestoreWorld(items, saved);
            grid.enabled = false;
        }
    }

    static float AverageGap(List<RectTransform> items)
    {
        if (items.Count < 2) return 0f;
        var ordered = new List<RectTransform>(items);
        ordered.Sort((a, b) => a.position.x.CompareTo(b.position.x));
        float sum = 0f;
        int n = 0;
        for (int i = 1; i < ordered.Count; i++)
        {
            float gap = (ordered[i].position.x - ordered[i].rect.width * 0.5f)
                - (ordered[i - 1].position.x + ordered[i - 1].rect.width * 0.5f);
            if (gap >= -1f)
            {
                sum += Mathf.Max(0f, gap);
                n++;
            }
        }
        return n > 0 ? sum / n : 0f;
    }

    static Vector3[] SnapshotWorld(List<RectTransform> items)
    {
        var pos = new Vector3[items.Count];
        for (int i = 0; i < items.Count; i++) pos[i] = items[i].position;
        return pos;
    }

    static bool Drifted(List<RectTransform> items, Vector3[] saved, float epsilon)
    {
        for (int i = 0; i < items.Count; i++)
        {
            if (Vector3.Distance(items[i].position, saved[i]) > epsilon) return true;
        }
        return false;
    }

    static void RestoreWorld(List<RectTransform> items, Vector3[] saved)
    {
        for (int i = 0; i < items.Count; i++) items[i].position = saved[i];
    }

    static void MoveUnderKeepingWorld(RectTransform newParent, RectTransform child)
    {
        if (newParent == null || child == null) return;
        if (child.parent == newParent) return;
        Vector3 world = child.position;
        Quaternion rot = child.rotation;
        Vector3 scale = child.localScale;
        Undo.SetTransformParent(child, newParent, UndoName);
        child.position = world;
        child.rotation = rot;
        child.localScale = scale;
    }

    static void FitToChildren(RectTransform row)
    {
        PlaceRowOverWorldBounds(row, row.parent, null);
    }

    static void PlaceRowOverWorldBounds(RectTransform row, Transform space, List<RectTransform> items)
    {
        if (row == null) return;
        Transform measureParent = space != null ? space : row.parent;
        Bounds b = new Bounds();
        bool init = false;
        int count = items != null ? items.Count : row.childCount;
        for (int i = 0; i < count; i++)
        {
            RectTransform child = items != null
                ? items[i]
                : row.GetChild(i) as RectTransform;
            if (child == null) continue;
            Vector3[] corners = new Vector3[4];
            child.GetWorldCorners(corners);
            for (int c = 0; c < 4; c++)
            {
                Vector3 local = measureParent != null
                    ? measureParent.InverseTransformPoint(corners[c])
                    : corners[c];
                if (!init)
                {
                    b = new Bounds(local, Vector3.zero);
                    init = true;
                }
                else b.Encapsulate(local);
            }
        }
        if (!init) return;
        row.anchorMin = row.anchorMax = new Vector2(0.5f, 0.5f);
        row.pivot = new Vector2(0.5f, 0.5f);
        row.localRotation = Quaternion.identity;
        row.localScale = Vector3.one;
        if (measureParent != null)
        {
            row.position = measureParent.TransformPoint(b.center);
        }
        row.sizeDelta = new Vector2(b.size.x, b.size.y);
    }

    static RectTransform EnsureChild(Transform parent, string name)
    {
        Transform existing = parent.Find(name);
        if (existing != null) return existing as RectTransform;
        var go = new GameObject(name, typeof(RectTransform));
        Undo.RegisterCreatedObjectUndo(go, UndoName);
        go.layer = parent.gameObject.layer;
        var rt = go.GetComponent<RectTransform>();
        rt.SetParent(parent, false);
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.localScale = Vector3.one;
        rt.localRotation = Quaternion.identity;
        rt.anchoredPosition = Vector2.zero;
        rt.sizeDelta = Vector2.zero;
        return rt;
    }

    static void EnsureCanvasGroup(GameObject go)
    {
        if (go == null) return;
        if (go.GetComponent<CanvasGroup>() == null)
        {
            Undo.AddComponent<CanvasGroup>(go);
        }
    }

    static void PlaceAfterSibling(Transform target, Transform sibling)
    {
        if (target == null || sibling == null || target.parent != sibling.parent) return;
        target.SetSiblingIndex(sibling.GetSiblingIndex() + 1);
    }

    static void Rename(Transform t, string name)
    {
        if (t == null || t.name == name) return;
        Undo.RecordObject(t.gameObject, UndoName);
        t.name = name;
        EditorUtility.SetDirty(t.gameObject);
    }

    static void HideIfExists(Transform root, string name)
    {
        Transform t = FindDeep(root, name);
        if (t == null || !t.gameObject.activeSelf) return;
        Undo.RecordObject(t.gameObject, UndoName);
        t.gameObject.SetActive(false);
    }

    static Transform FindDirectParentOf(Transform searchRoot, string childName)
    {
        Transform child = FindDeep(searchRoot, childName);
        return child != null ? child.parent : null;
    }

    static List<RectTransform> CollectNumbered(string prefix, Transform searchRoot, int maxCount)
    {
        var list = new List<RectTransform>();
        for (int i = 0; i < maxCount; i++)
        {
            Transform t = FindDeep(searchRoot, prefix + i);
            if (t == null) break;
            var rt = t as RectTransform;
            if (rt != null) list.Add(rt);
        }
        return list;
    }

    static Button[] CollectNumberedButtons(Transform root, string prefix, int maxCount)
    {
        var list = new List<Button>();
        for (int i = 0; i < maxCount; i++)
        {
            Button b = FindComp<Button>(root, prefix + i);
            if (b == null) break;
            list.Add(b);
        }
        return list.ToArray();
    }

    static void SetRef(SerializedObject so, string field, UnityEngine.Object value)
    {
        SerializedProperty p = so.FindProperty(field);
        if (p != null) p.objectReferenceValue = value;
    }

    static void SetButtonArray(SerializedObject so, string field, Button[] buttons)
    {
        SerializedProperty p = so.FindProperty(field);
        if (p == null || !p.isArray) return;
        p.arraySize = buttons.Length;
        for (int i = 0; i < buttons.Length; i++)
        {
            p.GetArrayElementAtIndex(i).objectReferenceValue = buttons[i];
        }
    }

    static T FindComp<T>(Transform root, string name) where T : Component
    {
        Transform t = FindDeep(root, name);
        return t != null ? t.GetComponent<T>() : null;
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
