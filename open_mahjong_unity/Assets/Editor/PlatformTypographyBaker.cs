#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TextCore.LowLevel;

public static class PlatformTypographyBaker
{
    const string Folder = "Assets/Resources/Localization/";
    static readonly string Latin = new string(Enumerable.Range(32, 95).Select(i => (char)i).ToArray());
    const string Punctuation = " °±×÷‐‑–—‘’“”…←↑→↓≤≥";
    public static bool IsTextFont(TMP_FontAsset font) => font &&
        !font.name.Contains("Icons") && !font.faceInfo.familyName.ToLowerInvariant().Contains("awesome");

    public static void Bake(Scene scene)
    {
        RepairSceneMaterials(scene);
        EnsureProfile();
        var roots = scene.GetRootGameObjects();
        var fonts = new HashSet<TMP_FontAsset>();
        foreach (var root in roots) Bind(root, fonts);
        foreach (var font in fonts) { RepairLatin(font); PlatformTypography.PrepareMixedFontVariants(font); }
        BakeEventGlyphs(scene);
        FontFallbackRepairBaker.RepairFontAssets();
        AssetDatabase.SaveAssets();
    }

    public static int BakeEventGlyphs(Scene scene)
    {
        var roots=scene.GetRootGameObjects();
        var pages=roots.SelectMany(r=>r.GetComponentsInChildren<EventLobbyPanel>(true)).Select(p=>p.gameObject)
            .Concat(roots.SelectMany(r=>r.GetComponentsInChildren<EventDetailPanel>(true)).Select(p=>p.gameObject))
            .Concat(roots.SelectMany(r=>r.GetComponentsInChildren<CreatePanel>(true)).Select(p=>p.gameObject));
        var labels=pages.SelectMany(p=>p.GetComponentsInChildren<TMP_Text>(true)).Distinct();
        var characters=new HashSet<char>();
        foreach(var label in labels)
        {
            var binding=label.GetComponent<LocalizedPlatformLabel>();
            string source=binding?binding.key:label.text;
            foreach(AppLanguage language in Enum.GetValues(typeof(AppLanguage)))
                foreach(char c in PlatformText.Get(source,language))if(!char.IsControl(c))characters.Add(c);
        }
        var fallback=AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(Folder+"EnglishSourceHanRegularFallback.asset");
        if(!fallback)return 0;
        int before=fallback.characterTable.Count;
        fallback.TryAddCharacters(new string(characters.ToArray()),out _);
        var data=new SerializedObject(fallback);
        data.FindProperty("m_ClearDynamicDataOnBuild").boolValue=true;
        data.ApplyModifiedPropertiesWithoutUndo();
        SaveTextures(fallback);
        return fallback.characterTable.Count-before;
    }

