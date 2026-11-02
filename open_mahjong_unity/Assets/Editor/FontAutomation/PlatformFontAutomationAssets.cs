using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using TMPro;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;

public static class PlatformFontAutomationAssets
{
    public const string ExtensionPath = "Assets/Resources/Localization/SourceHanRegularExtensions.asset";
    const string Folder = "Assets/Resources/Localization/";

    public static string Hash(string path)
    {
        using var hash = SHA256.Create();
        using var file = File.OpenRead(path);
        return BitConverter.ToString(hash.ComputeHash(file)).Replace("-", "").ToLowerInvariant();
    }

    public static bool IsManaged(TMP_FontAsset font) => font &&
        (font.faceInfo.familyName == "Source Han Sans SC" || font.faceInfo.familyName == "Salasasa UI Sans");

    static IEnumerable<TMP_FontAsset> Fonts() => AssetDatabase.FindAssets("t:TMP_FontAsset", new[] { "Assets" })
        .Select(AssetDatabase.GUIDToAssetPath).Where(path => !path.Contains("/Localization/MixedFonts/"))
        .Select(AssetDatabase.LoadAssetAtPath<TMP_FontAsset>).Where(IsManaged);

    public static bool SourcesMatch(PlatformFontManifest manifest)
    {
        foreach (var entry in manifest.fonts)
        {
            string path = PlatformFontAutomation.RuntimeSource(entry.weight);
            if (!File.Exists(path) || Hash(path) != entry.sha256) return false;
        }
        foreach (var entry in manifest.extraFonts ?? Array.Empty<PlatformExtraFont>())
        {
            var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(entry.path);
            if (!font || font.atlasPopulationMode != AtlasPopulationMode.Static || font.sourceFontFile) return false;
            var baked = new HashSet<uint>(font.characterTable.Select(c=>c.unicode));
            if (entry.characters.Any(c=>!baked.Contains(c))) return false;
        }
        foreach (var font in Fonts())
        {
            if (font.atlasPopulationMode == AtlasPopulationMode.Static)
            {
                if (font.sourceFontFile || font.fallbackFontAssetTable == null ||
                    !font.fallbackFontAssetTable.SequenceEqual(Fallbacks(font))) return false;
            }
            else if (AssetDatabase.GetAssetPath(font.sourceFontFile) != PlatformFontAutomation.RuntimeSource(font.faceInfo.styleName)) return false;
        }
        return true;
    }

    public static List<TMP_FontAsset> Fallbacks(TMP_FontAsset font)
    {
        string weight = font.faceInfo.styleName;
        var result = new List<TMP_FontAsset>();
        if (weight == "Regular" && AssetDatabase.GetAssetPath(font) != ExtensionPath)
        {
            var extension = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(ExtensionPath);
            if (extension) result.Add(extension);
        }
        var fallback = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(Folder + "EnglishSourceHan" + weight + "Fallback.asset");
        if (fallback && fallback != font) result.Add(fallback);
        return result;
    }

