using System;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;

/// <summary>A small static atlas for table text; the rest of the game's fonts stay unchanged.</summary>
public static class RuleSupportTableFont {
    private const string SourcePath = "Assets/Resources/font/Chinese/AlibabaPuHuiTi/AlibabaPuHuiTi-3-55-Regular.otf";
    private const string AssetPath = "Assets/Resources/font/Chinese/AlibabaPuHuiTi/RuleSupportTable SDF.asset";

    public static TMP_FontAsset Prepare(RuleSupportTableBaker.TableData data, bool allowWrite) {
        string characters = "麻将规则支持表关闭" + string.Concat(data.Headers) + string.Concat(data.Rows.SelectMany(row => row));
        uint[] codepoints = Enumerable.Range(0, characters.Length).Where(i => !char.IsLowSurrogate(characters[i]))
            .Select(i => (uint)char.ConvertToUtf32(characters, i)).Where(value => value >= 32).Distinct().ToArray();
        var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(AssetPath);
        if (font && codepoints.All(value => font.HasCharacter((int)value))) return font;
        if (!allowWrite) throw new BuildFailedException("支持表字体尚未同步，请重新烘焙主场景 UI。");
        if (!font) {
            var source = AssetDatabase.LoadAssetAtPath<Font>(SourcePath);
            if (!source) throw new InvalidOperationException("缺少麻将规则支持表字体源文件。");
            font = TMP_FontAsset.CreateFontAsset(source, 64, 6, GlyphRenderMode.SDFAA, 2048, 2048, AtlasPopulationMode.Dynamic, true);
            font.name = "RuleSupportTable SDF";
            AssetDatabase.CreateAsset(font, AssetPath);
        }
        font.atlasPopulationMode = AtlasPopulationMode.Dynamic;
        font.TryAddCharacters(codepoints, out uint[] missing, true);
        font.atlasPopulationMode = AtlasPopulationMode.Static;
        if (missing != null && missing.Length > 0) throw new InvalidOperationException("支持表字体缺少字符：" + string.Join(",", missing));
        font.material.name = "RuleSupportTable SDF Material";
        if (!AssetDatabase.Contains(font.material)) AssetDatabase.AddObjectToAsset(font.material, font);
        for (int i = 0; i < font.atlasTextures.Length; i++) {
            var atlas = font.atlasTextures[i];
            atlas.name = "RuleSupportTable SDF Atlas " + i;
            atlas.filterMode = FilterMode.Bilinear;
            if (!AssetDatabase.Contains(atlas)) AssetDatabase.AddObjectToAsset(atlas, font);
            EditorUtility.SetDirty(atlas);
        }
        EditorUtility.SetDirty(font.material);
        EditorUtility.SetDirty(font);
        AssetDatabase.SaveAssets();
        return font;
    }
}

public sealed class RuleSupportTableFontBuild : IPreprocessBuildWithReport {
    public int callbackOrder => -10;
    public void OnPreprocessBuild(BuildReport report) => RuleSupportTableFont.Prepare(RuleSupportTableBaker.ReadWorkbook(), true);
}
