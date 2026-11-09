#if UNITY_EDITOR
using System.Linq;
using System.Reflection;
using Salasasa.Home;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Author the real settings and the shared bottom control root, without runtime layout patches.</summary>
public static class TileSurfaceBaker {
    [MenuItem("Tools/Mahjong/Bake tile surface controls")]
    public static void Bake() {
        var settings = Resources.FindObjectsOfTypeAll<SettingsStyleCollection>()
            .Single(s => s.gameObject.scene.path == "Assets/Scenes/MainScene.unity");
        if (EditorApplication.isPlaying || settings.gameObject.scene.isDirty)
            throw new System.InvalidOperationException("Save MainScene and stop Play Mode before baking.");
        foreach (var page in settings.bakedPages.Where(p => p.category == 2)) AddSetting(page);
        int variant = settings.bakedPages.Single(p => p.stage == settings.stage).variant;
        settings.controls = settings.bakedPages.Where(p => p.variant == variant)
            .OrderBy(p => p.category).SelectMany(p => p.controls).ToArray();
        EditorUtility.SetDirty(settings);

        var canvas = Resources.FindObjectsOfTypeAll<GameCanvas>().Single(c => c.gameObject.scene == settings.gameObject.scene);
        var controls = (RectTransform)canvas.transform.Find("GamePanel/GameSceneUI/SelfControl");
        var hand = (RectTransform)controls.Find("HandCardsContainer");
        float desiredY = 16f - hand.anchoredPosition.y;
        float delta = desiredY - controls.anchoredPosition.y;
        controls.anchoredPosition = new Vector2(controls.anchoredPosition.x, desiredY);
        // Hongque's persistent supplement button is a sibling of SelfControl.
        var extra = canvas.transform.Find("GamePanel/GameSceneUI/ExtraActionButton") as RectTransform;
        if (extra) extra.anchoredPosition += Vector2.up * delta;
        EditorSceneManager.MarkSceneDirty(settings.gameObject.scene);
        EditorSceneManager.SaveScene(settings.gameObject.scene);
        AssetDatabase.SaveAssets();
    }

    private static void AddSetting(SettingsBakedPage page) {
        var source = page.controls.Single(f => f.key == "TileCornerLabelsRow");
        var existing = page.controls.SingleOrDefault(f => f.key == "DoraHintsRow");
        if (existing != null) {
            existing.UseSegments = source.UseSegments;
            EditorUtility.SetDirty(page);
            return;
        }
        var root = Object.Instantiate(source.root, source.root.parent);
        root.name = "DoraHintsRow";
        Undo.RegisterCreatedObjectUndo(root.gameObject, "Add dora highlights setting");
        var field = new SettingsStyleField();
        Component Remap(Component component) {
            if (!component) return null;
            string path = AnimationUtility.CalculateTransformPath(component.transform, source.root);
            return (string.IsNullOrEmpty(path) ? root : root.Find(path)).GetComponent(component.GetType());
        }
        foreach (var member in typeof(SettingsStyleField).GetFields(BindingFlags.Public | BindingFlags.Instance)) {
            object value = member.GetValue(source);
            if (value is Component component) value = Remap(component);
            else if (value is Button[] buttons) value = buttons.Select(b => (Button)Remap(b)).ToArray();
            member.SetValue(field, value);
        }
        // UseSegments wraps a private serialized field and is not in the public-field copy.
        field.UseSegments = source.UseSegments;
        field.key = root.name;
        field.label.text = "宝牌提示";
        field.label.GetComponent<LocalizedPlatformLabel>()?.SetSource("宝牌提示");
        root.anchoredPosition = new Vector2(page.controls.Single(f => f.key == "ActionButtonColorRow").root.anchoredPosition.x,
            source.root.anchoredPosition.y);
        page.controls = page.controls.Concat(new[] { field }).ToArray();
        var presentation = page.GetComponent<SettingsPagePresentation>();
        var hint = root.GetComponent<SettingsHintTarget>();
        hint.key = field.key;
        hint.description = "日麻中以流动光泽标出宝牌和赤宝牌。";
        hint.owner = presentation;
        if (presentation) {
            presentation.targets = presentation.targets.Concat(new[] { hint }).ToArray();
            EditorUtility.SetDirty(presentation);
        }
        EditorUtility.SetDirty(page);
    }
}
#endif
