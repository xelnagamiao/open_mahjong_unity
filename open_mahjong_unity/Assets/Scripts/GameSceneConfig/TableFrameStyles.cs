using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>Built-in frame choices, including variants that reuse an existing texture.</summary>
public static class TableFrameStyles
{
    public const string Default = "Edge_orange_wood";
    public const string Deep = "Edge_orange_wood_deep";

    public static IReadOnlyList<string> OrderedNames { get; } = Array.AsReadOnly(new[]
    {
        Default,
        Deep,
        "Edge_Relief_02_WhiteOakSilver",
        "Edge_Relief_03_CherryGunmetal",
        "Edge_Relief_04_EbonyTitanium",
        "Edge_Relief_05_JadeAnodized",
        "Edge_Relief_06_IvoryEnamelWalnut",
        "Edge_Focus_01_Sakura",
        "Edge_Focus_02_PeacockTeal",
        "Edge_Focus_03_Cobalt",
        "Edge_Focus_04_Wisteria",
        "Edge_Focus_05_Bamboo",
        "Edge_Focus_06_AutumnMaple",
        "Edge_Focus_07_MoonSea",
        "Edge_Focus_08_CityGlass",
        "Edge_Focus_09_Celadon",
        "Edge_Focus_10_Ginkgo",
        "Edge_bule"
    });

    public static string SourceName(string name) => name == Deep ? Default : name ?? "";

    public static bool IsSolid(string name) => name != null && SolidColors.ContainsKey(name);

    // Original clean-atlas colors, now stored as parameters rather than ten 2K textures.
    private static readonly Dictionary<string, Color32> SolidColors = new Dictionary<string, Color32>(StringComparer.Ordinal) {
        {"Edge_Focus_01_Sakura",new Color32(19,40,64,255)},
        {"Edge_Focus_02_PeacockTeal",new Color32(23,61,66,255)},
        {"Edge_Focus_03_Cobalt",new Color32(25,45,78,255)},
        {"Edge_Focus_04_Wisteria",new Color32(52,48,76,255)},
        {"Edge_Focus_05_Bamboo",new Color32(24,58,49,255)},
        {"Edge_Focus_06_AutumnMaple",new Color32(84,40,45,255)},
        {"Edge_Focus_07_MoonSea",new Color32(22,53,76,255)},
        {"Edge_Focus_08_CityGlass",new Color32(18,62,78,255)},
        {"Edge_Focus_09_Celadon",new Color32(34,81,88,255)},
        {"Edge_Focus_10_Ginkgo",new Color32(26,37,37,255)}
    };

    public static Color SolidColor(string name)
    {
        return name != null && SolidColors.TryGetValue(name, out var color) ? (Color)color : Color.white;
    }

    // Display-encoded RGB multipliers; alpha and the original wood grain are unchanged.
    public static Vector4 BaseTone(string name) => name == Deep ? new Vector4(.60f, .64f, .70f, 1f) : Vector4.one;

    public static int SortOrder(string name)
    {
        for (int index = 0; index < OrderedNames.Count; index++)
            if (OrderedNames[index] == name) return index;
        return int.MaxValue;
    }

    public static string DisplayName(string name)
    {
        switch (name)
        {
            case Default: return "原版橙木";
            case Deep: return "深色橙木";
            case "Edge_Relief_02_WhiteOakSilver": return "白橡木与银边";
            case "Edge_Relief_03_CherryGunmetal": return "樱木与枪灰边";
            case "Edge_Relief_04_EbonyTitanium": return "黑钛金属";
            case "Edge_Relief_05_JadeAnodized": return "青玉金属";
            case "Edge_Relief_06_IvoryEnamelWalnut": return "象牙漆木扶手";
            case "Edge_Focus_01_Sakura": return "藏蓝";
            case "Edge_Focus_02_PeacockTeal": return "孔雀青";
            case "Edge_Focus_03_Cobalt": return "钴蓝";
            case "Edge_Focus_04_Wisteria": return "深紫";
            case "Edge_Focus_05_Bamboo": return "墨绿";
            case "Edge_Focus_06_AutumnMaple": return "暗红";
            case "Edge_Focus_07_MoonSea": return "海蓝";
            case "Edge_Focus_08_CityGlass": return "湖蓝";
            case "Edge_Focus_09_Celadon": return "青瓷";
            case "Edge_Focus_10_Ginkgo": return "墨青";
            case "Edge_bule": return "原版蓝色";
            default: return name ?? "";
        }
    }
}
