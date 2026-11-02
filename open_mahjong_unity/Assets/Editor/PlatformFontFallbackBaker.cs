#if UNITY_EDITOR
using TMPro;
using UnityEditor;

public static class PlatformFontFallbackBaker
{
    public static void Bake()
    {
        PlatformTypographyBaker.EnsureProfile();
        FontFallbackRepairBaker.RepairFontAssets();
        PlatformTypography.Refresh();
        EditorUtility.SetDirty(TMP_Settings.instance);
    }
}
#endif
