using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;

/// <summary>
/// Rebuilds the Alibaba PuHuiTi TMP asset in place. The main asset and its GUID
/// survive the rebuild; generated materials and atlas textures are embedded as
/// sub-assets so serialized TMP references remain valid after an editor reload.
/// </summary>
[InitializeOnLoad]
public static class AlibabaFontBake
{
    private const string FontPath = "Assets/Resources/font/Chinese/AlibabaPuHuiTi/AlibabaPuHuiTi-3-55-Regular.otf";
    private const string CharacterSetPath = "Assets/Resources/font/AlibabaPuHuiTi-3-55-Recommended.txt";
    private const string TargetPath = "Assets/Resources/font/Chinese/AlibabaPuHuiTi/AlibabaPuHuiTi-3-55-Regular SDF.asset";
    private const string TemporaryPath = "Assets/Resources/font/Chinese/AlibabaPuHuiTi/AlibabaPuHuiTi-3-55-Regular SDF.generated.asset";
    private const string TargetGuid = "bae11b662bda1504e8546327cf07ae25";

    static AlibabaFontBake()
    {
        EditorApplication.delayCall += TryRunRequestedBake;
    }

    private static void TryRunRequestedBake()
    {
        var workspaceRoot = Directory.GetParent(Directory.GetParent(Application.dataPath).FullName).FullName;
        var marker = Path.Combine(workspaceRoot, ".om_workspace", "AlibabaFontBake.request");
        if (!File.Exists(marker))
            return;
        File.Delete(marker);
        Rebuild();
    }

    [MenuItem("Tools/Fonts/Rebuild Alibaba PuHuiTi 3-55")]
    public static void Rebuild()
    {
        var font = AssetDatabase.LoadAssetAtPath<Font>(FontPath);
        var target = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(TargetPath);
        if (font == null)
            throw new InvalidOperationException($"Could not load source font: {FontPath}");
        if (target == null)
            throw new InvalidOperationException($"Could not load target font asset: {TargetPath}");

        var characterSet = File.ReadAllText(CharacterSetPath);
        if (characterSet.Length == 0)
            throw new InvalidOperationException("The recommended character set is empty.");
        if (AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(TemporaryPath) != null)
            AssetDatabase.DeleteAsset(TemporaryPath);

        Debug.Log($"[AlibabaFontBake] Creating asset for {characterSet.Length} UTF-16 code units.");
        var generated = TMP_FontAsset.CreateFontAsset(
            font,
            31,
            9,
            GlyphRenderMode.SDFAA,
            4096,
            4096,
            AtlasPopulationMode.Dynamic,
            true);
        if (generated == null)
            throw new InvalidOperationException("TMP_FontAsset.CreateFontAsset returned null.");

        var unicodeSet = new HashSet<uint>();
        for (var i = 0; i < characterSet.Length; i++)
        {
            var codePoint = char.ConvertToUtf32(characterSet, i);
            unicodeSet.Add((uint)codePoint);
            if (codePoint > 0xFFFF)
                i++;
        }

        if (!generated.TryAddCharacters(new List<uint>(unicodeSet).ToArray(), out uint[] missingCharacters, true))
            Debug.LogWarning("[AlibabaFontBake] TryAddCharacters reported failure; inspecting missing characters.");
        if (missingCharacters != null && missingCharacters.Length > 0)
            Debug.LogWarning($"[AlibabaFontBake] Missing characters reported by TMP: {missingCharacters.Length}");
        if (generated.material == null || generated.atlasTextures == null || generated.atlasTextures.Length == 0)
            throw new InvalidOperationException("The generated TMP font is missing its material or atlas textures.");

        // Remove obsolete embedded objects, but keep the main TMP_FontAsset object
        // so its GUID and fileID 11400000 remain stable.
        foreach (var embeddedObject in AssetDatabase.LoadAllAssetsAtPath(TargetPath))
        {
            if (embeddedObject != null && embeddedObject != target)
                UnityEngine.Object.DestroyImmediate(embeddedObject, true);
        }

        EditorUtility.CopySerialized(generated, target);
        target.name = "AlibabaPuHuiTi-3-55-Regular SDF";

        var targetAtlases = new Texture2D[generated.atlasTextures.Length];
        for (var i = 0; i < generated.atlasTextures.Length; i++)
        {
            var atlas = UnityEngine.Object.Instantiate(generated.atlasTextures[i]);
            atlas.name = target.name + " Atlas " + i;
            atlas.hideFlags = HideFlags.None;
            AssetDatabase.AddObjectToAsset(atlas, target);
            targetAtlases[i] = atlas;
            EditorUtility.SetDirty(atlas);
        }

        var targetMaterial = new Material(generated.material)
        {
            name = target.name + " Material",
            hideFlags = HideFlags.None
        };
        targetMaterial.SetTexture(ShaderUtilities.ID_MainTex, targetAtlases[0]);
        AssetDatabase.AddObjectToAsset(targetMaterial, target);
        target.atlasTextures = targetAtlases;
        target.material = targetMaterial;
        target.atlasPopulationMode = AtlasPopulationMode.Static;
        EditorUtility.SetDirty(targetMaterial);
        EditorUtility.SetDirty(target);
        AssetDatabase.SaveAssets();

        if (!AssetDatabase.TryGetGUIDAndLocalFileIdentifier(targetMaterial, out _, out long materialFileId))
            throw new InvalidOperationException("Could not determine the rebuilt material local file ID.");

        var rewrittenFiles = RewriteSerializedMaterialReferences(materialFileId);
        foreach (var tmpText in Resources.FindObjectsOfTypeAll<TMP_Text>())
        {
            if (tmpText != null && tmpText.font == target)
            {
                tmpText.fontSharedMaterial = targetMaterial;
                EditorUtility.SetDirty(tmpText);
            }
        }

        AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);
        var rebuilt = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(TargetPath);
        if (rebuilt == null || rebuilt.material == null || rebuilt.atlasTextures == null || rebuilt.atlasTextures.Length == 0)
            throw new InvalidOperationException("The rebuilt TMP font did not reload with valid embedded resources.");
        Debug.Log($"[AlibabaFontBake] Rebuilt {TargetPath}; glyphs={rebuilt.glyphTable.Count}, atlases={rebuilt.atlasTextures.Length}, materialFileId={materialFileId}, rewrittenFiles={rewrittenFiles}.");
    }

    private static int RewriteSerializedMaterialReferences(long materialFileId)
    {
        // Match every material local ID for this font GUID, not only the two IDs
        // known today, so future rebuilds also migrate references safely.
        var pattern = new Regex(@"(m_sharedMaterial:\s*\{fileID:\s*)-?\d+(?=,\s*guid:\s*" + TargetGuid + @")");
        var rewrittenFiles = 0;
        foreach (var assetPath in AssetDatabase.GetAllAssetPaths())
        {
            var extension = Path.GetExtension(assetPath);
            if (!extension.Equals(".prefab", StringComparison.OrdinalIgnoreCase) &&
                !extension.Equals(".unity", StringComparison.OrdinalIgnoreCase) &&
                !extension.Equals(".asset", StringComparison.OrdinalIgnoreCase))
                continue;
            if (!File.Exists(assetPath))
                continue;

            var original = File.ReadAllText(assetPath);
            var rewritten = pattern.Replace(original, match => match.Groups[1].Value + materialFileId);
            if (rewritten == original)
                continue;
            File.WriteAllText(assetPath, rewritten);
            rewrittenFiles++;
        }
        return rewrittenFiles;
    }
}
