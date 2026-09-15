using UnityEngine;

public partial class ConfigManager
{
    const string ClothColorPrefix = "TableClothColor_";
    const string ClothBrightnessPrefix = "TableClothBrightness_";
    public Color GetTableClothColor(string style)
    {
        if (ColorUtility.TryParseHtmlString(PlayerPrefs.GetString(ClothColorPrefix+style,""),out var color))
            return new Color(color.r,color.g,color.b,1);
        return TableClothStyles.DefaultColor(style);
    }
    public float GetTableClothBrightness(string style) => Mathf.Clamp(PlayerPrefs.GetFloat(ClothBrightnessPrefix+style,0),-1,1);
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
