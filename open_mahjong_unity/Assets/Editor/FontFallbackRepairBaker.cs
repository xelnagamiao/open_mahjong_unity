#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class FontFallbackRepairBaker
{
    const string Folder = "Assets/Resources/Localization/";

    public static void RepairFontAssets()
    {
        PlatformTypographyBaker.EnsureProfile();
        foreach (string guid in AssetDatabase.FindAssets("t:TMP_FontAsset", new[] { "Assets" }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (path.StartsWith(Folder + "MixedFonts/", StringComparison.Ordinal)) continue;
            RepairFont(AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(path));
        }
    }

    public static void RepairFont(TMP_FontAsset font)
    {
        if (!font) return;
        bool sourceHan = PlatformFontAutomationAssets.IsManaged(font);
        if (!sourceHan) return;
        if (font.atlasPopulationMode != AtlasPopulationMode.Static)
        {
            var data = new SerializedObject(font);
            if (!font.sourceFontFile)
            {
                string guid = data.FindProperty("m_SourceFontFileGUID").stringValue;
                var source = AssetDatabase.LoadAssetAtPath<Font>(AssetDatabase.GUIDToAssetPath(guid));
                if (!source) throw new InvalidOperationException("Dynamic font source is missing: " + AssetDatabase.GetAssetPath(font));
                data.FindProperty("m_SourceFontFile").objectReferenceValue = source;
            }
            data.FindProperty("m_ClearDynamicDataOnBuild").boolValue = true;
            data.ApplyModifiedPropertiesWithoutUndo();
            font.isMultiAtlasTexturesEnabled = true;
        }
        else
        {
            string weight = font.faceInfo.styleName;
            string name = "EnglishSourceHan" + weight + "Fallback";
            var fallback = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(Folder + name + ".asset");
            if (!fallback || !PlatformFontAutomationAssets.IsManaged(fallback)
                || fallback.faceInfo.styleName != font.faceInfo.styleName)
                throw new InvalidOperationException("No same-family, same-weight fallback for " + AssetDatabase.GetAssetPath(font));
            font.fallbackFontAssetTable = PlatformFontAutomationAssets.Fallbacks(font);
        }
        font.ReadFontAssetDefinition();
        EditorUtility.SetDirty(font);
    }

    public static void Bake(Scene scene)
    {
        RepairFontAssets();
        TableHudTypographyBaker.Prepare();
        RoundPanelUiBaker.ApplyTypography(scene);
        PlatformTypography.RefreshSceneFonts();
    }

}
#endif
