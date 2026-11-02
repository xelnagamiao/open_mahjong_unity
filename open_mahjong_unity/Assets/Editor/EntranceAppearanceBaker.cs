#if UNITY_EDITOR
using System;
using System.Linq;
using Salasasa.Home;
using Salasasa.RightEntrances;
using TMPro;
using UnityEditor;
using UnityEditor.Events;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class EntranceAppearanceBaker
{
    public static void Bake(Scene scene)
    {
        var roots = scene.GetRootGameObjects();
        var bridge = roots.SelectMany(root => root.GetComponentsInChildren<EntranceMainBridge>(true)).Single();
        BakeVenueWaitingCount(scene);
        var plans = roots.SelectMany(root => root.GetComponentsInChildren<InterfacePlansController>(true)).Single();
        if (bridge.palettes.Length != EntranceMainBridge.DisplayPaletteOrder.Length || bridge.palettes[EntranceMainBridge.DefaultPalette].name != "冷白橙红")
            throw new InvalidOperationException("Entrance palette IDs no longer match the saved player choices.");
        bridge.defaultPalette = EntranceMainBridge.DefaultPalette;
        foreach (var entry in bridge.entrances.buttons)
        {
            entry.surfaceTint = entry.visual.Find("Surface").GetComponent<EntranceTint>();
            entry.hoverWash = entry.hover.transform.Find("Hover wash").GetComponent<EntranceSurface>();
            entry.hoverEdge = entry.hover.transform.Find("Hover edge").GetComponent<EntranceSurface>();
            entry.selectedOutline = entry.selectedFrame.transform.Find("Selected frame").GetComponent<EntranceSurface>();
            entry.selectedInnerOutline = entry.selectedFrame.transform.Find("Inner frame").GetComponent<EntranceSurface>();
            EditorUtility.SetDirty(entry);
        }

        var row = plans.choices.Single(choice => choice.option == DesignOption.EntrancePalette);
        var byValue = row.values.Select((value, index) => new { value, button = row.buttons[index] }).ToDictionary(pair => pair.value, pair => pair.button);
        var slots = Slots(row.buttons);
        row.values = EntranceMainBridge.DisplayPaletteOrder.ToArray();
        row.buttons = row.values.Select(value => byValue[value]).ToArray();
        for (int i = 0; i < row.values.Length; i++)
        {
            var button = row.buttons[i];
            ((RectTransform)button.transform).anchoredPosition = slots[i];
            button.transform.SetAsLastSibling();
            button.onClick = new UnityEngine.UI.Button.ButtonClickedEvent();
            UnityEventTools.AddIntPersistentListener(button.onClick, plans.SelectEntrancePalette, row.values[i]);
            EditorUtility.SetDirty(button); EditorUtility.SetDirty(button.transform);
        }

        // The legacy picker keeps stable-index arrays; only its visual slots move.
        var pickerSlots = Slots(bridge.paletteButtons);
        for (int i = 0; i < EntranceMainBridge.DisplayPaletteOrder.Length; i++)
        {
            int value = EntranceMainBridge.DisplayPaletteOrder[i];
            var button = bridge.paletteButtons[value];
            ((RectTransform)button.transform).anchoredPosition = pickerSlots[i];
            button.transform.SetAsLastSibling();
            button.onClick = new UnityEngine.UI.Button.ButtonClickedEvent();
            UnityEventTools.AddIntPersistentListener(button.onClick, bridge.SelectPalette, value);
            EditorUtility.SetDirty(button); EditorUtility.SetDirty(button.transform);
        }
        foreach (var preset in plans.presetDefaults.Where(preset => preset.option == DesignOption.EntrancePalette))
            preset.value = EntranceMainBridge.DefaultPalette;
        bridge.SelectPalette(EntranceMainBridge.DefaultPalette, false);
        foreach (var graphic in bridge.entrances.GetComponentsInChildren<UnityEngine.UI.Graphic>(true)) EditorUtility.SetDirty(graphic);
        EditorUtility.SetDirty(bridge); EditorUtility.SetDirty(plans);
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(scene);
    }

    static Vector2[] Slots(UnityEngine.UI.Button[] buttons) => buttons
        .Select(button => ((RectTransform)button.transform).anchoredPosition)
        .OrderByDescending(position => position.y).ThenBy(position => position.x).ToArray();

    public static void BakeVenueWaitingCount(Scene scene)
    {
        var bridge = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<EntranceMainBridge>(true)).Single();
        var venue = bridge.entrances.buttons.Single(button => button.route == "event");
        var parent = venue.visual;
        var title = parent.GetComponentsInChildren<TMP_Text>(true).First(text => text.name == "Label");
        title.rectTransform.sizeDelta = new Vector2(200, title.rectTransform.sizeDelta.y);
        title.enableAutoSizing = true;
        title.fontSizeMin = 16;
        title.fontSizeMax = 23;

        var node = parent.Find("VenueWaitingCount");
        if (!node) node = new GameObject("VenueWaitingCount", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI), typeof(EntranceTint)).transform;
        node.SetParent(parent, false);
        var rect = (RectTransform)node;
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(1, .5f);
        rect.anchoredPosition = new Vector2(-48, 0);
        rect.sizeDelta = new Vector2(200, 42);
        var label = node.GetComponent<TMP_Text>();
        label.font = title.font;
        label.fontStyle = FontStyles.Normal;
        label.fontSize = 20;
        label.enableAutoSizing = true;
        label.fontSizeMin = 14;
        label.fontSizeMax = 20;
        label.alignment = TextAlignmentOptions.MidlineRight;
        label.raycastTarget = false;
        label.text = PlatformText.Format("等待房间数（{0}）", "—");
        var tint = node.GetComponent<EntranceTint>();
        tint.target = label;
        tint.tone = EntranceTone.OnDark;
        if (!bridge.tints.Contains(tint)) bridge.tints = bridge.tints.Concat(new[] { tint }).ToArray();
        var bindings = bridge.entrances.metrics.Where(binding => binding.metric != EntranceMetric.VenueWaiting).ToList();
        bindings.Add(new EntranceMetricBinding { label = label, metric = EntranceMetric.VenueWaiting, format = "等待房间数（{0}）" });
        bridge.entrances.metrics = bindings.ToArray();
        tint.Apply(bridge.palettes[bridge.defaultPalette]);
        EditorUtility.SetDirty(bridge);
        EditorUtility.SetDirty(bridge.entrances);
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(scene);
    }
}
#endif
