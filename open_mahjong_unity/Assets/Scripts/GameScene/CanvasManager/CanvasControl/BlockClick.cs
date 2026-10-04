using UnityEngine;
using UnityEngine.EventSystems;

public class ActionBlock : MonoBehaviour, IPointerClickHandler {
    public const float TileWidth = 84f;
    public const float TileSpacing = 4f;
    public string actionType;
    public int targetTile;
    // 立直麻将涉赤 5 时吃牌候选索引，-1 表示非吃牌或无候选（走默认 0）
    public int chiComboIndex = -1;

    private void Awake() {
        TileFaceFit.FitRowHeight(transform);
    }

    // 普通候选、吃牌候选和分页候选共用尺寸，先设宽度再按当前牌背比例适配高度。
    public StaticCard AddTile(GameObject prefab, int tileId) {
        var card = Instantiate(prefab, transform).GetComponent<StaticCard>();
        ((RectTransform)card.transform).SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, TileWidth);
        card.SetTileOnlyImage(tileId);
        return card;
    }

    public void OnPointerClick(PointerEventData eventData) {
        Debug.Log($"选择了行动 {actionType} chiComboIndex={chiComboIndex}");
        GameCanvas.Instance.ChooseAction(actionType, targetTile, chiComboIndex);
    }
}
