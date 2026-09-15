using UnityEngine;

/// <summary>Additional cloth shading; UV dimensions are relative to the cloth, not the frame.</summary>
public static class TableLightingPresets
{
    public const int DefaultShadow = 8;
    public const int DefaultLight = 3;
    // DeskTopNew's cloth submesh occupies this region of the shared table UV atlas.
    // Original PSD light, base and seam use atlas UVs; additional designs use cloth UVs.
    public static readonly Vector4 ClothUvRect = new Vector4(.0702578f, .0702578f, .8594844f, .8594844f);
    public static readonly string[] ShadowNames =
    {
        "硬边阴影", "无阴影", "窄均匀阴影", "标准均匀阴影", "宽均匀阴影",
        "窄柔和阴影", "标准柔和阴影", "扩散阴影", "加深扩散阴影"
    };
    public static readonly string[] LightNames =
    {
        "方形光照", "圆形光照", "强圆形光照", "无光照"
    };

    // Presentation order is independent from persisted IDs and shader parameter IDs.
    private static readonly int[] ShadowOrder = { 8, 0, 5, 6, 7, 1, 2, 3, 4 };
    private static readonly int[] LightOrder = { 3, 1, 2, 0 };
    public static int ShadowStyleAtOption(int index) => ShadowOrder[Mathf.Clamp(index, 0, ShadowOrder.Length - 1)];
    public static int LightStyleAtOption(int index) => LightOrder[Mathf.Clamp(index, 0, LightOrder.Length - 1)];
    public static int ShadowOptionForStyle(int style) => System.Array.IndexOf(ShadowOrder, ClampShadow(style));
    public static int LightOptionForStyle(int style) => System.Array.IndexOf(LightOrder, ClampLight(style));
    public static int ClampShadow(int value) => value >= 0 && value <= 8 ? value : DefaultShadow;
    // Retired upper-light ID 4 (and invalid IDs) fall back to the new first option.
    public static int ClampLight(int value) => value >= 0 && value <= 3 ? value : DefaultLight;

    // x/y = feather width/black opacity; z/w = optional narrow contact width/opacity.
    // The original Edge OuterGlow is 90 px across a 1720 px cloth (5.23%).
    public static Vector4 ShadowParameters(int style)
    {
        switch (ClampShadow(style))
        {
            case 1: return new Vector4(.024f, .42f, 0f, 0f);
            case 2: return new Vector4(90f / 1720f, .58f, 0f, 0f);
            case 3: return new Vector4(.19f, .34f, 0f, 0f);
            case 4: return new Vector4(.105f, .46f, .018f, .30f);
            default: return Vector4.zero;
        }
    }

    // x/y = solid core / total reach, in normalized atlas units; z = black opacity.
    // Distances are 2048px source equivalents, fixed on the tabletop, not the screen.
    // w selects this non-overlapping planar band instead of the legacy corner shading.
    public static Vector4 FixedShadowParameters(int style)
    {
        switch (ClampShadow(style))
        {
            case 5: return new Vector4(8f / 2048f, 32f / 2048f, .36f, 1f);
            case 6: return new Vector4(12f / 2048f, 56f / 2048f, .42f, 1f);
            case 7: return new Vector4(20f / 2048f, 80f / 2048f, .48f, 1f);
            case 8: return new Vector4(23f / 2048f, 24f / 2048f, .40f, 1f);
            default: return Vector4.zero;
        }
    }

    // x/y = radii, z = superellipse power, w = white Normal-layer opacity.
    // These are editable interpretations of the source and references, not platform constants.
    public static Vector4 LightParameters(int style)
    {
        switch (ClampLight(style))
        {
            case 1: return new Vector4(.46f, .46f, 2f, .20f);
            case 2: return new Vector4(.95f, .95f, 2f, .24f);
            case 3: return new Vector4(.48f, .48f, 4f, .24f);
            default: return new Vector4(1f, 1f, 2f, 0f);
        }
    }

    public static Vector4 LightCenter(int style) => new Vector4(.50f, .50f, 0f, 0f);
}
