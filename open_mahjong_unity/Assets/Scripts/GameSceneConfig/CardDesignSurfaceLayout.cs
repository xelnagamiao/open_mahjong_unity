using UnityEngine;

/// <summary>Surface choices and card rotation share one settings scroll.</summary>
public sealed class CardDesignSurfaceLayout : MonoBehaviour
{
    public RectTransform content;
    public CardDesignImagePicker background, back;
    public Card3DPresetPanel rotation;
    void OnEnable() => RefreshLayout();
    public void RefreshLayout()
    {
        if (background == null || back == null || content == null) return;
        background.PlaceEdgeControls(); back.PlaceEdgeControls();
        float top = 0;
        if (rotation != null && rotation.PresetSection != null)
        {
            rotation.LayoutPresetChoices();
            CardFaceLayoutPresentation.Place(rotation.PresetSection, 0, 0, 610, rotation.PresetSectionHeight);
            top += rotation.PresetSectionHeight + 22;
        }
        CardFaceLayoutPresentation.Place((RectTransform)background.transform, 0, top, 610, background.TotalHeight);
        CardFaceLayoutPresentation.Place((RectTransform)back.transform, 0, top + background.TotalHeight + 22, 610, back.TotalHeight);
        float height = top + background.TotalHeight + back.TotalHeight + 44;
        if (rotation != null && rotation.RotationSection != null)
        {
            rotation.LayoutInlineContent();
            CardFaceLayoutPresentation.Place(rotation.RotationSection, 0, height, 610, rotation.InlineHeight);
            height += rotation.InlineHeight + 22;
        }
        content.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, height);
    }

    public void ScrollToRotation()
    {
        RefreshLayout();
        var scroll = content.GetComponentInParent<UnityEngine.UI.ScrollRect>();
        if (scroll == null) return;
        Canvas.ForceUpdateCanvases();
        scroll.StopMovement();
        scroll.verticalNormalizedPosition = 0;
    }
}
