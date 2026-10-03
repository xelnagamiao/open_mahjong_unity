using UnityEngine;

public partial class ConfigManager
{
    const string ClothColorPrefix = "TableClothColor_";
    const string ClothBrightnessPrefix = "TableClothBrightness_";
    public Color GetTableClothColor(string style)
    {
        // Official presets are immutable; user colors live in TableSurfaceColorLibrary.
        return TableClothStyles.DefaultColor(style);
    }
    public float GetTableClothBrightness(string style) => 0;
    public Color GetTableClothDisplayColor(string style)
    {
        return ApplyColorBrightness(GetTableClothColor(style), GetTableClothBrightness(style));
    }
    public void SetTableClothColor(string style,Color color)
    {
        if(TableClothStyles.IsSolid(style)) PlayerPrefs.SetString(ClothColorPrefix+style,"#"+ColorUtility.ToHtmlStringRGB(color));
    }
    public void SetTableClothBrightness(string style,float value)
    {
        if(TableClothStyles.IsSolid(style)) PlayerPrefs.SetFloat(ClothBrightnessPrefix+style,Mathf.Clamp(value,-1,1));
    }
    private void ResetTableClothColors()
    {
        foreach(string style in TableClothStyles.SolidNames) {
            PlayerPrefs.DeleteKey(ClothColorPrefix+style);
            PlayerPrefs.DeleteKey(ClothBrightnessPrefix+style);
        }
    }
}
