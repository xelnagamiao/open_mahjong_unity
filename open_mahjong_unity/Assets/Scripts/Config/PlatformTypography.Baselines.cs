using TMPro;

public sealed partial class PlatformTypography
{
    [System.Serializable]
    public sealed class BaselineFontAdjustment
    {
        public TMP_FontAsset font;
        public float offsetEm;
    }
    public TMP_FontAsset baselineReferenceFont;
    public System.Collections.Generic.List<BaselineFontAdjustment> baselineFontAdjustments = new();
    public static TMP_FontAsset BaselineReferenceFont => Profile && Profile.baselineReferenceFont ? Profile.baselineReferenceFont : ChineseRegular;
    public static float BaselineFontOffset(TMP_FontAsset font)
    {
        if (Profile && Profile.baselineFontAdjustments != null)
            foreach (var entry in Profile.baselineFontAdjustments)
                if (entry != null && entry.font == font) return entry.offsetEm;
        return 0;
    }
    internal static float BaselineReferenceOffset(float size)
    {
        var font = BaselineReferenceFont;
        if (!font || font.faceInfo.pointSize <= 0) return -.35f * size;
        var face = font.faceInfo;
        return -(face.ascentLine + face.descentLine - 2 * face.baseline) * .5f
            * size / face.pointSize * face.scale;
    }
}
