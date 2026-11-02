#if UNITY_EDITOR
using System;
using System.Linq;
using Salasasa.Home;
using TMPro;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public static class TableHudClarityBaker {
    public static void Bake(Scene scene) {
        var roots = scene.GetRootGameObjects();
        var simple = roots.SelectMany(r => r.GetComponentsInChildren<SimpleRoundPanel>(true)).Single();
        var binding = new SerializedObject(simple);
        var roundLabel = (TMP_Text)binding.FindProperty("roomNowRoundText").objectReferenceValue;
        var scores = SichuanRoundScorePanel.Create(roundLabel);
        binding.FindProperty("sichuanScores").objectReferenceValue = scores;
        binding.ApplyModifiedPropertiesWithoutUndo();
        scores.gameObject.SetActive(false);

        var layout = roots.SelectMany(r => r.GetComponentsInChildren<TableHudUtilityLayout>(true)).Single();
        var rects = roots.SelectMany(r => r.GetComponentsInChildren<RectTransform>(true)).ToArray();
        layout.latency = rects.Single(r => r.name == "Delay" && r.parent.name == "GamePageControl");
        layout.replayTips = rects.Single(r => r.name == "MouseControllerTips");
        foreach (var rect in new[] { layout.latency, layout.replayTips }) {
            var text = rect.GetComponentInChildren<TMP_Text>(true);
            text.rectTransform.anchorMin = Vector2.zero;
            text.rectTransform.anchorMax = Vector2.one;
            text.rectTransform.offsetMin = text.rectTransform.offsetMax = Vector2.zero;
            if (rect == layout.replayTips) {
                var backing = rect.GetComponent<Image>();
                if (backing) { backing.enabled = true; backing.color = new Color(0.5415094f, 0.57174706f, 1f, 0.50980395f); EditorUtility.SetDirty(backing); }
                text.color = Color.white;
                text.fontSize = text.fontSizeMax = 17.4f;
                text.fontSizeMin = 12;
                text.enableAutoSizing = true;
                text.fontStyle = FontStyles.Normal;
                text.fontWeight = FontWeight.Regular;
                var locale = text.GetComponent<LocalizedPlatformLabel>();
                if (locale) {
                    locale.authoredSize = 17.4f;
                    locale.minimumSize = 12;
                    locale.authoredAutoSize = true;
                    EditorUtility.SetDirty(locale);
                }
            }
            EditorUtility.SetDirty(text);
            EditorUtility.SetDirty(text.rectTransform);
            EditorUtility.SetDirty(rect);
        }
        var horizontal = layout.exits.GetComponent<HorizontalLayoutGroup>();
        if (horizontal) horizontal.enabled = false;
        var buttons = layout.utility.Cast<Transform>().Concat(layout.exits.Cast<Transform>())
            .Select(t => t.GetComponent<Button>()).Where(b => b).ToArray();
        foreach (var button in buttons) {
            var background = button.transform.Find("UtilityBackground");
            if (background) UnityEngine.Object.DestroyImmediate(background.gameObject);
            var label = button.GetComponentInChildren<TMP_Text>(true);
            if (label) {
                var locale = label.GetComponent<LocalizedPlatformLabel>();
                if (locale) {
                    locale.authoredSize = 18;
                    locale.minimumSize = 12;
                    locale.authoredAutoSize = true;
                    EditorUtility.SetDirty(locale);
                }
            }
        }
        foreach (var target in new[] { layout.utility, layout.exits }) {
            var canvas = target.GetComponent<Canvas>();
            if (!canvas) canvas = target.gameObject.AddComponent<Canvas>();
            UiCanvasLayers.Apply(canvas, UiCanvasLayers.Board, 10);
            if (!target.GetComponent<GraphicRaycaster>()) target.gameObject.AddComponent<GraphicRaycaster>();
            EditorUtility.SetDirty(canvas);
        }
        layout.RefreshLayout();
        var plans = roots.SelectMany(r => r.GetComponentsInChildren<InterfacePlansController>(true)).Single();
        RemoveRetiredChoice(plans);
        foreach (var rule in roots.SelectMany(r => r.GetComponentsInChildren<RoundPanel>(true))
            .SelectMany(r => new[] { r.transform.Find("SimpleRoundPanel/Header/Rule"), r.transform.Find("RiichiRoundPanel/Header/Rule") }))
            if (rule) rule.GetComponent<TMP_Text>().color = Color.black;
        foreach (var component in scores.GetComponentsInChildren<Component>(true)) if (component) EditorUtility.SetDirty(component);
        foreach (var button in buttons)
            foreach (var component in button.GetComponentsInChildren<Component>(true)) if (component) EditorUtility.SetDirty(component);
        EditorUtility.SetDirty(layout);
        EditorUtility.SetDirty(simple);
        EditorUtility.SetDirty(plans);
        EditorSceneManager.MarkSceneDirty(scene);
    }

    static void RemoveRetiredChoice(InterfacePlansController plans) {
        const int retiredTableUtilityOption = 25;
        foreach (var group in plans.choices.Where(g => (int)g.option == retiredTableUtilityOption)) {
            var button = group.buttons?.FirstOrDefault(b => b);
            if (button) UnityEngine.Object.DestroyImmediate(button.transform.parent.gameObject);
        }
        plans.choices = plans.choices.Where(g => (int)g.option != retiredTableUtilityOption).ToArray();
        plans.presetDefaults = plans.presetDefaults.Where(g => (int)g.option != retiredTableUtilityOption).ToArray();
        var page = plans.pages[1].transform;
        float bottom = plans.choices.Where(g => g.buttons != null && g.buttons.Length > 0 && g.buttons[0])
            .Select(g => (RectTransform)g.buttons[0].transform.parent).Where(r => r.parent == page && r.gameObject.activeSelf)
            .Select(r => -r.anchoredPosition.y + r.sizeDelta.y).DefaultIfEmpty(0).Max();
        plans.pageHeights[1] = bottom + 128;
    }
}
#endif
