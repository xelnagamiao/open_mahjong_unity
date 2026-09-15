using System;
using UnityEngine;

/// <summary>Stable IDs for the six former patterned cloths (original gallery positions 5,6,7,8,10,11).</summary>
public static class TableClothStyles
{
    public static readonly string[] SolidNames = {
        "Tablecloth_Focus_02_PeacockTeal", "Tablecloth_Focus_03_Cobalt",
        "Tablecloth_Focus_04_Wisteria", "Tablecloth_Focus_05_Bamboo",
        "Tablecloth_Focus_07_MoonSea", "Tablecloth_Focus_08_CityGlass"
    };
    static readonly Color32[] Colors = {
        new Color32(30,82,87,255), new Color32(41,79,121,255),
        new Color32(146,172,227,255), new Color32(22,106,98,255),
        new Color32(15,40,77,255), new Color32(27,111,150,255)
    };
    static readonly string[] Labels = { "纯色孔雀青", "纯色钴蓝", "纯色藤蓝", "纯色竹绿", "纯色月海蓝", "纯色都市青" };
    public static bool IsSolid(string name) => Array.IndexOf(SolidNames, name) >= 0;
    public static Color DefaultColor(string name) { int i=Array.IndexOf(SolidNames,name); return i>=0 ? Colors[i] : Color.white; }
    public static string DisplayName(string name) { int i=Array.IndexOf(SolidNames,name); return i>=0 ? Labels[i] : name; }
}
