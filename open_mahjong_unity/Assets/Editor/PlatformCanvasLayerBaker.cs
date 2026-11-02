using System;
using System.IO;
using System.Linq;
using Salasasa.Home;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class PlatformCanvasLayerBaker {
    static readonly string[] Layers = { UiCanvasLayers.Board, UiCanvasLayers.Page, UiCanvasLayers.Navigation,
        UiCanvasLayers.Overlay, UiCanvasLayers.Modal, UiCanvasLayers.Design, UiCanvasLayers.Feedback };

    [MenuItem("Tools/Mahjong/Main Scene/Repair Overlay Sorting Layers")]
    public static void BakeActiveScene() {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("请先退出播放模式。");
        var scene = SceneManager.GetActiveScene();
        Bake(scene);
        Validate(scene);
        EditorSceneManager.MarkSceneDirty(scene);
    }

    public static int Bake(Scene scene) {
        EnsureLayers();
        var roots = scene.GetRootGameObjects();
        var main = roots.Single(x => x.name == "MainCanvas").GetComponent<Canvas>();
        var overlay = roots.Single(x => x.name == "OverlayCanvas").GetComponent<Canvas>();
        var header = main.GetComponentInChildren<FourSideHeader>(true);
        int changed = 0;
        foreach (var canvas in main.GetComponentsInChildren<Canvas>(true)) {
            string layer = canvas.transform.IsChildOf(header.transform) ? UiCanvasLayers.Navigation : UiCanvasLayers.Page;
            int order = canvas == header.canvas ? UiCanvasLayers.NavigationOrder : canvas.transform.IsChildOf(header.transform) ? UiCanvasLayers.NavigationOrder + 10 : canvas.sortingOrder;
            changed += Set(canvas, layer, order, canvas == header.canvas || canvas.overrideSorting);
        }
        var existingModal = overlay.transform.Find("ModalRoot");
        var existingFeedback = overlay.transform.Find("FeedbackOverlay");
        var existingPointer = overlay.transform.Find("PointerClickCanvas");
        var plans = overlay.GetComponentInChildren<InterfacePlansController>(true);
        foreach (var canvas in overlay.GetComponentsInChildren<Canvas>(true)) {
            bool feedbackCanvas = (existingFeedback && canvas.transform.IsChildOf(existingFeedback)) ||
                (existingPointer && canvas.transform.IsChildOf(existingPointer));
            string layer = plans && canvas.transform.IsChildOf(plans.transform) ? UiCanvasLayers.Design : feedbackCanvas ? UiCanvasLayers.Feedback :
                existingModal && canvas.transform.IsChildOf(existingModal) ? UiCanvasLayers.Modal : UiCanvasLayers.Overlay;
            int order = canvas.sortingOrder;
            if (layer == UiCanvasLayers.Overlay && order < UiCanvasLayers.OverlayOrder) order += UiCanvasLayers.OverlayOrder;
            changed += Set(canvas, layer, order, canvas.overrideSorting);
        }

        var modal = overlay.transform.Find("ModalRoot");
        if (!modal) {
            var go = new GameObject("ModalRoot", typeof(RectTransform), typeof(Canvas), typeof(UnityEngine.UI.GraphicRaycaster));
            Undo.RegisterCreatedObjectUndo(go, "Create modal UI group");
            go.layer = overlay.gameObject.layer;
            modal = go.transform;
            modal.SetParent(overlay.transform, false);
            var rect = (RectTransform)modal;
            rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
        }
        changed += Set(modal.GetComponent<Canvas>(), UiCanvasLayers.Modal, UiCanvasLayers.ModalOrder, true);
        var notifications = roots.SelectMany(r => r.GetComponentsInChildren<NotificationManager>(true)).Single();
        var messagePosition = new SerializedObject(notifications).FindProperty("messagePosition").objectReferenceValue as GameObject;
        if (messagePosition && messagePosition.transform.parent != modal) {
            Undo.SetTransformParent(messagePosition.transform, modal, "Group notification messages");
            changed++;
        }
        SetPath(overlay, "StorePanel", UiCanvasLayers.InventoryOrder);
        SetPath(overlay, "GraphiteChat", UiCanvasLayers.ChatOrder);
        SetPath(overlay, "MailPanel", UiCanvasLayers.MailOrder);
        SetPath(overlay, "Mahjong Classroom", UiCanvasLayers.OverlayOrder + 280);
        SetPath(overlay, "DeveloperOptionsPanel", UiCanvasLayers.OverlayOrder + 290);
        SetPath(overlay, "RankPolicyPanel", UiCanvasLayers.OverlayOrder + 620);
        SetPath(overlay, "NotificationPanel/DescribePos/DescribePanel", UiCanvasLayers.OverlayOrder + 620);
        SetPath(overlay, "NotificationPanel/PlayerPanelPos", UiCanvasLayers.PlayerInfoOrder);
        SetPath(overlay, "LobbyOverlay/LeaderboardCanvas", UiCanvasLayers.RankingOrder);
        // Design controls must stay available while the lobby-only overlays are disabled at the table.
        if (plans) {
            if (plans.transform.parent != overlay.transform)
                Undo.SetTransformParent(plans.transform, overlay.transform, "Keep interface plans available in every scene mode");
            if (plans.name != "InterfacePlansPanel") {
                Undo.RecordObject(plans.gameObject, "Name interface plans panel");
                plans.name = "InterfacePlansPanel";
            }
            changed += Set(plans.GetComponent<Canvas>(), UiCanvasLayers.Design, UiCanvasLayers.DesignOptionsOrder, true);
        }
        SetPath(overlay, "PageOverlays/RulebookOverlay", UiCanvasLayers.RulebookOrder);
        var player = overlay.GetComponentInChildren<PlayerInfoPanel>(true);
        var playerPosition = (RectTransform)player.transform.parent;
        var fit = playerPosition.GetComponent<ViewportPanelFit>();
        if (!fit) {
            fit = Undo.AddComponent<ViewportPanelFit>(playerPosition.gameObject);
            fit.authoredPosition = playerPosition.anchoredPosition;
        }
        Undo.RecordObject(fit, "Fit player data to the available viewport");
        fit.panel = (RectTransform)player.transform; fit.navigation = header;
        EditorUtility.SetDirty(fit);

        var feedback = overlay.transform.Find("FeedbackOverlay");
        if (feedback) {
            foreach (var canvas in feedback.GetComponentsInChildren<Canvas>(true)) {
                int order = UiCanvasLayers.FeedbackOrder + (canvas.transform == feedback ? 0 : canvas.name == "TipsPos" ? 20 : 10);
                changed += Set(canvas, UiCanvasLayers.Feedback, order, true);
            }
        }
        var pointer = overlay.transform.Find("PointerClickCanvas");
        if (pointer) {
            changed += Set(pointer.GetComponent<Canvas>(), UiCanvasLayers.Feedback, UiCanvasLayers.FeedbackOrder + 100, true);
            foreach (var graphic in pointer.GetComponentsInChildren<UnityEngine.UI.Graphic>(true)) {
                if (!graphic.raycastTarget) continue;
                Undo.RecordObject(graphic, "Keep pointer effects nonblocking");
                graphic.raycastTarget = false; EditorUtility.SetDirty(graphic);
            }
        }
        var viewport = roots.SelectMany(r => r.GetComponentsInChildren<FixedAspectRatio>(true)).Single();
        var viewportData = new SerializedObject(viewport);
        var canvases = viewportData.FindProperty("viewportCanvases");
        var viewportRoots = new[] { main, overlay, roots.Single(r => r.name == "#GameCanvas").GetComponent<Canvas>() };
        canvases.arraySize = viewportRoots.Length;
        for (int i = 0; i < viewportRoots.Length; i++) canvases.GetArrayElementAtIndex(i).objectReferenceValue = viewportRoots[i];
        viewportData.ApplyModifiedProperties();
        foreach (var root in viewportRoots) {
            Undo.RecordObject(root, "Save initial UI viewport mode");
            root.renderMode = RenderMode.ScreenSpaceOverlay; root.worldCamera = null; root.targetDisplay = 0;
            EditorUtility.SetDirty(root);
        }
        foreach (var root in roots) changed += BindDropdowns(root);
        return changed;
    }

    static void SetPath(Canvas overlay, string path, int order) {
        var target = overlay.transform.Find(path);
        if (!target) return;
        var canvas = target.GetComponent<Canvas>();
        if (canvas) Set(canvas, UiCanvasLayers.Overlay, order, true);
    }

    static int Set(Canvas canvas, string layer, int order, bool independent) {
        if (!canvas) return 0;
        independent &= canvas.transform.parent != null;
        int id = SortingLayer.NameToID(layer);
        if (canvas.sortingLayerID == id && canvas.sortingOrder == order && canvas.overrideSorting == independent) return 0;
        Undo.RecordObject(canvas, "Unify UI drawing layers");
        var serialized = new SerializedObject(canvas);
        serialized.FindProperty("m_OverrideSorting").boolValue = independent;
        serialized.FindProperty("m_SortingLayerID").intValue = id;
        serialized.FindProperty("m_SortingOrder").intValue = order;
        serialized.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(canvas);
        return 1;
    }

    static int BindDropdowns(GameObject root) {
        int changed = 0;
        foreach (var dropdown in root.GetComponentsInChildren<TMP_Dropdown>(true)) {
            if (!dropdown.template) continue;
            var scope = dropdown.template.GetComponent<DropdownCanvasScope>();
            if (!scope) { scope = Undo.AddComponent<DropdownCanvasScope>(dropdown.template.gameObject); changed++; }
            if (scope.owner != dropdown) {
                Undo.RecordObject(scope, "Bind dropdown window scope");
                scope.owner = dropdown; EditorUtility.SetDirty(scope); changed++;
            }
            Canvas canvas = dropdown.template.GetComponent<Canvas>();
            Canvas owner = UiCanvasLayers.SortingCanvas(dropdown.transform);
            if (canvas && owner) changed += Set(canvas, owner.sortingLayerName, owner.sortingOrder + 20, true);
        }
        return changed;
    }

    public static int BakePrefabs() {
        int changed = 0;
        foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { "Assets" })) {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            // Include lazily loaded rule configuration panels as well as common UI prefabs.
            if (!File.ReadAllText(path).Contains("m_Template:")) continue;
            var root = PrefabUtility.LoadPrefabContents(path);
            try {
                int count = BindDropdowns(root);
                if (count == 0) continue;
                PrefabUtility.SaveAsPrefabAsset(root, path); changed += count;
            } finally { PrefabUtility.UnloadPrefabContents(root); }
        }
        return changed;
    }

    static void EnsureLayers() {
        var data = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
        var layers = data.FindProperty("m_SortingLayers");
        string previous = "Default";
        foreach (string name in Layers) {
            int previousIndex = FindLayer(layers, previous), index = FindLayer(layers, name);
            if (index < 0) {
                layers.InsertArrayElementAtIndex(layers.arraySize);
                index = layers.arraySize - 1;
                var entry = layers.GetArrayElementAtIndex(index);
                entry.FindPropertyRelative("name").stringValue = name;
                uint id;
                do { id = BitConverter.ToUInt32(Guid.NewGuid().ToByteArray(), 0); }
                while (Enumerable.Range(0, index).Any(i => layers.GetArrayElementAtIndex(i).FindPropertyRelative("uniqueID").longValue == id));
                entry.FindPropertyRelative("uniqueID").longValue = id;
                entry.FindPropertyRelative("locked").boolValue = false;
            }
            int target = previousIndex + 1;
            if (index != target) layers.MoveArrayElement(index, target);
            previous = name;
        }
        data.ApplyModifiedProperties();
    }

    static int FindLayer(SerializedProperty layers, string name) {
        for (int i = 0; i < layers.arraySize; i++)
            if (layers.GetArrayElementAtIndex(i).FindPropertyRelative("name").stringValue == name) return i;
        return -1;
    }

    public static void Validate(Scene scene) {
        var values = Layers.Select(n => SortingLayer.GetLayerValueFromName(n)).ToArray();
        if (values.Distinct().Count() != Layers.Length || !values.SequenceEqual(values.OrderBy(v => v)))
            throw new InvalidOperationException("UI sorting layer order is invalid.");
        var roots = scene.GetRootGameObjects();
        var main = roots.Single(r => r.name == "MainCanvas").GetComponent<Canvas>();
        var overlay = roots.Single(r => r.name == "OverlayCanvas").GetComponent<Canvas>();
        var header = main.GetComponentInChildren<FourSideHeader>(true);
        if (header.canvas.sortingLayerName != UiCanvasLayers.Navigation || !header.canvas.overrideSorting || header.canvas.sortingOrder != UiCanvasLayers.NavigationOrder)
            throw new InvalidOperationException("Header must use the navigation drawing layer.");
        if (main.sortingLayerName != UiCanvasLayers.Page || overlay.sortingLayerName != UiCanvasLayers.Overlay)
            throw new InvalidOperationException("Page or overlay root uses the wrong drawing layer.");
        var modal = overlay.transform.Find("ModalRoot");
        var messages = roots.SelectMany(r => r.GetComponentsInChildren<NotificationManager>(true)).Single();
        var messagePosition = new SerializedObject(messages).FindProperty("messagePosition").objectReferenceValue as GameObject;
        if (!modal || !messagePosition || messagePosition.transform.parent != modal ||
            modal.GetComponent<Canvas>().sortingLayerName != UiCanvasLayers.Modal)
            throw new InvalidOperationException("Notification dialogs must use the modal group.");
        var plans = overlay.GetComponentInChildren<InterfacePlansController>(true);
        if (!plans || plans.transform.parent != overlay.transform ||
            plans.GetComponent<Canvas>().sortingLayerName != UiCanvasLayers.Design ||
            !plans.GetComponent<Canvas>().overrideSorting)
            throw new InvalidOperationException("Interface plans must have an independent drawing layer outside lobby-only visibility.");
        var viewport = roots.SelectMany(r => r.GetComponentsInChildren<FixedAspectRatio>(true)).Single();
        var viewportData = new SerializedObject(viewport).FindProperty("viewportCanvases");
        var expectedRoots = new[] { main, overlay, roots.Single(r => r.name == "#GameCanvas").GetComponent<Canvas>() };
        if (viewportData.arraySize != expectedRoots.Length || expectedRoots.Any(c =>
            !Enumerable.Range(0, viewportData.arraySize).Any(i => viewportData.GetArrayElementAtIndex(i).objectReferenceValue == c)))
            throw new InvalidOperationException("Viewport switching must include every authored screen UI root.");
        foreach (var canvas in overlay.GetComponentsInChildren<Canvas>(true)) {
            if (!canvas.overrideSorting) continue;
            if (!new[] { UiCanvasLayers.Overlay, UiCanvasLayers.Modal, UiCanvasLayers.Design, UiCanvasLayers.Feedback }.Contains(canvas.sortingLayerName))
                throw new InvalidOperationException("Overlay escaped its drawing layers: " + canvas.name);
            int lower = canvas.sortingLayerName == UiCanvasLayers.Overlay ? UiCanvasLayers.OverlayOrder :
                canvas.sortingLayerName == UiCanvasLayers.Modal ? UiCanvasLayers.ModalOrder :
                canvas.sortingLayerName == UiCanvasLayers.Design ? UiCanvasLayers.DesignOptionsOrder : UiCanvasLayers.FeedbackOrder;
            int upper = canvas.sortingLayerName == UiCanvasLayers.Overlay ? UiCanvasLayers.ModalOrder :
                canvas.sortingLayerName == UiCanvasLayers.Modal ? UiCanvasLayers.DesignOptionsOrder :
                canvas.sortingLayerName == UiCanvasLayers.Design ? UiCanvasLayers.FeedbackOrder : short.MaxValue;
            if (canvas.sortingOrder < lower || canvas.sortingOrder >= upper)
                throw new InvalidOperationException("Canvas order disagrees with its drawing layer: " + canvas.name);
        }
        foreach (var dropdown in roots.SelectMany(r => r.GetComponentsInChildren<TMP_Dropdown>(true))) {
            if (dropdown.template && dropdown.template.GetComponent<DropdownCanvasScope>()?.owner != dropdown)
                throw new InvalidOperationException("Dropdown has no window scope: " + dropdown.name);
        }
        var pointer = overlay.transform.Find("PointerClickCanvas");
        if (pointer && pointer.GetComponentsInChildren<UnityEngine.UI.Graphic>(true).Any(g => g.raycastTarget))
            throw new InvalidOperationException("Pointer effects must not intercept input.");
    }
}
