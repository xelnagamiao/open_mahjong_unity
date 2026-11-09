using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;

public sealed partial class PlatformTypography
{
    [Serializable]
    public sealed class MixedFontEntry
    {
        public TMP_FontAsset latin, chinese, font;
    }
    public List<MixedFontEntry> mixedFonts = new();
    static readonly Dictionary<(TMP_FontAsset latin, TMP_FontAsset chinese), TMP_FontAsset> MixedFontCache = new();
    static readonly List<(TMP_FontAsset font, bool sharedAtlas)> RuntimeMixedFonts = new();

    static bool HasLatinContent(string text, bool richText)
    {
        bool inTag = false;
        foreach (char c in text ?? "")
        {
            if (richText && c == '<') { inTag = true; continue; }
            if (inTag) { if (c == '>') inTag = false; continue; }
            if (c >= 'A' && c <= 'Z' || c >= 'a' && c <= 'z' || c >= '0' && c <= '9' || c >= '\u00C0' && c <= '\u024F' || c >= '\u0400' && c <= '\u052F') return true;
        }
        return false;
    }

    static bool IsLatinGlyph(uint value) => value >= 32 && value <= 0x024F || value >= 0x0400 && value <= 0x052F || value >= 0x1E00 && value <= 0x1EFF
        || value >= 0x2000 && value <= 0x22FF || value >= 0x25A0 && value <= 0x25FF || value >= 0xFF01 && value <= 0xFF5E;

#if UNITY_EDITOR
    public static void RefreshMixedFontVariants()
    {
        MixedFontCache.Clear();
        if (!Profile) return;
        foreach (var entry in Profile.mixedFonts.ToArray())
            if (entry.latin && entry.chinese) ResolveMixedFont(entry.latin, entry.chinese);
    }
#endif

    public static void PrepareMixedFontVariants(TMP_FontAsset original)
    {
        if (!original || !Profile) return;
        var chinese = ResolveChineseFont(original);
        ResolveMixedFont(ResolveLatin(original), chinese);
    }

    static TMP_FontAsset ResolveMixedFont(TMP_FontAsset latin, TMP_FontAsset chinese)
    {
        if (!latin || !chinese || latin == chinese) return latin ? latin : chinese;
        var key = (latin, chinese);
        if (MixedFontCache.TryGetValue(key, out var cached) && MixedFontReady(cached, latin, chinese)) return cached;
        var registered = Profile ? Profile.mixedFonts.Find(entry => entry.latin == latin && entry.chinese == chinese && entry.font) : null;
        if (registered != null && MixedFontReady(registered.font, latin, chinese)) return MixedFontCache[key] = registered.font;
#if UNITY_EDITOR
        if (!Application.isPlaying)
        {
            if (!UnityEditor.AssetDatabase.TryGetGUIDAndLocalFileIdentifier(latin, out string latinGuid, out long latinId)
                || !UnityEditor.AssetDatabase.TryGetGUIDAndLocalFileIdentifier(chinese, out string chineseGuid, out long chineseId)) return chinese;
            const string folder = "Assets/Resources/Localization/MixedFonts";
            if (!UnityEditor.AssetDatabase.IsValidFolder(folder)) UnityEditor.AssetDatabase.CreateFolder("Assets/Resources/Localization", "MixedFonts");
            string name = latinGuid + "_" + latinId + "_" + chineseGuid + "_" + chineseId;
            string path = folder + "/" + name + ".asset";
            var asset = UnityEditor.AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(path);
            if (!MixedFontReady(asset, latin, chinese))
            {
                var replacement = CreateMixedFont(latin, chinese, name, HideFlags.None);
                if (asset)
                {
                    UnityEditor.EditorUtility.CopySerialized(replacement, asset);
                    // TMP destroys its material and atlases with the font; these now belong to the asset.
                    replacement.atlasTextures = Array.Empty<Texture2D>(); replacement.material = null;
                    DestroyImmediate(replacement);
                }
                else
                {
                    asset = replacement;
                    UnityEditor.AssetDatabase.CreateAsset(asset, path);
                }
                foreach (var texture in asset.atlasTextures)
                    if (texture && !UnityEditor.AssetDatabase.Contains(texture))
                    {
                        texture.hideFlags = HideFlags.None;
                        UnityEditor.AssetDatabase.AddObjectToAsset(texture, asset);
                    }
                if (!UnityEditor.AssetDatabase.Contains(asset.material))
                {
                    asset.material.hideFlags = HideFlags.None;
                    UnityEditor.AssetDatabase.AddObjectToAsset(asset.material, asset);
                }
                var settings = new UnityEditor.SerializedObject(asset);
                settings.FindProperty("m_ClearDynamicDataOnBuild").boolValue = false;
                settings.ApplyModifiedPropertiesWithoutUndo();
                asset.ReadFontAssetDefinition();
                UnityEditor.EditorUtility.SetDirty(asset);
            }
            if (Profile)
            {
                Profile.mixedFonts.RemoveAll(entry => entry.latin == latin && entry.chinese == chinese);
                Profile.mixedFonts.Add(new MixedFontEntry { latin = latin, chinese = chinese, font = asset });
                UnityEditor.EditorUtility.SetDirty(Profile);
            }
            return MixedFontCache[key] = asset;
        }
#endif
        var runtime = CreateMixedFont(latin, chinese, latin.name + " Latin / " + chinese.name + " CJK", HideFlags.HideAndDontSave);
        RuntimeMixedFonts.Add((runtime, latin.atlasPopulationMode == AtlasPopulationMode.Static));
        return MixedFontCache[key] = runtime;
    }

