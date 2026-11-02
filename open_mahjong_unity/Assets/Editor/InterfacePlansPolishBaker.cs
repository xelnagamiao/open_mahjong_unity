#if UNITY_EDITOR
using System;
using System.Linq;
using Salasasa.Home;
using TMPro;
using UnityEditor;
using UnityEditor.Events;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class InterfacePlansPolishBaker
{
    public static void Bake(Scene scene)
    {
        var plans = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<InterfacePlansController>(true)).Single();
        var global = plans.pages[0].transform;
        var homePreset = plans.presetDefaults.Single(p => p.option == DesignOption.Home);
        homePreset.value = 0;
        var homeRow = plans.choices.Single(c => c.option == DesignOption.Home);
        foreach (var button in homeRow.buttons.Skip(HomeHeaderAppearance.VariantCount)) if (button) UnityEngine.Object.DestroyImmediate(button.gameObject);
        homeRow.buttons = homeRow.buttons.Take(HomeHeaderAppearance.VariantCount).ToArray();
        homeRow.values = HomeHeaderAppearance.DisplayVariantOrder.ToArray();
        for (int i = 0; i < homeRow.buttons.Length; i++)
        {
            var button = homeRow.buttons[i];
            button.name = "Choice" + i;
            button.onClick = new UnityEngine.UI.Button.ButtonClickedEvent();
            UnityEventTools.AddIntPersistentListener(button.onClick, plans.SelectHome, i);
            Rect((RectTransform)button.transform, 122 + i * 108, 5, 102, 40);
            SetText(button.GetComponentInChildren<TMP_Text>(true), HomeHeaderAppearance.VariantNames[i]);
        }
        PlaceRow(global, "Navigation", 0, 66);
        PlaceRow(global, "Home", 66, 72);
        PlaceRow(global, "EntrancePalette", 138, 160);
        plans.pageHeights[0] = 420;
        var pageOptions=plans.pages[1].transform;
        PlaceRow(pageOptions,"Settings",0,60);
        PlaceRow(pageOptions,"EventDesign",60,60);
        plans.pageHeights[1]=252;
        for (int i = 0; i < plans.home.plates.Length; i++)
        {
            if (!plans.home.plates[i]) continue;
            plans.home.plates[i].name = "HomeSelection" + i;
            if (i < plans.home.surfaces.Length && plans.home.surfaces[i])
                plans.home.surfaces[i].Paint(Color.clear, Color.clear, 0, 0, 0);
        }
        plans.home.target.presentation.Entry.useHomePalette = true;
        EditorUtility.SetDirty(plans.home.target.presentation.profile);
        BakeHomeIconBounds(plans.home);
        plans.home.BakeDefault();
        plans.homePalette.BakeDefaults();
        EditorUtility.SetDirty(plans);
        EditorUtility.SetDirty(plans.home);
        HomeHeaderControlsBaker.Bake(scene);
        LeaderboardPresentationBaker.BakeOptions(plans);
        EntranceAppearanceBaker.Bake(scene);
    }
    static void BakeHomeIconBounds(HomeHeaderAppearance appearance)
    {
        if (!appearance.icon || !appearance.icon.sprite) return;
        var sprite = appearance.icon.sprite;
        string path = AssetDatabase.GetAssetPath(sprite.texture);
        if (!System.IO.File.Exists(path)) return;
        var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        try
        {
            if (!ImageConversion.LoadImage(texture, System.IO.File.ReadAllBytes(path))) return;
            var pixels = texture.GetPixels32();
            var area = sprite.rect;
            float sx = texture.width / (float)sprite.texture.width, sy = texture.height / (float)sprite.texture.height;
            int left = Mathf.RoundToInt(area.xMin * sx), right = Mathf.RoundToInt(area.xMax * sx);
            int bottom = Mathf.RoundToInt(area.yMin * sy), top = Mathf.RoundToInt(area.yMax * sy);
            int minX = right, maxX = left, minY = top, maxY = bottom;
            for (int y = bottom; y < top; y++)
                for (int x = left; x < right; x++)
                    if (pixels[y * texture.width + x].a > 2)
                    { minX = Mathf.Min(minX, x); maxX = Mathf.Max(maxX, x + 1); minY = Mathf.Min(minY, y); maxY = Mathf.Max(maxY, y + 1); }
            if (maxX > minX && maxY > minY)
                appearance.iconContentBounds = new Rect((minX - left) / (float)(right - left), (minY - bottom) / (float)(top - bottom),
                    (maxX - minX) / (float)(right - left), (maxY - minY) / (float)(top - bottom));
            appearance.blockPadding = new Vector2(8, 7);
        }
        finally { UnityEngine.Object.DestroyImmediate(texture); }
    }
    static void PlaceRow(Transform global, string name, float top, float height)
    {
        var row = global.Find(name) as RectTransform;
        if (!row) throw new InvalidOperationException("Missing interface row: " + name);
        Rect(row, 24, top, 552, height);
    }
    static void Rect(RectTransform rect, float left, float top, float width, float height)
    {
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0, 1);
        rect.anchoredPosition = new Vector2(left, -top); rect.sizeDelta = new Vector2(width, height);
    }
    static void SetText(TMP_Text text, string value)
    {
        text.text = value; text.fontStyle = FontStyles.Normal;
        var locale = text.GetComponent<LocalizedPlatformLabel>();
        if (locale) locale.key = value;
    }
}
#endif
