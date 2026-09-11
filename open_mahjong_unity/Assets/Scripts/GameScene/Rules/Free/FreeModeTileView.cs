using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

/// <summary>
/// 自由模式工具面板里的牌张视图。牌体、花纹、选中框和说明由 prefab 提供，
/// 本组件只绑定内容；旋转与等比缩放只作用于 art，不改变列表单元格的尺寸。
/// </summary>
public sealed class FreeModeTileView : MonoBehaviour {
    public Image background;
    public Image face;
    public Image selection;
    public RectTransform art;
    public Text caption;
    public Button button;
    public Vector2 artworkBounds = new Vector2(92f, 82f);

    private const string FontResource = "font/Chinese/AlibabaPuHuiTi/AlibabaPuHuiTi-3-55-Regular";
    private static readonly Vector2 FaceInsetMin = new Vector2(0.075f, 0.035f);
    private static readonly Vector2 FaceInsetMax = new Vector2(0.925f, 0.905f);
    private static readonly string[] NumberNames = { "", "一", "二", "三", "四", "五", "六", "七", "八", "九" };
    private static readonly string[] HonorNames = { "东", "南", "西", "北", "中", "白", "发" };
    private static readonly string[] FlowerNames = { "春", "夏", "秋", "冬", "梅", "兰", "竹", "菊" };

    private int boundTileId = -1;
    private int boundOrient;
    private bool hasBinding;
    private UnityAction clickHandler;

    private void OnEnable() {
        TileFaceResolver.OnPackChanged += RefreshArtwork;
        if (hasBinding) RefreshArtwork();
    }

    private void OnDisable() {
        TileFaceResolver.OnPackChanged -= RefreshArtwork;
    }

    public void Bind(int tileId, int orient = 0, bool selected = false, string label = null, UnityAction onClick = null) {
        boundTileId = tileId;
        boundOrient = orient;
        hasBinding = true;
        if (selection != null) {
            selection.enabled = selected;
            selection.raycastTarget = false;
        }
        if (caption != null) {
            caption.text = label ?? (orient == 2 ? "背面" : TileLabel(tileId));
            caption.raycastTarget = false;
            caption.supportRichText = false;
            caption.horizontalOverflow = HorizontalWrapMode.Wrap;
            caption.verticalOverflow = VerticalWrapMode.Truncate;
            caption.resizeTextForBestFit = true;
            caption.resizeTextMinSize = 10;
            caption.resizeTextMaxSize = Mathf.Max(12, caption.fontSize);
            if (caption.font == null) caption.font = Resources.Load<Font>(FontResource);
        }
        if (button != null) {
            if (clickHandler != null) button.onClick.RemoveListener(clickHandler);
            clickHandler = onClick;
            if (clickHandler != null) button.onClick.AddListener(clickHandler);
            button.interactable = clickHandler != null;
            button.navigation = new Navigation { mode = Navigation.Mode.None };
        }
        RefreshArtwork();
    }

    public void RefreshArtwork() {
        if (!hasBinding || art == null) return;

        bool faceDown = boundOrient == 2 || boundTileId == ConfigManager.HandBackImageId;
        bool completeHandArt = faceDown || HongqueTileVisual.IsHongqueId(boundTileId);
        Sprite faceSprite = completeHandArt
            ? TileFaceResolver.LoadSprite(faceDown ? ConfigManager.HandBackImageId : boundTileId)
            : TileFaceResolver.LoadTableSprite(boundTileId);
        // 桌面花纹本身没有牌体。工具里的预览始终保留牌体，并复用设置里的 3D 背景。
        Sprite bodySprite = !completeHandArt && faceSprite != null
            ? TileFaceResolver.LoadTableBackground()
            : null;

        SetImage(background, bodySprite);
        SetImage(face, faceSprite);
        if (background != null) StretchImage(background.rectTransform, Vector2.zero, Vector2.one);
        if (face != null) {
            // table 图是 220×366 的不透明牌顶纹理，必须缩进牌体内侧，不能盖住立体边。
            StretchImage(face.rectTransform,
                bodySprite != null ? FaceInsetMin : Vector2.zero,
                bodySprite != null ? FaceInsetMax : Vector2.one);
        }

        Sprite fitSprite = bodySprite != null ? bodySprite : faceSprite;
        float aspect = fitSprite != null && fitSprite.rect.height > 0f
            ? fitSprite.rect.width / fitSprite.rect.height
            : 272f / 389f;
        bool horizontal = boundOrient == 1;
        float maxWidth = Mathf.Max(1f, horizontal ? artworkBounds.y : artworkBounds.x);
        float maxHeight = Mathf.Max(1f, horizontal ? artworkBounds.x : artworkBounds.y);
        float height = Mathf.Min(maxHeight, maxWidth / Mathf.Max(0.01f, aspect));
        art.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, height * aspect);
        art.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, height);
        art.localScale = Vector3.one;
        art.localRotation = Quaternion.Euler(0f, 0f, horizontal ? 90f : 0f);
    }

    private static void SetImage(Image image, Sprite sprite) {
        if (image == null) return;
        image.sprite = sprite;
        image.enabled = sprite != null;
        image.color = Color.white;
        image.type = Image.Type.Simple;
        image.preserveAspect = true;
        image.raycastTarget = false;
    }

    private static void StretchImage(RectTransform rect, Vector2 min, Vector2 max) {
        rect.anchorMin = min;
        rect.anchorMax = max;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
        rect.localRotation = Quaternion.identity;
        rect.localScale = Vector3.one;
    }

    private static string TileLabel(int tileId) {
        if (tileId < 0) return "空";
        if (tileId == ConfigManager.HandBackImageId) return "背面";
        if (tileId == ConfigManager.BlankFaceImageId) return "白板";
        if (tileId == 105) return "赤五万";
        if (tileId == 205) return "赤五筒";
        if (tileId == 305) return "赤五条";
        int suit = tileId / 10;
        int rank = tileId % 10;
        if (suit >= 1 && suit <= 3 && rank >= 1 && rank <= 9) {
            return NumberNames[rank] + (suit == 1 ? "万" : suit == 2 ? "筒" : "条");
        }
        if (suit == 4 && rank >= 1 && rank <= 7) return HonorNames[rank - 1];
        if (suit == 5 && rank >= 1 && rank <= 8) return FlowerNames[rank - 1];
        return HongqueTileVisual.ToCode(tileId) ?? tileId.ToString();
    }
}
