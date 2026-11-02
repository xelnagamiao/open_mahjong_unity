using System;
using System.IO;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEngine;

public static class PlatformFontCatalog
{
    [Serializable] public sealed class Catalog { public Entry[] fonts; public Alias[] aliases; }
    [Serializable] public sealed class Entry { public string guid, path, source, family; public uint[] seed; }
    [Serializable] public sealed class Alias { public string guid, target; }

    public static void Export()
    {
        var fonts = AssetDatabase.FindAssets("t:TMP_FontAsset", new[] { "Assets" })
            .Select(AssetDatabase.GUIDToAssetPath).Where(p => !p.Contains("/Localization/MixedFonts/"))
            .Select(AssetDatabase.LoadAssetAtPath<TMP_FontAsset>)
            .Where(f => f && (f.faceInfo.familyName.StartsWith("Kongyuan Sans", StringComparison.Ordinal)
                || f.faceInfo.familyName.StartsWith("Font Awesome", StringComparison.Ordinal))).ToArray();
        var entries = fonts.Select(f => new Entry {
            guid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(f)), path = AssetDatabase.GetAssetPath(f),
            family = f.faceInfo.familyName,
            source = AssetDatabase.GUIDToAssetPath(new SerializedObject(f).FindProperty("m_SourceFontFileGUID").stringValue),
            seed = f.characterTable.Select(c => c.unicode).Distinct().OrderBy(c => c).ToArray()
        }).OrderBy(e => e.path, StringComparer.Ordinal).ToArray();
        var profile = Resources.Load<PlatformTypography>("Localization/PlatformTypography");
        var aliases = profile.mixedFonts.Where(e => e.font && fonts.Contains(e.chinese))
            .Select(e => new Alias {guid=AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(e.font)),
                target=AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(e.chinese))})
            .OrderBy(e=>e.guid,StringComparer.Ordinal).ToArray();
        string path = Path.Combine(PlatformFontAutomation.Cache, "font-inputs.json");
        string json = JsonUtility.ToJson(new Catalog { fonts=entries, aliases=aliases }, true);
        Directory.CreateDirectory(PlatformFontAutomation.Cache);
        if (!File.Exists(path) || File.ReadAllText(path) != json) File.WriteAllText(path, json);
    }
}
