#if UNITY_EDITOR
using System.Linq;
using System.Reflection;
using Salasasa.Home;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Adds the persistent 1x/2x/4x MSAA choice to every display settings prefab.
/// The row is authored into the same three themed pages as the existing display
/// controls so runtime pages do not need to create UI objects dynamically.
/// </summary>
public static class MsaaSettingsBaker {
    [MenuItem("Tools/Mahjong/Bake MSAA setting")]
    public static void Bake() {
        var settings = Resources.FindObjectsOfTypeAll<SettingsStyleCollection>()
            .Single(s => s.gameObject.scene.path == "Assets/Scenes/MainScene.unity");
        if (EditorApplication.isPlaying || settings.gameObject.scene.isDirty)
            throw new System.InvalidOperationException("Save MainScene and stop Play Mode before baking MSAA.");

        SettingsPagePrefabBaker.EditPages(settings, page => page.category == 2, AddSetting);
        if (!settings.UsesPagePrefabs) {
            int variant = settings.bakedPages.Single(p => p.stage == settings.stage).variant;
            settings.controls = settings.bakedPages.Where(p => p.variant == variant)
                .OrderBy(p => p.category).SelectMany(p => p.controls).ToArray();
        }

        EditorUtility.SetDirty(settings);
        EditorSceneManager.MarkSceneDirty(settings.gameObject.scene);
        EditorSceneManager.SaveScene(settings.gameObject.scene);
        AssetDatabase.SaveAssets();
    }

    private static void AddSetting(SettingsBakedPage page) {
        var existing = page.controls.SingleOrDefault(f => f.key == "MsaaSampleCount");
        if (existing != null) {
            existing.root.anchoredPosition = new Vector2(0f, existing.root.anchoredPosition.y);
            ConfigureDropdown(existing);
            var existingHint = existing.root.GetComponent<SettingsHintTarget>();
            if (existingHint) existingHint.description = "MSAA: 1x / 2x / 4x";
            EditorUtility.SetDirty(page);
            return;
        }

        var source = page.controls.Single(f => f.key == "FrameRateLimit");
        var root = Object.Instantiate(source.root, source.root.parent);
        root.name = "MsaaSampleCount";
        Undo.RegisterCreatedObjectUndo(root.gameObject, "Add MSAA setting");

        var field = new SettingsStyleField();
        Component Remap(Component component) {
            if (!component) return null;
            string path = AnimationUtility.CalculateTransformPath(component.transform, source.root);
            var target = string.IsNullOrEmpty(path) ? root : root.Find(path);
            return target.GetComponent(component.GetType());
        }
        foreach (var member in typeof(SettingsStyleField).GetFields(BindingFlags.Public | BindingFlags.Instance)) {
            object value = member.GetValue(source);
            if (value is Component component) value = Remap(component);
            else if (value is Button[] buttons) value = buttons.Select(b => (Button)Remap(b)).ToArray();
            member.SetValue(field, value);
        }

        field.key = root.name;
        field.UseSegments = false;
        ConfigureDropdown(field);
        float rowStep = source.root.rect.height + 4f;
        root.anchoredPosition = new Vector2(0f, source.root.anchoredPosition.y - rowStep);
        page.controls = page.controls.Concat(new[] { field }).ToArray();

        var section = page.sections[source.section];
        section.root.sizeDelta += Vector2.up * rowStep;
        section.surface.rectTransform.sizeDelta += Vector2.up * rowStep;
        page.content.sizeDelta += Vector2.up * rowStep;

        var presentation = page.GetComponent<SettingsPagePresentation>();
        var hint = root.GetComponent<SettingsHintTarget>() ?? root.gameObject.AddComponent<SettingsHintTarget>();
        hint.key = field.key;
        hint.description = "MSAA: 1x / 2x / 4x";
        hint.owner = presentation;
        if (presentation) {
            presentation.targets = presentation.targets.Concat(new[] { hint }).ToArray();
            EditorUtility.SetDirty(presentation);
        }
        EditorUtility.SetDirty(page);
    }

    private static void ConfigureDropdown(SettingsStyleField field) {
        field.label.text = "MSAA";
        field.label.GetComponent<LocalizedPlatformLabel>()?.SetSource("MSAA");
        field.dropdown.ClearOptions();
        field.dropdown.AddOptions(new System.Collections.Generic.List<string> { "1x", "2x", "4x" });
        field.dropdown.SetValueWithoutNotify(1);
        field.dropdown.RefreshShownValue();
    }
}
#endif
