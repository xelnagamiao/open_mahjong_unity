using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

public class StaticCard : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler {
    [SerializeField] private Image tileImage;
    [SerializeField] private Image faceBackground;
    private Image tableBackground;

    private int tileId = -1;
    private bool useTableFace;
    private bool hasDangerTint;
    private bool hasZimoTint;
    private Color baseColor = Color.white;
    private float brightness = 1f;
    private static readonly Color DangerTintColor = new Color(1f, 0.65f, 0.65f, 1f);
    private static readonly Color ZimoTintColor = new Color(0.65f, 0.8f, 1f, 1f);

    public int TileId => tileId;

    private void OnEnable() {
        if (tileId >= 0) RefreshVisual();
    }

    public void SetTileOnlyImage(int tile) {
        SetTileImage(tile, false);
    }

    /// <summary>牌张设置显示完整 3D 牌面（底色、背景与花纹），保留原槽位与 Toggle 选中标记。</summary>
    public void SetTableFaceImage(int tile) {
        SetTileImage(tile, true);
    }

    private void SetTileImage(int tile, bool tableFace) {
        tileId = tile;
        useTableFace = tableFace;
        hasDangerTint = false;
        hasZimoTint = false;
        baseColor = Color.white;
        brightness = 1f;
        RefreshVisual();
    }

    /// <summary>换肤或重新显示时保留牌面类型、指定颜色、牌山明暗和铳牌、自摸提示。</summary>
    public void RefreshVisual() {
        if (!useTableFace && tableBackground != null) tableBackground.enabled = false;
        bool applied = useTableFace
            ? TileFaceFit.ApplyTableLayers(transform as RectTransform, tileImage, ref faceBackground, ref tableBackground, tileId)
            : TileFaceFit.ApplyHandLayers(transform as RectTransform, tileImage, ref faceBackground, tileId);
        if (!applied) {
            Debug.LogError($"找不到牌面图片: {tileId}");
            return;
        }
        ApplyDisplayColor();
        GuangdongMilTileVisual.Refresh(transform, tileId);
        RuleTileBadge.Apply(transform, tileId);
    }

    private void OnRectTransformDimensionsChange() {
        if (!useTableFace || tileImage == null || faceBackground == null) return;
        faceBackground.rectTransform.sizeDelta = ((RectTransform)transform).rect.size;
        TileFaceFit.FitTableArtwork(faceBackground.rectTransform, tileImage, tableBackground,
            TileFaceResolver.TableImageScaleFor(tileId));
    }

    public void SetTileImageColor(Color color) {
        baseColor = color;
        baseColor.a = Mathf.Clamp01(color.a);
        ApplyDisplayColor();
    }

    public void SetOpacity(float alpha) {
        baseColor.a = Mathf.Clamp01(alpha);
        ApplyDisplayColor();
    }

    public void SetDangerTint(bool on) {
        hasDangerTint = on;
        ApplyDisplayColor();
    }

    public void ClearDangerTint() {
        hasDangerTint = false;
        ApplyDisplayColor();
    }

    public void ClearWallTints() {
        hasDangerTint = false;
        hasZimoTint = false;
        ApplyDisplayColor();
    }

    /// <summary>已摸走的牌只降低 RGB 明度，牌体保持不透明，避免分层牌面透出桌布。</summary>
    public void ApplyWallVisual(float tileBrightness, bool dangerTint, bool zimoTint) {
        brightness = Mathf.Clamp01(tileBrightness);
        hasDangerTint = dangerTint;
        hasZimoTint = zimoTint;
        ApplyDisplayColor();
    }

    private void ApplyDisplayColor() {
        Color tint = Color.white;
        if (hasDangerTint) {
            tint = DangerTintColor;
        }
        else if (hasZimoTint) {
            tint = ZimoTintColor;
        }
        Color c = baseColor * tint;
        c.r *= brightness;
        c.g *= brightness;
        c.b *= brightness;
        if (tileImage != null) tileImage.color = c;
        if (faceBackground != null && faceBackground.enabled) {
            Color bodyColor = useTableFace ? TileFaceResolver.TablePreviewBaseColor : Color.white;
            bodyColor.a = 1f;
            faceBackground.color = bodyColor * c;
        }
        if (tableBackground != null && tableBackground.enabled) tableBackground.color = c;
    }

    public void OnPointerEnter(PointerEventData eventData) {
        if (tileId > 0) {
            Card3DHoverManager.Instance?.OnCardHover(tileId);
        }
    }
    public void OnPointerExit(PointerEventData eventData) {
        Card3DHoverManager.Instance?.OnCardExit();
    }
}
