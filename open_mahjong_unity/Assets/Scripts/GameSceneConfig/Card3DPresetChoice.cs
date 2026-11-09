using TMPro;
using UnityEngine;

/// <summary>The existing image/name choice with a real rotating tile preview.</summary>
public sealed class Card3DPresetChoice : MonoBehaviour
{
    public CardDesignModelPreview preview;
    public TMP_Text caption, order;
    public CardSurfaceOptionView selection;
    public CanvasGroup feedback;

    public void Show(Card3DPreset preset, bool current, bool selected, bool interactable = true, string position = "")
    {
        caption.text = preset.name;
        if (order != null) order.text = position;
        selection.SetSelected(selected);
        feedback.alpha = interactable ? 1 : .45f;
        preview.SetPresetPreview(preset.appearance, current, Mathf.Repeat(preset.id.GetHashCode() % 360, 360));
    }
}

/// <summary>Preview saved appearances without selecting them or changing the live configuration.</summary>
public static class Card3DPresetPreviewMaterial
{
    static Texture2D Texture(string path, bool custom)
    {
        if (string.IsNullOrEmpty(path)) return null;
        if (Card3DPresetTextureCache.TryGet(path, out var texture)) return texture;
        return custom ? null : Resources.Load<Texture2D>(path);
    }

    public static void Apply(Material material, Card3DAppearance value)
    {
        var backTexture = Texture(value.backImage, value.backImageCustom);
        var background = Texture(value.backgroundImage, value.backgroundImageCustom);
        var back = ConfigManager.ApplyColorBrightness(value.back, value.backBrightness);
        var face = ConfigManager.ApplyColorBrightness(value.face, value.faceBrightness);
        var frontIndependent = ConfigManager.ApplyColorBrightness(value.frontEdge, value.frontEdgeBrightness);
        var backIndependent = ConfigManager.ApplyColorBrightness(value.backEdge, value.backEdgeBrightness);
        var backEdge = value.backEdgeMode == 1 ? back : value.backEdgeMode == 2 ? frontIndependent : backIndependent;
        var frontEdge = value.frontEdgeMode == 2 ? backIndependent : value.frontEdgeMode == 1
            ? value.useSolid ? face : value.useBackground && background != null ? Color.white : ConfigManager.DefaultTableFaceFallbackColor
            : frontIndependent;
        material.SetTexture("_BackTex", backTexture);
        material.SetFloat("_BackTexBlend", backTexture != null ? 1 : 0);
        material.SetColor("_BackColor", back);
        material.SetColor("_SideColor", value.side);
        material.SetColor("_BackEdgeColor", backEdge);
        material.SetColor("_FrontEdgeColor", frontEdge);
        material.SetTexture("_FrontBgTex", background);
        material.SetFloat("_FrontBgBlend", value.useBackground && !value.useSolid && background != null ? 1 : 0);
        material.SetFloat("_FrontBgTexAspect", background != null ? (float)background.width / background.height : 0);
        material.SetFloat("_TableBgCoverFace", value.frontEdgeMode == 1 ? 1 : 0);
        material.SetColor("_TableFaceColor", face);
        material.SetFloat("_TableFaceBlend", value.useSolid ? 1 : 0);
    }
}
