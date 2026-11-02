using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using TMPro;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TextCore.LowLevel;

public sealed class PlatformFontBuildValidation : IProcessSceneWithReport, IPostprocessBuildWithReport
{
    public int callbackOrder => 1000;
    static readonly Regex Tags = new Regex(@"</?(?:b|i|u|s|color|size|font|font-weight|sprite|link|align|alpha|cspace|mspace|voffset|mark|noparse|br|style|material)(?:\s[^<>]*|=[^<>]*)?/?>", RegexOptions.IgnoreCase);

    public static string[] ValidateScene(Scene scene)
    {
        return ValidateTexts(scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<TMP_Text>(true)));
    }

    public static string[] ValidateTexts(IEnumerable<TMP_Text> texts)
    {
        var failures = new List<string>();
        var cache = new Dictionary<(TMP_FontAsset,uint),bool>();
        foreach (var text in texts)
        {
            if (!text.font) { failures.Add(Path(text.transform) + ": missing font"); continue; }
            Check(text.font, text.text, Path(text.transform), failures,cache);
        }
        return failures.ToArray();
    }

    static void Check(TMP_FontAsset font, string text, string context, List<string> failures, Dictionary<(TMP_FontAsset,uint),bool> cache)
    {
        text = Tags.Replace(text ?? "", "");
        text = new string(text.Where(c => !char.IsControl(c) && c != '\uFE0F' && c != '\uFE0E' && c != '\u200D' && c != '\u200B').ToArray());
        if (text.Length == 0) return;
        var missing = new HashSet<uint>();
        for(int i=0;i<text.Length;i++)
        {
            uint value = char.IsSurrogatePair(text,i) ? (uint)char.ConvertToUtf32(text,i++) : text[i];
            if(!Supports(font,value,new HashSet<TMP_FontAsset>(),cache) &&
               !(TMP_Settings.fallbackFontAssets ?? new List<TMP_FontAsset>()).Any(f=>Supports(f,value,new HashSet<TMP_FontAsset>(),cache))) missing.Add(value);
        }
        if(missing.Count>0) failures.Add(context + " / " + font.name + ": " + string.Join(", ", missing.Select(c => "U+" + c.ToString("X4"))));
    }

    static bool Supports(TMP_FontAsset font,uint value,HashSet<TMP_FontAsset> visited,Dictionary<(TMP_FontAsset,uint),bool> cache)
    {
        if(!font || !visited.Add(font))return false;
        var key=(font,value);
        if(cache.TryGetValue(key,out bool found))return found;
        found=font.characterTable.Any(c=>c.unicode==value);
        if(!found && font.atlasPopulationMode!=AtlasPopulationMode.Static && font.sourceFontFile)
            found=FontEngine.LoadFontFace(font.sourceFontFile,(int)font.faceInfo.pointSize)==FontEngineError.Success &&
                FontEngine.TryGetGlyphWithUnicodeValue(value, GlyphLoadFlags.LOAD_NO_BITMAP, out var glyph) && glyph.index!=0;
        if(!found && font.fallbackFontAssetTable!=null)
            found=font.fallbackFontAssetTable.Any(f=>Supports(f,value,visited,cache));
        cache[key]=found;
        return found;
    }

    public static string[] ValidatePrefabs()
    {
        var paths=AssetDatabase.FindAssets("t:Prefab",new[]{"Assets"}).Select(AssetDatabase.GUIDToAssetPath)
            .Where(p=>p.Contains("/Resources/")).ToArray();
        return ValidateTexts(paths.Select(AssetDatabase.LoadAssetAtPath<GameObject>).Where(g=>g)
            .SelectMany(g=>g.GetComponentsInChildren<TMP_Text>(true)));
    }

    static string Path(Transform transform) => transform.parent ? Path(transform.parent) + "/" + transform.name : transform.name;

    public void OnProcessScene(Scene scene, BuildReport report)
    {
        if (report == null) return;
        var failures = ValidateScene(scene);
        if (failures.Length > 0) throw new BuildFailedException("场景固定文本缺字：\n" + string.Join("\n", failures));
    }

    public void OnPostprocessBuild(BuildReport report)
    {
        var forbidden = new HashSet<string>(new[] { PlatformFontAutomation.FullSource("Medium"), PlatformFontAutomation.FullSource("Bold") });
        var manifest = JsonUtility.FromJson<PlatformFontManifest>(File.ReadAllText(PlatformFontAutomation.Root + "/Generated/manifest.json"));
        foreach (var font in manifest.extraFonts ?? Array.Empty<PlatformExtraFont>())
            if (!string.IsNullOrEmpty(font.source)) forbidden.Add(font.source);
        var paths = report.packedAssets.SelectMany(asset => asset.contents).Select(info => info.sourceAssetPath)
            .Where(p => !string.IsNullOrEmpty(p)).Distinct().ToArray();
        var included = paths.Where(forbidden.Contains).ToArray();
        var sourceFonts = paths.Where(p => p.EndsWith(".otf", StringComparison.OrdinalIgnoreCase) ||
            p.EndsWith(".ttf", StringComparison.OrdinalIgnoreCase) || p.EndsWith(".ttc", StringComparison.OrdinalIgnoreCase)).ToArray();
        Directory.CreateDirectory(PlatformFontAutomation.Cache);
        File.WriteAllText(System.IO.Path.Combine(PlatformFontAutomation.Cache, "last-build-fonts.json"),
            JsonUtility.ToJson(new PackedFonts {
                outputPath = report.summary.outputPath, target = report.summary.platform.ToString(),
                hasPackedAssetDetails = paths.Length > 0, sourceFonts = sourceFonts, forbiddenSources = included
            }, true));
        if (included.Length > 0) throw new BuildFailedException("仅供烘焙的完整源字体意外进入构建：" + string.Join(", ", included));
    }

    [Serializable]
    sealed class PackedFonts
    {
        public string outputPath, target;
        public bool hasPackedAssetDetails;
        public string[] sourceFonts, forbiddenSources;
    }
}
