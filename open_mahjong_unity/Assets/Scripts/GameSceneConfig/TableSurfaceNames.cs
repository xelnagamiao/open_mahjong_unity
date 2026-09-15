using System;

/// <summary>Surface options and legacy selection migration; resource names remain stable.</summary>
public static class TableSurfaceNames
{
    public const int DefaultSeam = 3;
    public static int SeamStyleCount => Styles.Length;
    public static int SeamOptionCount => SeamDisplayOrder.Length;

    private const string MetalPrefix = "Tablecloth_MetalSeam_";
    private static readonly string[] Styles =
    {
        "纯色细线", "纯色标准线", "纯色粗线", "标准缝线", "粗缝线"
    };
    // Display order is independent of the persisted IDs and Seam_00–04 resource keys.
    private static readonly int[] SeamDisplayOrder = { 3, 4, -1, 0, 1, 2 };
    private static readonly string[] Backgrounds = { "蓝色纤维", "素绿", "绿色纤维" };

    // Keep explicit none and surviving IDs; retired profiles fall back to the default.
    public static int NormalizeSeamStyle(int style) =>
        style >= -1 && style < SeamStyleCount ? style : DefaultSeam;

    public static int SeamStyleAtOption(int index) =>
        index >= 0 && index < SeamOptionCount ? SeamDisplayOrder[index] : DefaultSeam;

    public static int SeamOptionForStyle(int style) =>
        Array.IndexOf(SeamDisplayOrder, NormalizeSeamStyle(style));

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
        if (TableClothStyles.IsSolid(resourceName)) return TableClothStyles.DisplayName(resourceName);
        return TryGetMetalSeam(resourceName, out int style, out int background)
            ? SeamDisplayName(NormalizeSeamStyle(style)) + " · " + Backgrounds[background]
            : resourceName;
    }

    public static string SeamDisplayName(int style)
    {
        if (style == -1) return "无缝线";
        return style >= 0 && style < Styles.Length ? Styles[style] : "";
    }

    public static int ClothSortOrder(string resourceName)
    {
        if (TableClothStyles.IsSolid(resourceName)) return int.MaxValue;
        // Keep the three original cloths first, with MainScene's default blue first.
        switch (resourceName)
        {
            case "Tablecloth_blue": return -3;
            case "Tablecloth_green": return -2;
            case "Tablecloth_green2": return -1;
        }
        return TryGetMetalSeam(resourceName, out int style, out int background)
            ? style * 3 + background : 100;
    }
}
