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

    public void UpdateRoomInfo(GameInfo gameInfo, string roomType) {
        if (ruleText != null) ruleText.text = RuleNameDictionary.GetWholeName(roomType);

        if (GameRoundText != null) {
            if (gameInfo.max_round == 1) GameRoundText.text = "东风战";
            else if (gameInfo.max_round == 2) GameRoundText.text = "东南战";
            else if (gameInfo.max_round == 3) GameRoundText.text = "西风战";
            else if (gameInfo.max_round == 4) GameRoundText.text = "全庄战";
            else GameRoundText.text = "未知轮数";
        }

        if (roomNowRoundText != null) {
            var map = RoundTextDictionary.CurrentRoundTextRiichi;
            if (map != null && map.TryGetValue(gameInfo.current_round, out string roundText)) {
                roomNowRoundText.text = roundText;
            } else {
                roomNowRoundText.text = "未知轮数";
            }
        }

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
        if (honbaCountText != null) honbaCountText.text = honba.ToString();
        if (riichiSticksCountText != null) riichiSticksCountText.text = riichiSticks.ToString();

        if (doraSlots == null) return;
        for (int i = 0; i < doraSlots.Length; i++) {
            if (doraSlots[i] == null) continue;
            int tileId = (doraIndicators != null && i < doraIndicators.Count) ? doraIndicators[i] : TileBackId;
            doraSlots[i].SetTileOnlyImage(tileId);
        }
    }
}