    static bool MixedFontReady(TMP_FontAsset font, TMP_FontAsset latin, TMP_FontAsset chinese)
    {
        if (!font || font.atlasPopulationMode != AtlasPopulationMode.Static || !font.material
            || font.atlasTextures == null || font.atlasTextures.Length == 0 || !font.atlasTextures[0]) return false;
        if (font.fallbackFontAssetTable == null || font.fallbackFontAssetTable.Count != 1 || font.fallbackFontAssetTable[0] != chinese) return false;
        var present = new HashSet<uint>(font.characterTable.Select(c => c.unicode));
        if (latin.characterTable.Any(c => IsLatinGlyph(c.unicode) && !present.Contains(c.unicode))) return false;
        if (latin.atlasPopulationMode != AtlasPopulationMode.Static
            && font.atlasTextures.Any(texture => !texture || latin.atlasTextures.Contains(texture))) return false;
#if UNITY_EDITOR
        if (!Application.isPlaying && latin.atlasPopulationMode == AtlasPopulationMode.Static)
        {
            var glyphs = latin.characterTable.ToDictionary(c=>c.unicode,c=>c.glyph);
            if (!font.atlasTextures.SequenceEqual(latin.atlasTextures) || font.characterTable.Any(c=>
                glyphs.TryGetValue(c.unicode,out var glyph) && (c.glyph == null || glyph == null || !c.glyph.glyphRect.Equals(glyph.glyphRect) || !c.glyph.metrics.Equals(glyph.metrics)))) return false;
        }
        if (!Application.isPlaying && (!UnityEditor.AssetDatabase.Contains(font)
            || !UnityEditor.AssetDatabase.Contains(font.material))) return false;
#endif
        return true;
    }

    static TMP_FontAsset CreateMixedFont(TMP_FontAsset latin, TMP_FontAsset chinese, string name, HideFlags flags)
    {
        TMP_FontAsset font;
        if (latin.atlasPopulationMode == AtlasPopulationMode.Static)
        {
            font = Instantiate(latin);
            font.material = new Material(latin.material);
        }
        else
        {
            // A dynamic source can clear its atlas during builds; bake Latin into an independent static atlas.
            int pointSize = Mathf.Max(1, (int)latin.faceInfo.pointSize);
            font = latin.sourceFontFile
                ? TMP_FontAsset.CreateFontAsset(latin.sourceFontFile, pointSize, latin.atlasPadding,
                    latin.atlasRenderMode, 1024, 1024, AtlasPopulationMode.Dynamic, true)
                : TMP_FontAsset.CreateFontAsset(latin.faceInfo.familyName, latin.faceInfo.styleName, pointSize);
            if (!font) throw new InvalidOperationException("Cannot bake Latin glyphs for " + latin.name);
            var characters = Enumerable.Range(32, 95).Select(value => (uint)value)
                .Concat(latin.characterTable.Where(character => IsLatinGlyph(character.unicode)).Select(character => character.unicode))
                .Distinct().ToArray();
            font.TryAddCharacters(characters, out _, true);
            if (!font.HasCharacters("ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789"))
                throw new InvalidOperationException("Latin glyph coverage incomplete: " + latin.name);
            font.faceInfo = latin.faceInfo;
            font.normalStyle = latin.normalStyle; font.normalSpacingOffset = latin.normalSpacingOffset;
            font.boldStyle = latin.boldStyle; font.boldSpacing = latin.boldSpacing;
            font.italicStyle = latin.italicStyle; font.tabSize = latin.tabSize;
            var atlasMaterial = font.material;
            atlasMaterial.SetFloat(ShaderUtilities.ID_WeightNormal, font.normalStyle);
            atlasMaterial.SetFloat(ShaderUtilities.ID_WeightBold, font.boldStyle);
            font.material = new Material(latin.material);
            CopyMaterialStyle(font.material, latin.material, atlasMaterial);
            DestroyImmediate(atlasMaterial);
            foreach (var texture in font.atlasTextures) if (texture) texture.hideFlags = flags;
        }
        font.name = name; font.hideFlags = flags;
        font.atlasPopulationMode = AtlasPopulationMode.Static;
        font.material.name = name + " Material"; font.material.hideFlags = flags;
        font.characterTable.RemoveAll(character => !IsLatinGlyph(character.unicode));
        var glyphs = new HashSet<uint>(font.characterTable.Select(character => character.glyphIndex));
        font.glyphTable.RemoveAll(glyph => !glyphs.Contains(glyph.index));
        Array.Clear(font.fontWeightTable, 0, font.fontWeightTable.Length);
        font.fallbackFontAssetTable = new List<TMP_FontAsset> { chinese };
        font.ReadFontAssetDefinition();
        return font;
    }

    static void ClearRuntimeMixedFonts()
    {
        foreach (var entry in RuntimeMixedFonts)
            if (entry.font)
            {
                if (entry.sharedAtlas) entry.font.atlasTextures = Array.Empty<Texture2D>();
                Destroy(entry.font);
            }
        RuntimeMixedFonts.Clear(); MixedFontCache.Clear();
    }
}
