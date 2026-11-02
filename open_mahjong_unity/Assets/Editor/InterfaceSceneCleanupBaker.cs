#if UNITY_EDITOR
using System;
using System.Linq;
using Salasasa.Home;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class InterfaceSceneCleanupBaker {
    public static void Bake(Scene scene) {
        var plans = Find(scene);
        var ranking = plans.ranking;
        var retained = ranking.paletteEditor.view;
        foreach (var old in ranking.views.Where(v => v && v != retained).ToArray())
            UnityEngine.Object.DestroyImmediate(old.gameObject);
        ranking.views = new[] { retained };
        retained.gameObject.SetActive(true);
        Remove(ranking.popup.transform, "PaletteEditor", "PalettePresets", "DesignSwitcher");
        Remove(retained.transform, "PaletteButton");
        Remove(plans.frame, "PlansBody/Colors", "PlansTabs/Tab2", "ExpansionChoices");
        plans.pages = plans.pages.Where(p => p && (p.name == "Global" || p.name == "Pages")).ToArray();
        plans.tabs = plans.tabs.Where(t => t).Take(2).ToArray();
        plans.pageHeights = plans.pageHeights.Take(2).ToArray();
        for (int i = 0; i < plans.tabs.Length; i++) {
            var rect = (RectTransform)plans.tabs[i].transform;
            rect.anchoredPosition = new Vector2(i * 280, 0);
            rect.sizeDelta = new Vector2(272, 40);
        }
        var module = plans.statistics.transform.parent;
        Remove(module, "PaletteEditor");
        ranking.paletteEditor.shapes = ranking.paletteEditor.shapes.Where(b => b != null && b.graphic).ToArray();
        ranking.paletteEditor.texts = ranking.paletteEditor.texts.Where(b => b != null && b.text).ToArray();
        plans.statistics.tints.RemoveAll(b => b == null || !b.graphic);
        EditorUtility.SetDirty(ranking.paletteEditor);
        EditorUtility.SetDirty(plans.statistics);
        plans.SelectPage(0);
        EditorUtility.SetDirty(plans); EditorUtility.SetDirty(ranking);
        EditorSceneManager.MarkSceneDirty(scene);
        Validate(scene);
    }
    public static void Validate(Scene scene) {
        var plans = Find(scene);
        if (plans.pages.Length != 2 || plans.tabs.Length != 2 || plans.pageHeights.Length != 2
            || plans.pages[0].name != "Global" || plans.pages[1].name != "Pages"
            || plans.frame.Find("PlansBody/Colors") || plans.frame.Find("PlansTabs/Tab2"))
            throw new InvalidOperationException("Interface plans must contain only Global and Pages.");
        if (plans.ranking.views.Length != 1 || plans.ranking.views[0] != plans.ranking.paletteEditor.view
            || plans.ranking.GetComponentsInChildren<Salasasa.Leaderboards.LeaderboardView>(true).Length != 1
            || plans.ranking.popup.transform.Find("PaletteEditor") || plans.statistics.transform.parent.Find("PaletteEditor"))
            throw new InvalidOperationException("Obsolete ranking layouts or palette editors remain in the scene.");
    }
    static InterfacePlansController Find(Scene scene) => scene.GetRootGameObjects()
        .SelectMany(r => r.GetComponentsInChildren<InterfacePlansController>(true)).Single();
    static void Remove(Transform parent, params string[] paths) {
        foreach (var path in paths) {
            var child = parent.Find(path);
            if (child) UnityEngine.Object.DestroyImmediate(child.gameObject);
        }
    }
}
#endif
