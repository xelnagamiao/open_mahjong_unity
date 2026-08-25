using UnityEngine;
using UnityEngine.UI;

/// <summary>场景里画好的单张牌面预览槽。背景在根 Image 上，花纹原样叠在子 Image 上。</summary>
public class CardFacePreviewSlot : MonoBehaviour {
    public int tileId;
    public Image image;
    [SerializeField] private Image overlay;

    public void Apply(Sprite sprite, bool dimMissingCustom) {
        Apply(sprite, null, dimMissingCustom);
    }

    public void Apply(Sprite sprite, Sprite background, bool dimMissingCustom) {
        if (image == null) return;
        image.preserveAspect = true;
        Color tint = dimMissingCustom ? new Color(1f, 1f, 1f, 0.45f) : Color.white;
        bool layered = background != null && sprite != null;
        if (layered) {
            EnsureOverlay();
            image.sprite = background;
            image.color = tint;
            overlay.sprite = sprite;
            overlay.color = tint;
            overlay.preserveAspect = true;
            overlay.enabled = true;
            overlay.gameObject.SetActive(true);
            return;
        }
        if (overlay != null) {
            overlay.enabled = false;
            overlay.gameObject.SetActive(false);
        }
        if (sprite != null) {
            image.sprite = sprite;
            image.color = tint;
        } else {
            image.sprite = null;
            image.color = new Color(0.3f, 0.3f, 0.3f, 1f);
        }
    }

    private void EnsureOverlay() {
        if (overlay != null) return;
        Transform existing = transform.Find("FaceOverlay");
        if (existing != null) {
            overlay = existing.GetComponent<Image>();
            if (overlay != null) return;
        }
        var go = new GameObject("FaceOverlay", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        RectTransform rt = (RectTransform)go.transform;
        rt.SetParent(transform, false);
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
        overlay = go.GetComponent<Image>();
        overlay.raycastTarget = false;
        overlay.preserveAspect = true;
    }
}
