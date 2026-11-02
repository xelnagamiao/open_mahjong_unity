#if UNITY_EDITOR
using System;
using System.Linq;
using Salasasa.DataLab;
using Salasasa.Statistics;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>Stores the production statistics page, including its editor-visible presentation.</summary>
public static class DataPageUiBaker {
    [MenuItem("Tools/Mahjong/Main Scene/Bake Data Page")]
    public static void BakeActiveScene() {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("请先退出播放模式。");
        Bake(SceneManager.GetActiveScene());
    }

    public static void Bake(Scene scene) {
        var bridge = Find<StatisticsLobbyBridge>(scene).Single();
        var panel = bridge.panel;
        Undo.RegisterFullObjectHierarchyUndo(bridge.gameObject, "Bake production data page");
        bridge.name = "DataPanel";
        bridge.viewport.name = "Viewport";
        panel.pageRoot.name = "ContentPanel";
        panel.pageRoot.GetComponent<StatisticsWhitespaceFit>().Shell.name = "DataLayout";
        panel.gameObject.name = "DataController";
        var preview = panel.pageRoot.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == "DesignVariants");
        if (preview) {
            var retired=preview.gameObject;
            panel.tints.RemoveAll(t => t == null || !t.graphic || t.graphic.transform.IsChildOf(retired.transform));
            UnityEngine.Object.DestroyImmediate(retired);
        }
        panel.BakePresentation();
        bridge.GetComponent<CanvasReferenceLayout>().Apply(true);
        bridge.ApplyInsets();
        panel.pageRoot.GetComponent<StatisticsWhitespaceFit>().ApplyNow();
        foreach (var component in bridge.GetComponentsInChildren<Component>(true))
            if (component) EditorUtility.SetDirty(component);
        EditorSceneManager.MarkSceneDirty(scene);
        Validate(scene);
    }

    public static void Validate(Scene scene) {
        var bridge = Find<StatisticsLobbyBridge>(scene).Single();
        var panel = bridge.panel;
        var fit = panel.pageRoot.GetComponent<StatisticsWhitespaceFit>();
        if (bridge.name != "DataPanel" || bridge.transform.parent.name != "PageRoot" || !fit || !fit.IsWide)
            throw new InvalidOperationException("The production data page must be baked under MainCanvas/PageRoot/DataPanel.");
        if (Find<MonoBehaviour>(scene).Any(c => c && c.GetType().FullName == "DataPanel"))
            throw new InvalidOperationException("The retired combined ranking/data page remains in MainScene.");
        if (!panel.mainRules || !panel.allRules || !panel.playerIdInput || !panel.queryPlayer || !panel.visitDataStation
            || !panel.fullscreen || panel.recordLayouts.Count != 1 || panel.rows.Count != 6
            || panel.rows.Any(row => !row.root || !row.card || row.card.layoutIndex != 4))
            throw new InvalidOperationException("Production statistics bindings or selected six-row layout are missing.");
        if (panel.useSnapshot) throw new InvalidOperationException("Production statistics cannot use fixture data.");
        if (fit.Shell.rect.width <= 0 || fit.Shell.localScale != Vector3.one)
            throw new InvalidOperationException("Data layout has invalid authored geometry.");
    }

    static T[] Find<T>(Scene scene) where T : Component => scene.GetRootGameObjects()
        .SelectMany(root => root.GetComponentsInChildren<T>(true)).ToArray();
}
#endif
