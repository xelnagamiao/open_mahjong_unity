using TMPro;
using UnityEngine;
using UnityEngine.Serialization;

/// <summary>所有公开翻牌共用场景内的五个牌槽，依照服务器的揭示顺序从左向右展示。</summary>
[AddComponentMenu("麻将/有翻牌的左上角板")]
public sealed class IndicatorRoundPanel : MonoBehaviour {
    [Header("房间信息")]
    [SerializeField] private TMP_Text ruleText;
    [SerializeField] private TMP_Text GameRoundText;
    [SerializeField] private TMP_Text roomNowRoundText;

    [Header("本场与供托")]
    [SerializeField] private TMP_Text riichiSticksCountText;
    [SerializeField] private TMP_Text honbaCountText;
    [SerializeField] private GameObject[] stickDecorations;

    [Header("从左至右排列的翻牌槽")]
    [FormerlySerializedAs("doraSlots")]
    [SerializeField] private StaticCard[] indicatorSlots;

    private CanvasGroup[] slotVisibility;

    public void UpdateRoomInfo(GameInfo gameInfo, string roomType, RuleManifest manifest) {
        if (ruleText) {
            ruleText.enableAutoSizing = true;
            ruleText.fontSizeMin = 20;
            ruleText.fontSizeMax = 26;
            ruleText.textWrappingMode = TextWrappingModes.NoWrap;
            ruleText.overflowMode = TextOverflowModes.Ellipsis;
            string nameKey = manifest.DefaultSubRule == gameInfo.sub_rule && manifest != RuleRegistry.Resolve(roomType, roomType)
                ? gameInfo.sub_rule : roomType;
            TableText.Set(ruleText, RuleNameDictionary.GetWholeName(nameKey));
            ruleText.color = Color.black;
        }
        if (GameRoundText)
            TableText.Set(GameRoundText, manifest.MaxRoundInfoText?.Invoke(gameInfo)
                ?? RoundTextDictionary.GetMaxRoundText(roomType, gameInfo.max_round));
        if (roomNowRoundText)
            TableText.Set(roomNowRoundText, manifest.RoundStatusText?.Invoke(gameInfo)
                ?? RoundTextDictionary.GetRoundName(roomType, gameInfo.current_round));
    }

    public void RefreshIndicators(RoundIndicatorDisplay display) {
        bool showSticks = display.Honba.HasValue || display.RiichiSticks.HasValue;
        if (honbaCountText) {
            honbaCountText.gameObject.SetActive(showSticks);
            honbaCountText.text = display.Honba.GetValueOrDefault().ToString();
        }
        if (riichiSticksCountText) {
            riichiSticksCountText.gameObject.SetActive(showSticks);
            riichiSticksCountText.text = display.RiichiSticks.GetValueOrDefault().ToString();
        }
        if (stickDecorations != null)
            foreach (var decoration in stickDecorations)
                if (decoration) decoration.SetActive(showSticks);

        if (indicatorSlots == null || indicatorSlots.Length == 0) return;
        slotVisibility ??= new CanvasGroup[indicatorSlots.Length];
        for (int i = 0; i < indicatorSlots.Length; i++) {
            var slot = indicatorSlots[i];
            if (!slot) continue;
            var group = slotVisibility[i];
            if (!group) {
                if (!slot.TryGetComponent(out group)) group = slot.gameObject.AddComponent<CanvasGroup>();
                slotVisibility[i] = group;
            }
            // 保留未使用槽的布局宽度。只显示两张时，前两张仍与五张时处于同一位置。
            bool visible = i < display.Tiles.Count;
            group.alpha = visible ? 1 : 0;
            group.blocksRaycasts = visible;
            int tile = visible ? display.Tiles[i].TileId : 0;
            string role = tile > 0 ? display.Tiles[i].Role : null;
            RuleTileBadge.SetIndicatorRole(slot, role);
            slot.SetTileOnlyImage(tile);
        }
        if (indicatorSlots[0]) TileFaceFit.FitRowHeight(indicatorSlots[0].transform.parent);
    }
}
