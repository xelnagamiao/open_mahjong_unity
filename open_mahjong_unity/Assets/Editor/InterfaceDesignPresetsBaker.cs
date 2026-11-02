#if UNITY_EDITOR
using System;
using System.Linq;
using Salasasa.Home;
using TMPro;
using UnityEditor;
using UnityEditor.Events;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public static class InterfaceDesignPresetsBaker
{
    public static void Bake(Scene scene)
    {
        var plans = Find(scene);
        plans.venue.SetVariant(0);

        var settings = plans.choices.Single(c => c.option == DesignOption.Settings);
        settings.values = SettingsStyleCollection.DisplayVariantOrder.ToArray();
        for (int i = 0; i < settings.buttons.Length; i++)
        {
            int value = settings.values[i];
            var button = settings.buttons[i];
            button.onClick = new Button.ButtonClickedEvent();
            UnityEventTools.AddIntPersistentListener(button.onClick, plans.SelectSettings, value);
            Label(button.GetComponentInChildren<TMP_Text>(true), SettingsStyleCollection.StyleNames[value]);
        }
        foreach (var page in plans.settings.bakedPages)
            for (int id = 0; id < page.variants.Length; id++)
            {
                var button = page.variants[id];
                int position = Array.IndexOf(SettingsStyleCollection.DisplayVariantOrder, id);
                Place((RectTransform)button.transform, 6 + position * 190, 5, 184, 38);
                Label(button.GetComponentInChildren<TMP_Text>(true), SettingsStyleCollection.StyleNames[id]);
            }
        plans.settings.ShowBakedPage(SettingsStyleCollection.DefaultVariant, 0);

        var restore = plans.restorePresetsButton;
        restore.onClick = new Button.ButtonClickedEvent();
        UnityEventTools.AddPersistentListener(restore.onClick, plans.RestoreDesignPreset);
        var restoreRect = (RectTransform)restore.transform;
        restoreRect.anchoredPosition = new Vector2(344, restoreRect.anchoredPosition.y);
        restoreRect.sizeDelta = new Vector2(188, 40);
        var caption = restore.GetComponentInChildren<TMP_Text>(true);
        Label(caption, "恢复全部预设");
        caption.fontSize = caption.fontSizeMax = 18;
        caption.fontSizeMin = 13;
        caption.enableAutoSizing = true;
        caption.textWrappingMode = TextWrappingModes.NoWrap;
        var localization = caption.GetComponent<LocalizedPlatformLabel>();
        if (localization)
        {
            localization.authoredSize = 18;
            localization.minimumSize = 13;
            localization.authoredAutoSize = true;
        }

        plans.presetDefaults = InterfacePlansController.RestorableDesignOptions
            .Select(option => new DesignPresetChoice { option = option, value = plans.DefaultDesignValue(option) }).ToArray();
        var pageRoot = plans.pages[1].transform;
        var rows = plans.choices.Where(g => g.buttons.Length > 0 && g.buttons[0])
            .Select(g => (RectTransform)g.buttons[0].transform.parent)
            .Where(row => row.parent == pageRoot && row.gameObject.activeSelf).Distinct()
            .OrderByDescending(row => row.anchoredPosition.y).ThenBy(row => row.GetSiblingIndex()).ToArray();
        float top = 0;
        foreach (var row in rows)
        {
            Place(row, 24, top, 552, row.sizeDelta.y);
            foreach (RectTransform child in row)
                if (child.rect.height <= 2 && child.GetComponent<Graphic>())
                    child.anchoredPosition = new Vector2(child.anchoredPosition.x, -row.sizeDelta.y + 2);
            top += row.sizeDelta.y;
        }
        plans.pageHeights[1] = top + 128;
        EditorUtility.SetDirty(plans);
        EditorUtility.SetDirty(plans.settings);
        EditorUtility.SetDirty(plans.venue);
        Validate(scene);
    }

    public static void Validate(Scene scene)
    {
        var plans = Find(scene);
        if (plans.choices.Any(c => !Enum.IsDefined(typeof(DesignOption), c.option))
            || plans.pages[1].transform.Find("Venue") || plans.venue.Variant != 0)
            throw new InvalidOperationException("The venue frame must be fixed without a design option.");
        var settings = plans.choices.Single(c => c.option == DesignOption.Settings);
        if (!settings.values.SequenceEqual(SettingsStyleCollection.DisplayVariantOrder)
            || settings.values[0] != SettingsStyleCollection.DefaultVariant)
            throw new InvalidOperationException("The summer settings style must be first and the default.");
        if (!plans.restorePresetsButton || plans.restorePresetsButton.onClick.GetPersistentEventCount() != 1
            || plans.restorePresetsButton.onClick.GetPersistentMethodName(0) != nameof(InterfacePlansController.RestoreDesignPreset))
            throw new InvalidOperationException("Restore all presets is not bound to the complete restore operation.");
        if (plans.presetDefaults.Select(p => p.option).Distinct().Count() != plans.presetDefaults.Length
            || !plans.presetDefaults.Select(p => p.option).SequenceEqual(InterfacePlansController.RestorableDesignOptions)
            || plans.presetDefaults.Any(p => p.value != plans.DefaultDesignValue(p.option)))
            throw new InvalidOperationException("The baked preset defaults differ from the first-run defaults.");
        foreach (var group in plans.choices)
            if (!plans.presetDefaults.Any(p => p.option == group.option))
                throw new InvalidOperationException("No restore default for design option " + group.option);
        if (!Application.isPlaying)
        {
            var visible = plans.settings.bakedPages.Where(p => p.gameObject.activeSelf).ToArray();
            if (visible.Length != 1 || visible[0].variant != SettingsStyleCollection.DefaultVariant)
                throw new InvalidOperationException("The summer settings page is not the baked initial page.");
        }
    }
    static InterfacePlansController Find(Scene scene) => scene.GetRootGameObjects()
        .SelectMany(root => root.GetComponentsInChildren<InterfacePlansController>(true)).Single();
    static void Label(TMP_Text label, string value)
    {
        label.text = value;
        label.fontStyle = FontStyles.Normal;
        label.fontWeight = FontWeight.Regular;
        var localization = label.GetComponent<LocalizedPlatformLabel>();
        if (localization) localization.key = value;
    }
    static void Place(RectTransform rect, float left, float top, float width, float height)
    {
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0, 1);
        rect.anchoredPosition = new Vector2(left, -top);
        rect.sizeDelta = new Vector2(width, height);
    }
}
#endif
