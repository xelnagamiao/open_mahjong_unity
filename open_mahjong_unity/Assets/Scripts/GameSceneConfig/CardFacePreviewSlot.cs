using UnityEngine;
using UnityEngine.UI;

/// <summary>场景里画好的单张牌面预览槽。背景在根 Image 上，花纹叠在 FaceOverlay 上。</summary>
public class CardFacePreviewSlot : MonoBehaviour {
    public int tileId;
    public Image image;
    [SerializeField] private Image overlay;

    public void Apply(Sprite sprite, bool dimMissingCustom) {
        Apply(sprite, null, dimMissingCustom);
    }

    public void Apply(Sprite sprite, Sprite background, bool dimMissingCustom) {
        image.preserveAspect = true;
        Color tint = dimMissingCustom ? new Color(1f, 1f, 1f, 0.45f) : Color.white;
        bool layered = background != null && sprite != null;
        if (layered) {
            image.sprite = background;
            image.color = tint;
            overlay.sprite = sprite;
            overlay.color = tint;
            overlay.preserveAspect = true;
            overlay.enabled = true;
            overlay.gameObject.SetActive(true);
            return;
        }
        overlay.enabled = false;
        overlay.gameObject.SetActive(false);
        if (sprite != null) {
            image.sprite = sprite;
            image.color = tint;
        } else {
            image.sprite = null;
            image.color = new Color(0.3f, 0.3f, 0.3f, 1f);
        }
    }
}
