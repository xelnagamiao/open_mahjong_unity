using System.Collections.Generic;
using TMPro;
using UnityEngine;

/// <summary>
/// 立直麻将专用轮数面板。
/// 除常规规则/圈局外，展示：
/// - 本场棒数（honba）/ 场供立直棒数（riichi sticks）
/// - 宝牌指示槽位（固定 N 个，N 由 inspector 拖入的 StaticCard 数量决定；未翻开的位置显示牌背 0）
/// 承诺值 / 盐值由 RoundPanel 统管，点击 RoundPanel 区域后另行展示。
/// </summary>
public class RiichiRoundPanel : MonoBehaviour {
    [Header("房间信息显示")]
    [SerializeField] private TMP_Text ruleText;
    [SerializeField] private TMP_Text GameRoundText;
    [SerializeField] private TMP_Text roomNowRoundText;

    [Header("场况显示：数量")]
    [Tooltip("场供立直棒（1000点棒）数量")]
    [SerializeField] private TMP_Text riichiSticksCountText;
    [Tooltip("本场（300点棒/100点棒）数量")]
    [SerializeField] private TMP_Text honbaCountText;

    [Header("场况显示：宝牌指示槽位（手动拖入 StaticCard）")]
    [Tooltip("从左到右依序对应 1 张 dora 指示牌 + 最多 4 张杠宝牌。未翻开槽位固定显示牌背 0")]
    [SerializeField] private StaticCard[] doraSlots;

    private const int TileBackId = 0;
    private TMP_Text indicatorCaption, indicatorCounter;
    private UnityEngine.UI.HorizontalLayoutGroup indicatorRow;
    private TextAnchor originalIndicatorAlignment;
    private readonly List<GameObject> counterDecorations = new List<GameObject>();
    private bool indicatorLayoutInitialized;

    public void UpdateRoomInfo(GameInfo gameInfo, string roomType) {
        if (ruleText != null) {
            ruleText.enableAutoSizing = true;
            ruleText.fontSizeMin = 16f;
            ruleText.fontSizeMax = 28f;
            ruleText.textWrappingMode = TextWrappingModes.NoWrap;
            ruleText.overflowMode = TextOverflowModes.Ellipsis;
            ruleText.text = RuleNameDictionary.GetWholeName(roomType);
        }

        if (GameRoundText != null) {
            GameRoundText.text = RoundTextDictionary.GetMaxRoundText(roomType, gameInfo.max_round);
        }

        if (roomNowRoundText != null) {
            roomNowRoundText.text = RoundTextDictionary.GetRoundName(roomType, gameInfo.current_round);
        }

        var manifest = RuleRegistry.Resolve(roomType, roomType)
            ?? RuleRegistry.Resolve(gameInfo.room_rule, gameInfo.sub_rule);
        if (manifest?.RoundIndicators != null) {
            RefreshRuleIndicators(manifest.RoundIndicators(gameInfo));
            return;
        }
        SetIndicatorLayout(false);

        int honba = gameInfo.honba ?? 0;
        int sticks = gameInfo.riichi_sticks ?? 0;
        var indicators = gameInfo.dora_indicators != null
            ? new List<int>(gameInfo.dora_indicators)
            : new List<int>();
        if (gameInfo.kan_dora_indicators != null) {
            foreach (int t in gameInfo.kan_dora_indicators) indicators.Add(t);
        }
        RefreshFieldState(honba, sticks, indicators);
    }

    /// <summary>
    /// 更新场况显示。传入的 doraIndicators 已合并了初始宝牌与已翻开的杠宝牌。
    /// </summary>
    public void RefreshFieldState(int honba, int riichiSticks, List<int> doraIndicators) {
        SetIndicatorLayout(false);
        if (honbaCountText != null) honbaCountText.text = honba.ToString();
        if (riichiSticksCountText != null) riichiSticksCountText.text = riichiSticks.ToString();

        if (doraSlots == null) return;
        for (int i = 0; i < doraSlots.Length; i++) {
            if (doraSlots[i] == null) continue;
            doraSlots[i].gameObject.SetActive(true);
            int tileId = (doraIndicators != null && i < doraIndicators.Count) ? doraIndicators[i] : TileBackId;
            doraSlots[i].SetTileOnlyImage(tileId);
            TileFaceFit.FitRowHeight(doraSlots[i].transform.parent);
        }
    }

    /// <summary>在同一回合面板内显示翻出的牌，隐藏未使用的槽位与日麻点棒。</summary>
    public void RefreshRuleIndicators(RoundIndicatorDisplay display) {
        SetIndicatorLayout(true);
        if (indicatorCaption != null) indicatorCaption.text = display?.Caption ?? "";
        if (indicatorCounter != null) indicatorCounter.text = display?.Counter ?? "";
        if (doraSlots == null) return;
        for (int i = 0; i < doraSlots.Length; i++) {
            if (doraSlots[i] == null) continue;
            bool visible = display?.Tiles != null && i < display.Tiles.Length;
            doraSlots[i].gameObject.SetActive(visible);
            doraSlots[i].SetTileOnlyImage(visible ? display.Tiles[i] : TileBackId);
        }
        if (doraSlots.Length > 0 && doraSlots[0] != null)
            TileFaceFit.FitRowHeight(doraSlots[0].transform.parent);
    }

    private void SetIndicatorLayout(bool custom) {
        if (!indicatorLayoutInitialized) {
            indicatorLayoutInitialized = true;
            if (doraSlots != null && doraSlots.Length > 0 && doraSlots[0] != null) {
                indicatorRow = doraSlots[0].transform.parent.GetComponent<UnityEngine.UI.HorizontalLayoutGroup>();
                if (indicatorRow != null) originalIndicatorAlignment = indicatorRow.childAlignment;
            }
            // These are the two stick images and multiplication signs in the existing scene.
            // The counts have explicit references; keep all of them when returning to riichi.
            if (honbaCountText != null) {
                foreach (Transform child in honbaCountText.transform.parent)
                    if (child.name == "100image" || child.name == "1000image" || child.name == "x")
                        counterDecorations.Add(child.gameObject);
            }
        }
        if (custom && indicatorCaption == null && honbaCountText != null) {
            indicatorCaption = CreateIndicatorLabel("IndicatorCaption", new Vector2(85, -8), new Vector2(185, 67));
            indicatorCounter = CreateIndicatorLabel("IndicatorCounter", new Vector2(125, -78), new Vector2(155, 30));
        }
        if (indicatorCaption != null) indicatorCaption.gameObject.SetActive(custom);
        if (indicatorCounter != null) indicatorCounter.gameObject.SetActive(custom);
        if (honbaCountText != null) honbaCountText.gameObject.SetActive(!custom);
        if (riichiSticksCountText != null) riichiSticksCountText.gameObject.SetActive(!custom);
        foreach (var decoration in counterDecorations) decoration.SetActive(!custom);
        if (indicatorRow != null) indicatorRow.childAlignment = custom ? TextAnchor.MiddleLeft : originalIndicatorAlignment;
    }

    private TMP_Text CreateIndicatorLabel(string name, Vector2 position, Vector2 size) {
        var label = Instantiate(honbaCountText, honbaCountText.transform.parent);
        label.name = name;
        label.raycastTarget = false;
        label.alignment = TextAlignmentOptions.MidlineLeft;
        label.enableAutoSizing = true;
        label.fontSizeMin = 16f;
        label.fontSizeMax = 24f;
        label.textWrappingMode = TextWrappingModes.NoWrap;
        label.overflowMode = TextOverflowModes.Ellipsis;
        var rect = label.rectTransform;
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0, 1);
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
        return label;
    }
}
