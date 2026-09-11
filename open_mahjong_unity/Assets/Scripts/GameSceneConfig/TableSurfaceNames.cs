using System;

/// <summary>Display metadata only; resource names remain the persisted selection keys.</summary>
public static class TableSurfaceNames
{
    private const string MetalPrefix = "Tablecloth_MetalSeam_";
    private static readonly string[] Styles =
    {
        "当前细线（对照）", "柔和加粗暗槽", "深黑机械切口", "青灰单侧倒角",
        "升牌口暖灰金属", "宽口冷钢倒角", "宽深槽·双阶倒角"
    };
    private static readonly string[] Backgrounds = { "蓝色纤维", "素绿", "绿色纤维" };

    public static bool TryGetMetalSeam(string resourceName, out int style, out int background)
    {
        style = -1;
        background = -1;
        if (string.IsNullOrEmpty(resourceName)) return false;

        // Legacy combined keys are retained only for saved-selection migration.
        switch (resourceName)
        {
            case "Tablecloth_Official_Blue_NewSeams": style = 0; background = 0; return true;
            case "Tablecloth_Official_Green_NewSeams": style = 0; background = 1; return true;
            case "Tablecloth_Official_Green2_NewSeams": style = 0; background = 2; return true;
        }
        if (!resourceName.StartsWith(MetalPrefix, StringComparison.Ordinal)) return false;
        string suffix = resourceName.Substring(MetalPrefix.Length);
        if (suffix.Length < 4 || suffix[0] != '0' || suffix[1] < '1' || suffix[1] > '6' || suffix[2] != '_')
            return false;
        switch (suffix.Substring(3))
        {
            case "Blue": background = 0; break;
            case "Green": background = 1; break;
            case "Green2": background = 2; break;
            default: return false;
        }
        style = suffix[1] - '0';
        return true;
    }

    public static string ClothDisplayName(string resourceName)
    {
        return TryGetMetalSeam(resourceName, out int style, out int background)
            ? style.ToString("00") + " " + Styles[style] + " · " + Backgrounds[background]
            : resourceName;
    }

    public static string SeamDisplayName(int style)
    {
        if (style == -1) return "无缝线";
        return style >= 0 && style < Styles.Length ? style.ToString("00") + " " + Styles[style] : "";
    }

    public static int ClothSortOrder(string resourceName)
    {
        return TryGetMetalSeam(resourceName, out int style, out int background)
            ? style * 3 + background : int.MaxValue;
    }
}
