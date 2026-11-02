#if UNITY_EDITOR
using System;
using System.Linq;
using Salasasa.Home;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TextCore.LowLevel;

public static class PlayerDataIconBaker
{
    public const string FontPath = "Assets/Salasasa/Fonts/PlayerDataIcons.asset";

    public static TMP_FontAsset EnsureFont()
    {
        var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontPath);
        if (!font)
        {
            var source = AssetDatabase.LoadAssetAtPath<Font>("Assets/Salasasa/Fonts/fa-solid-900.ttf");
            if (!source) throw new InvalidOperationException("Font Awesome source is missing.");
            font = TMP_FontAsset.CreateFontAsset(source, 96, 10, GlyphRenderMode.SDF16, 512, 512, AtlasPopulationMode.Dynamic, false);
            font.name = "PlayerDataIcons";
            AssetDatabase.CreateAsset(font, FontPath);
            font.material.name = "PlayerDataIcons Material";
            AssetDatabase.AddObjectToAsset(font.material, font);
        }
        font.ReadFontAssetDefinition();
        string characters = string.Concat(PlayerDataIcon.Glyphs) + string.Concat(PlayerDataIcon.InventoryGlyphs);
        if (!font.HasCharacters(characters))
        {
            font.atlasPopulationMode = AtlasPopulationMode.Dynamic;
            try
            {
                font.TryAddCharacters(characters, out string missing);
                if (!font.HasCharacters(characters)) throw new InvalidOperationException("Missing player data icon glyphs: " + missing);
            }
            finally { font.atlasPopulationMode = AtlasPopulationMode.Static; }
        }
        font.atlasPopulationMode = AtlasPopulationMode.Static;
        foreach (var texture in font.atlasTextures.Where(t => t))
        {
            texture.filterMode = FilterMode.Bilinear;
            if (!AssetDatabase.Contains(texture)) AssetDatabase.AddObjectToAsset(texture, font);
            EditorUtility.SetDirty(texture);
        }
        var material = font.material;
        material.SetFloat("_FaceDilate", 0); material.SetFloat("_OutlineWidth", 0);
        material.SetFloat("_WeightNormal", 0); material.SetFloat("_WeightBold", 0);
        material.SetFloat("_Sharpness", 0);
        material.SetColor("_FaceColor", Color.white);
        material.EnableKeyword("UNDERLAY_ON");
        material.SetColor("_UnderlayColor", new Color(0, 0, 0, .35f));
        material.SetFloat("_UnderlayOffsetX", 0); material.SetFloat("_UnderlayOffsetY", -.6f);
        material.SetFloat("_UnderlayDilate", 0); material.SetFloat("_UnderlaySoftness", .2f);
        EditorUtility.SetDirty(material); EditorUtility.SetDirty(font);
        return font;
    }

    public static PlayerDataIcon Bind(LobbyAction action, TMP_FontAsset font)
    {
        var current = action.GetComponentInChildren<PlayerDataIcon>(true);
        var legacy = action.GetComponentsInChildren<UnityEngine.UI.Graphic>(true)
            .FirstOrDefault(g => g.GetType().Name == "PlayerDataGraphic");
        var oldBinding = action.GetComponentInChildren<LobbyIconBinding>(true);
        RectTransform rect;
        if (current) rect = (RectTransform)current.transform;
        else if (legacy)
        {
            rect = legacy.rectTransform;
            UnityEngine.Object.DestroyImmediate(legacy);
        }
        else if (oldBinding)
        {
            var original = oldBinding.primary.rectTransform;
            rect = new GameObject("PlayerDataIcon", typeof(RectTransform)).GetComponent<RectTransform>();
            rect.SetParent(original.parent, false);
            rect.anchorMin = original.anchorMin; rect.anchorMax = original.anchorMax;
            rect.pivot = original.pivot; rect.anchoredPosition = original.anchoredPosition;
            rect.sizeDelta = original.sizeDelta;
        }
        else throw new InvalidOperationException("MyDataButton has no authored icon geometry: " + action.name);

        if (oldBinding)
        {
            oldBinding.primary.gameObject.SetActive(false);
            if (oldBinding.accent) oldBinding.accent.gameObject.SetActive(false);
            if (oldBinding.shadow) oldBinding.shadow.gameObject.SetActive(false);
            UnityEngine.Object.DestroyImmediate(oldBinding);
        }
        rect.name = "PlayerDataIcon";
        if (rect.parent.name.StartsWith("DesignedIcon_")) rect.parent.name = "PlayerDataArtwork";
        var text = rect.GetComponent<TextMeshProUGUI>() ?? rect.gameObject.AddComponent<TextMeshProUGUI>();
        text.font = font; text.fontSharedMaterial = font.material; text.color = Color.white;
        var icon = current ? current : rect.gameObject.AddComponent<PlayerDataIcon>();
        icon.glyph = text; icon.visualSize = 46; icon.Apply(PlayerDataIcon.DefaultStyle);
        rect.gameObject.SetActive(true);
        EditorUtility.SetDirty(icon); EditorUtility.SetDirty(text); EditorUtility.SetDirty(rect);
        return icon;
    }

    public static void Validate(Scene scene)
    {
        var roots = scene.GetRootGameObjects();
        var actions = roots.SelectMany(r => r.GetComponentsInChildren<LobbyAction>(true))
            .Where(a => a.name == "MyDataButton").ToArray();
        if (actions.Length == 0) throw new InvalidOperationException("MyDataButton is missing.");
        foreach (var action in actions)
        {
            var icon = action.GetComponentInChildren<PlayerDataIcon>(true);
            if (!icon || !icon.glyph || icon.glyph.font.atlasPopulationMode != AtlasPopulationMode.Static
                || icon.glyph.font.atlasRenderMode != GlyphRenderMode.SDF16
                || !icon.glyph.font.HasCharacters(string.Concat(PlayerDataIcon.Glyphs)))
                throw new InvalidOperationException("MyDataButton icon is not completely baked.");
            if (action.GetComponentsInChildren<UnityEngine.UI.Graphic>(true).Any(g => g.GetType().Name == "PlayerDataGraphic"))
                throw new InvalidOperationException("Legacy player data geometry remains.");
        }
        var workbench = roots.SelectMany(r => r.GetComponentsInChildren<LobbyIconWorkbench>(true)).Single();
        if (workbench.playerDataIcons.Length != actions.Length || workbench.panel
            || workbench.choices.Length != 0 || workbench.playerDataIcons.Any(i => !i || i.Style != 0)
            || workbench.inventoryIcons.Length == 0 || workbench.inventoryIcons.Any(i => !i || i.Style != 1))
            throw new InvalidOperationException("The fixed personal data and inventory icons have not been baked.");
    }
}
#endif
