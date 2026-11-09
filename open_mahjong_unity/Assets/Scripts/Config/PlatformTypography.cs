using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using TMPro;
using UnityEngine;

public sealed partial class PlatformTypography : ScriptableObject
{
    public TMP_FontAsset regular, medium, bold;
    public static event Action Changed;
    static PlatformTypography profile;
    sealed class MaterialVariant
    {
        public Material material;
        public int sourceCrc, targetCrc;
    }
    static readonly Dictionary<(Material source, TMP_FontAsset font), MaterialVariant> RuntimeMaterials = new();
#if UNITY_EDITOR
    static readonly Dictionary<(Material source, TMP_FontAsset font), MaterialVariant> EditorMaterials = new();
#endif
    static readonly Regex BoldMarkup = new Regex(@"</?b>|<font-weight\s*=\s*[^>]*>|</font-weight>", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    public static void Refresh()
    {
        ApplyGlobalFonts();
        Changed?.Invoke();
    }
    static PlatformTypography Profile => profile ? profile : profile = Resources.Load<PlatformTypography>("Localization/PlatformTypography");
    public static bool HasChinese(string value)
    {
        if (string.IsNullOrEmpty(value)) return false;
        foreach (char c in value)
            if ((c >= '\u3400' && c <= '\u9FFF') || (c >= '\uF900' && c <= '\uFAFF')) return true;
        return false;
    }
    public static bool HeavyFace(TMP_FontAsset font)
    {
        if (!font) return false;
        string style = font.faceInfo.styleName.ToLowerInvariant();
        return style.Contains("bold") || style.Contains("black") || style.Contains("heavy") || style.Contains("medium");
    }
    public static string NormalizeChineseMarkup(string value) => HasCjk(value) && value.IndexOf('<') >= 0 ? BoldMarkup.Replace(value, "") : value;
    public static TMP_FontAsset Resolve(TMP_FontAsset original) => Resolve(original, null);
    public static TMP_FontAsset Resolve(TMP_FontAsset original, string text, bool richText = false)
    {
        if (!original) return original;
        if (UseJapaneseFont(text) && JapaneseRegular) return JapaneseRegular;
        var mixed = Profile ? Profile.mixedFonts.Find(entry => entry.font == original) : null;
        var latin = mixed != null ? mixed.latin : ResolveLatin(original);
        original = mixed != null ? mixed.chinese : original;
        if (!HasChinese(text)) return latin;
        var chinese = ResolveChineseFont(original);
        return HasLatinContent(text, richText) ? ResolveMixedFont(latin, chinese) : chinese;
    }
    static TMP_FontAsset ResolveLatin(TMP_FontAsset original)
    {
        if (!Profile) return original;
        string style = original.faceInfo.styleName.ToLowerInvariant();
        return style.Contains("bold") || style.Contains("black") || style.Contains("heavy") ? Profile.bold
            : style.Contains("medium") || style.Contains("semibold") ? Profile.medium : Profile.regular;
    }
    public static Material ResolveMaterial(Material original, TMP_FontAsset font)
    {
        if (!font || !font.material) return original;
        if (!original) return font.material;
#if UNITY_EDITOR
        if (!Application.isPlaying) return ResolveEditorMaterial(original, font);
#endif
        if (original.mainTexture == font.material.mainTexture && (original.hideFlags & HideFlags.DontSave) == 0) return original;
        var key = (original, font);
        int sourceCrc = original.ComputeCRC(), targetCrc = font.material.ComputeCRC();
        if (!RuntimeMaterials.TryGetValue(key, out var cached) || !cached.material)
        {
            cached = new MaterialVariant { material = new Material(original) { hideFlags = HideFlags.HideAndDontSave } };
            RuntimeMaterials[key] = cached;
            CopyMaterialStyle(cached.material, original, font.material);
        }
        else if (cached.sourceCrc != sourceCrc || cached.targetCrc != targetCrc)
            CopyMaterialStyle(cached.material, original, font.material);
        cached.sourceCrc = sourceCrc; cached.targetCrc = targetCrc;
        return cached.material;
    }

    static void CopyMaterialStyle(Material destination, Material source, Material target)
    {
        bool sdf = source.HasProperty(ShaderUtilities.ID_GradientScale) && target.HasProperty(ShaderUtilities.ID_GradientScale);
        var template = sdf ? source : target;
        destination.shader = template.shader;
        destination.CopyPropertiesFromMaterial(template);
        destination.shaderKeywords = template.shaderKeywords;
        if (sdf)
        {
            destination.SetTexture(ShaderUtilities.ID_MainTex, target.GetTexture(ShaderUtilities.ID_MainTex));
            foreach (int property in new[] { ShaderUtilities.ID_GradientScale, ShaderUtilities.ID_TextureWidth,
                ShaderUtilities.ID_TextureHeight, ShaderUtilities.ID_WeightNormal, ShaderUtilities.ID_WeightBold })
                destination.SetFloat(property, target.GetFloat(property));
        }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ClearRuntimeMaterials()
    {
        foreach (var cached in RuntimeMaterials.Values) if (cached.material) Destroy(cached.material);
        RuntimeMaterials.Clear();
        ClearRuntimeMixedFonts();
        ApplyGlobalFonts();
    }

#if UNITY_EDITOR
    static Material ResolveEditorMaterial(Material original, TMP_FontAsset font)
    {
        var target = font.material;
        if (UnityEditor.AssetDatabase.Contains(original) && original.mainTexture == target.mainTexture) return original;
        if (!UnityEditor.AssetDatabase.TryGetGUIDAndLocalFileIdentifier(font, out string fontGuid, out long fontId))
            return target;
        int sourceCrc = original.ComputeCRC(), targetCrc = target.ComputeCRC();
        var key = (original, font);
        if (EditorMaterials.TryGetValue(key, out var cached) && cached.material
            && cached.sourceCrc == sourceCrc && cached.targetCrc == targetCrc) return cached.material;
        string sourceKey = UnityEditor.AssetDatabase.TryGetGUIDAndLocalFileIdentifier(original, out string sourceGuid, out long sourceId)
            ? sourceGuid + "_" + sourceId : "recovered_" + sourceCrc.ToString("x8");
        const string folder = "Assets/Resources/Localization/Materials";
        if (!UnityEditor.AssetDatabase.IsValidFolder(folder)) UnityEditor.AssetDatabase.CreateFolder("Assets/Resources/Localization", "Materials");
        string path = folder + "/" + sourceKey + "_" + fontGuid + "_" + fontId + ".mat";
        var material = UnityEditor.AssetDatabase.LoadAssetAtPath<Material>(path);
        if (!material)
        {
            material = new Material(original) { name = original.name + " / " + font.name, hideFlags = HideFlags.None };
            CopyMaterialStyle(material, original, target);
            UnityEditor.AssetDatabase.CreateAsset(material, path);
        }
        else
        {
            CopyMaterialStyle(material, original, target);
            material.hideFlags = HideFlags.None;
            UnityEditor.EditorUtility.SetDirty(material);
        }
        EditorMaterials[key] = new MaterialVariant { material = material, sourceCrc = sourceCrc, targetCrc = targetCrc };
        return material;
    }
#endif
}
