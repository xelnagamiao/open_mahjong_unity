using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 轮数面板总控制器：根据 room_rule 决定显示 SimpleRoundPanel（古典/国标/青雀）
/// 或 RiichiRoundPanel（立直麻将匹配房）。
/// </summary>
public class RoundPanel : MonoBehaviour {
    public static RoundPanel Instance { get; private set; }

    [Header("子面板引用")]
    [SerializeField] private SimpleRoundPanel simpleRoundPanel;
    [SerializeField] private RiichiRoundPanel riichiRoundPanel;

    private GameInfo _cachedGameInfo;
    private string _cachedRoomRule;

    private void Awake() {
        if (Instance != null && Instance != this) {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    /// <summary>
    /// 初始化轮数面板：根据房间规则切换 Simple / Riichi。
    /// </summary>
    public void UpdateRoomInfo(GameInfo gameInfo, string roomRule) {
        _cachedGameInfo = gameInfo;
        _cachedRoomRule = roomRule;

        bool useRiichi = IsRiichiLayout(gameInfo, roomRule);
        if (simpleRoundPanel != null) simpleRoundPanel.gameObject.SetActive(!useRiichi);
        if (riichiRoundPanel != null) riichiRoundPanel.gameObject.SetActive(useRiichi);

        if (useRiichi) {
            riichiRoundPanel?.UpdateRoomInfo(gameInfo, roomRule);
        } else {
            simpleRoundPanel?.UpdateRoomInfo(gameInfo, roomRule);
        }
    }

    /// <summary>
    /// 立直房间的场况刷新（本场、立直棒、宝牌指示牌）。
    /// 杠宝牌指示追加到 doraIndicators 末尾即可。
    /// </summary>
    public void RefreshRiichi(int honba, int riichiSticks, List<int> doraIndicators, List<int> kanDoraIndicators) {
        if (riichiRoundPanel == null || !riichiRoundPanel.gameObject.activeSelf) return;
        var merged = doraIndicators != null ? new List<int>(doraIndicators) : new List<int>();
        if (kanDoraIndicators != null) merged.AddRange(kanDoraIndicators);
        riichiRoundPanel.RefreshFieldState(honba, riichiSticks, merged);
    }

    private static bool IsRiichiLayout(GameInfo gameInfo, string roomRule) {
        if (!string.IsNullOrEmpty(roomRule) && (roomRule == "riichi" || roomRule.StartsWith("riichi/"))) return true;
        if (gameInfo != null) {
            if (!string.IsNullOrEmpty(gameInfo.sub_rule) && gameInfo.sub_rule.StartsWith("riichi")) return true;
            if (gameInfo.room_rule == "riichi") return true;
        }
        return false;
    }
}