    public static void RepairSceneMaterials(Scene scene)
    {
        foreach (var binding in scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<PlatformFontBinding>(true)))
        {
            var text = binding.GetComponent<TMP_Text>();
            if (!text || !binding.authoredFont) continue;
            RepairAuthoredMaterial(binding, text);
            binding.Apply();
            EditorUtility.SetDirty(binding); EditorUtility.SetDirty(text);
        }
    }

    static void RepairAuthoredMaterial(PlatformFontBinding binding, TMP_Text text)
    {
        var material = binding.authoredMaterial;
        if (material && AssetDatabase.Contains(material)) return;
        if (!material) material = text.fontSharedMaterial ? text.fontSharedMaterial : binding.authoredFont.material;
        binding.authoredMaterial = PlatformTypography.ResolveMaterial(material, binding.authoredFont);
    }

    public static void BakePrefabs(Scene scene)
    {
        var paths = AssetDatabase.GetDependencies(scene.path, true).Where(p => p.EndsWith(".prefab"))
            .Concat(AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/Resources/UI" }).Select(AssetDatabase.GUIDToAssetPath)).Distinct();
        var fonts = new HashSet<TMP_FontAsset>();
        foreach (var path in paths)
        {
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (!asset || !asset.GetComponentsInChildren<TMP_Text>(true).Any(t => IsTextFont(t.font))) continue;
            var root = PrefabUtility.LoadPrefabContents(path);
            try { Bind(root, fonts); PrefabUtility.SaveAsPrefabAsset(root, path); }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }
        foreach (var font in fonts) { RepairLatin(font); PlatformTypography.PrepareMixedFontVariants(font); }
    }

    static void Bind(GameObject root, HashSet<TMP_FontAsset> fonts)
    {
        foreach (var text in root.GetComponentsInChildren<TMP_Text>(true))
        {
            if (!IsTextFont(text.font)) continue;
            var binding = text.GetComponent<PlatformFontBinding>() ?? text.gameObject.AddComponent<PlatformFontBinding>();
            if (!binding.authoredFont) { binding.authoredFont = text.font; binding.authoredMaterial = text.fontSharedMaterial; }
            RepairAuthoredMaterial(binding, text);
            if (!binding.styleCaptured) { binding.authoredStyle = text.fontStyle; binding.authoredWeight = text.fontWeight; binding.styleCaptured = true; }
            if (binding.authoredFont == TableHudTypography.Body || binding.authoredFont == TableHudTypography.Emphasis)
                binding.preserveAuthoredTypeface = true;
            fonts.Add(binding.authoredFont);
            binding.Apply();
            EditorUtility.SetDirty(binding);
            EditorUtility.SetDirty(text);
        }
    }

    public static string RepairLatin(TMP_FontAsset font)
    {
        if (!IsTextFont(font) || font.atlasPopulationMode != AtlasPopulationMode.Static || font.HasCharacters(Latin)) return "";
        var data = new SerializedObject(font);
        string guid = data.FindProperty("m_SourceFontFileGUID").stringValue;
        var source = AssetDatabase.LoadAssetAtPath<Font>(AssetDatabase.GUIDToAssetPath(guid));
        if (!source && font.faceInfo.familyName == "Source Han Sans SC")
        {
            string weight = font.faceInfo.styleName;
            string path = PlatformFontAutomation.FullSource(weight);
            source = AssetDatabase.LoadAssetAtPath<Font>(path);
            if (source) data.FindProperty("m_SourceFontFileGUID").stringValue = AssetDatabase.AssetPathToGUID(path);
        }
        if (!source) throw new InvalidOperationException("Font source is missing: " + AssetDatabase.GetAssetPath(font));
        data.FindProperty("m_SourceFontFile").objectReferenceValue = source;
        data.ApplyModifiedPropertiesWithoutUndo();
        font.atlasPopulationMode = AtlasPopulationMode.Dynamic;
        font.isMultiAtlasTexturesEnabled = true;
        try
        {
            font.TryAddCharacters(Latin + Punctuation, out string missing);
            if (!font.HasCharacters(Latin)) throw new InvalidOperationException("Latin coverage incomplete: " + font.name + " / " + missing);
            SaveTextures(font);
        }
        finally { font.atlasPopulationMode = AtlasPopulationMode.Static; EditorUtility.SetDirty(font); }
        return AssetDatabase.GetAssetPath(font);
    }

    static void SaveTextures(TMP_FontAsset font)
    {
        foreach (var texture in font.atlasTextures.Where(t => t))
        {
            if (!AssetDatabase.Contains(texture)) AssetDatabase.AddObjectToAsset(texture, font);
            EditorUtility.SetDirty(texture);
        }
        EditorUtility.SetDirty(font.material);
        EditorUtility.SetDirty(font);
    }

    static TMP_FontAsset Font(string name, string path, bool dynamic)
    {
        var asset = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(Folder + name + ".asset");
        if (asset) { if (!dynamic) RepairLatin(asset); return asset; }
        var source = AssetDatabase.LoadAssetAtPath<Font>(path);
        if (!source) throw new InvalidOperationException("Font source missing: " + path);
        asset = TMP_FontAsset.CreateFontAsset(source, dynamic ? 48 : 80, dynamic ? 5 : 8,
            GlyphRenderMode.SDFAA, 1024, 1024, AtlasPopulationMode.Dynamic, true);
        asset.name = name;
        AssetDatabase.CreateAsset(asset, Folder + name + ".asset");
        asset.material.name = name + " Material";
        AssetDatabase.AddObjectToAsset(asset.material, asset);
        if (!dynamic)
        {
            asset.TryAddCharacters(Latin + Punctuation, out string missing);
            if (!asset.HasCharacters(Latin)) throw new InvalidOperationException(name + " missing Latin: " + missing);
            asset.atlasPopulationMode = AtlasPopulationMode.Static;
        }
        var data = new SerializedObject(asset);
        data.FindProperty("m_ClearDynamicDataOnBuild").boolValue = dynamic;
        data.ApplyModifiedPropertiesWithoutUndo();
        SaveTextures(asset);
        return asset;
    }

    public static void EnsureProfile()
    {
        var asset = AssetDatabase.LoadAssetAtPath<PlatformTypography>(Folder + "PlatformTypography.asset");
        if (!asset) { asset = ScriptableObject.CreateInstance<PlatformTypography>(); AssetDatabase.CreateAsset(asset, Folder + "PlatformTypography.asset"); }
        string[] names = { "Regular", "Medium", "Bold" };
        var fonts = new TMP_FontAsset[3];
        for (int i = 0; i < names.Length; i++)
        {
            fonts[i] = Font("EnglishSourceHan" + names[i], PlatformFontAutomation.FullSource(names[i]), false);
            var fallback = Font("EnglishSourceHan" + names[i] + "Fallback", PlatformFontAutomation.RuntimeSource(names[i]), true);
            var fallbacks = PlatformFontAutomationAssets.Fallbacks(fonts[i]);
            if (fonts[i].fallbackFontAssetTable == null || !fonts[i].fallbackFontAssetTable.SequenceEqual(fallbacks))
            {
                fonts[i].fallbackFontAssetTable = fallbacks;
                EditorUtility.SetDirty(fonts[i]);
            }
        }
        var chinese = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/font/思源黑体/SourceHanSansSC-Regular SDF.asset");
        if (asset.regular != fonts[0] || asset.medium != fonts[1] || asset.bold != fonts[2] || asset.chineseRegular != chinese)
        {
            asset.regular = fonts[0]; asset.medium = fonts[1]; asset.bold = fonts[2]; asset.chineseRegular = chinese;
            EditorUtility.SetDirty(asset);
        }
    }
}
#endif