    public static void Apply(PlatformFontManifest manifest,PlatformFontManifest previous=null)
    {
        PlatformTypographyBaker.EnsureProfile();
        var changedSources=new HashSet<string>();
        if(!string.IsNullOrEmpty(previous?.regularSourceHash) && previous.regularSourceHash!=manifest.regularSourceHash)changedSources.Add("Regular");
        foreach(var entry in manifest.fonts)
        {
            var old=previous?.fonts?.FirstOrDefault(f=>f.weight==entry.weight);
            if(!string.IsNullOrEmpty(old?.sourceHash) && old.sourceHash!=entry.sourceHash)changedSources.Add(entry.weight);
        }
        foreach(var font in Fonts().Where(f=>f.atlasPopulationMode==AtlasPopulationMode.Static && changedSources.Contains(f.faceInfo.styleName)).ToArray())
        {
            var chars=font.characterTable.Select(c=>c.unicode).ToArray();
            font.ClearFontAssetData(true);
            Bake(font,chars,AssetDatabase.LoadAssetAtPath<Font>(PlatformFontAutomation.FullSource(font.faceInfo.styleName)));
        }
        var regularSource = AssetDatabase.LoadAssetAtPath<Font>(PlatformFontAutomation.FullSource("Regular"));
        var extension = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(ExtensionPath);
        if (!extension)
        {
            extension = TMP_FontAsset.CreateFontAsset(regularSource, 48, 5, GlyphRenderMode.SDFAA,
                1024, 1024, AtlasPopulationMode.Dynamic, true);
            extension.name = "SourceHanRegularExtensions";
            AssetDatabase.CreateAsset(extension, ExtensionPath);
            AssetDatabase.AddObjectToAsset(extension.material, extension);
            extension.atlasPopulationMode = AtlasPopulationMode.Static;
        }
        var canonical = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/font/思源黑体/SourceHanSansSC-Regular SDF.asset");
        var existing = new HashSet<uint>(canonical.characterTable.Select(c => c.unicode));
        var extensions = manifest.regularCharacters.Where(c => !existing.Contains(c)).ToArray();
        if (extension.characterTable.Any(c => !extensions.Contains(c.unicode))) extension.ClearFontAssetData(true);
        Bake(extension, extensions, regularSource);
        foreach (string weight in new[] { "Regular", "Medium", "Bold" })
        {
            var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(Folder + "EnglishSourceHan" + weight + ".asset");
            uint[] chars = weight == "Regular" ? manifest.fonts[0].characters : manifest.fonts.First(f => f.weight == weight).characters;
            Bake(font, chars, AssetDatabase.LoadAssetAtPath<Font>(PlatformFontAutomation.FullSource(weight)));
        }
        var regularFallback = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(Folder + "EnglishSourceHanRegularFallback.asset");
        foreach (var font in Fonts().ToArray())
        {
            string weight = font.faceInfo.styleName;
            if (weight != "Regular" && weight != "Medium" && weight != "Bold") continue;
            if (font.atlasPopulationMode == AtlasPopulationMode.Static)
            {
                var fallbacks=Fallbacks(font);
                if (!font.sourceFontFile && font.fallbackFontAssetTable != null && font.fallbackFontAssetTable.SequenceEqual(fallbacks)) continue;
                SetSource(font,null,PlatformFontAutomation.FullSource(weight));
                font.fallbackFontAssetTable=fallbacks;
            }
            else
            {
                string path = PlatformFontAutomation.RuntimeSource(weight);
                var source = AssetDatabase.LoadAssetAtPath<Font>(path);
                if (!source) throw new BuildFailedException("Missing runtime font subset: " + path);
                bool changed=changedSources.Contains(weight) || weight!="Regular" && previous?.fonts?.FirstOrDefault(f=>f.weight==weight)?.sha256 != manifest.fonts.First(f=>f.weight==weight).sha256;
                var fallbacks=font == regularFallback ? new List<TMP_FontAsset>() : new List<TMP_FontAsset> { regularFallback };
                var data = new SerializedObject(font);
                bool reset=changed || font.sourceFontFile!=source;
                if(!reset && font.isMultiAtlasTexturesEnabled && data.FindProperty("m_ClearDynamicDataOnBuild").boolValue &&
                    font.fallbackFontAssetTable!=null && font.fallbackFontAssetTable.SequenceEqual(fallbacks))continue;
                // Subsetting can renumber glyphs: discard old atlas/lookup data before switching sources.
                if(reset)
                {
                    font.ClearFontAssetData(true);
                    SetSource(font,source,path);
                    if(FontEngine.LoadFontFace(source,(int)font.faceInfo.pointSize)!=FontEngineError.Success)
                        throw new BuildFailedException("Cannot load runtime font: "+path);
                    font.faceInfo=FontEngine.GetFaceInfo();
                }
                font.isMultiAtlasTexturesEnabled=true;
                font.fallbackFontAssetTable=fallbacks;
                data.Update();
                data.FindProperty("m_ClearDynamicDataOnBuild").boolValue = true;
                data.ApplyModifiedPropertiesWithoutUndo();
            }
            font.ReadFontAssetDefinition();
            EditorUtility.SetDirty(font);
        }
        foreach (var entry in manifest.extraFonts ?? Array.Empty<PlatformExtraFont>())
        {
            var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(entry.path);
            var source = AssetDatabase.LoadAssetAtPath<Font>(entry.source);
            var old=previous?.extraFonts?.FirstOrDefault(f=>f.path==entry.path);
            if(!string.IsNullOrEmpty(old?.sourceHash) && old.sourceHash!=entry.sourceHash)font.ClearFontAssetData(true);
            Bake(font, entry.characters, source);
            // Decorative faces and icons are fixed UI: preserve their authored
            // outlines/materials and bake the used glyphs without shipping a source.
            if(font.atlasPopulationMode!=AtlasPopulationMode.Static || font.sourceFontFile || font.fallbackFontAssetTable.Count>0)
            {
                font.atlasPopulationMode = AtlasPopulationMode.Static;
                SetSource(font, null, entry.source);
                font.fallbackFontAssetTable = new List<TMP_FontAsset>();
                EditorUtility.SetDirty(font);
            }
        }
        // Regenerate only mixed variants whose Latin character tables changed.
        PlatformTypography.RefreshMixedFontVariants();
    }

    static void SetSource(TMP_FontAsset font, Font source, string path)
    {
        var data = new SerializedObject(font);
        data.FindProperty("m_SourceFontFile").objectReferenceValue = source;
        data.FindProperty("m_SourceFontFileGUID").stringValue = AssetDatabase.AssetPathToGUID(path);
        var editor = data.FindProperty("m_SourceFontFile_EditorRef");
        if (editor != null) editor.objectReferenceValue = AssetDatabase.LoadAssetAtPath<Font>(path);
        data.ApplyModifiedPropertiesWithoutUndo();
    }

    static void Bake(TMP_FontAsset font, uint[] characters, Font source)
    {
        if (!font || !source) throw new BuildFailedException("Missing static font or source.");
        font.ReadFontAssetDefinition();
        var existing = new HashSet<uint>(font.characterTable.Select(c => c.unicode));
        var pending = characters.Where(c => !existing.Contains(c)).ToArray();
        if (pending.Length == 0) return;
        SetSource(font, source, AssetDatabase.GetAssetPath(source));
        font.atlasPopulationMode = AtlasPopulationMode.Dynamic;
        font.isMultiAtlasTexturesEnabled = true;
        try
        {
            font.TryAddCharacters(pending, out _, true);
            existing = new HashSet<uint>(font.characterTable.Select(c => c.unicode));
            var missing = characters.Where(c => !existing.Contains(c)).ToArray();
            if (missing.Length > 0) throw new BuildFailedException(font.name + " 字符烘焙失败：" +
                string.Join(",", missing.Select(c => "U+" + c.ToString("X4"))));
            foreach (var atlas in font.atlasTextures.Where(t => t))
            {
                if (!AssetDatabase.Contains(atlas)) AssetDatabase.AddObjectToAsset(atlas, font);
                EditorUtility.SetDirty(atlas);
            }
            EditorUtility.SetDirty(font.material);
        }
        finally
        {
            font.atlasPopulationMode = AtlasPopulationMode.Static;
            SetSource(font, null, AssetDatabase.GetAssetPath(source));
            EditorUtility.SetDirty(font);
        }
    }
}
