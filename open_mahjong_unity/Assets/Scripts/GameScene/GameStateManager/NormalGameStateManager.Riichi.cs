using System.Collections.Generic;

public partial class NormalGameStateManager {
    /// <summary>
    /// 立直宣告广播处理：刷新玩家 tag_list、播放立直语音、立直棒从 outputPos 飞向 tenbouPos，
    /// 并按规则把场供立直棒 +1 同步到 RoundPanel；服务端的供托结算在 _commit_pending_riichi 与
    /// 和牌/抽水时处理，此处仅做客户端表现。
    /// </summary>
    public void OnRiichiDeclared(Dictionary<int, string[]> playerToTagList, int? riichiDeclaredPlayerIndex) {
        if (playerToTagList != null) {
            RefreshPlayerTagList(playerToTagList);
        }
        riichiSticks += 1;
        if (RoundPanel.Instance != null) {
            RoundPanel.Instance.RefreshRiichi(honba, riichiSticks, doraIndicators, kanDoraIndicators);
        }
        if (riichiDeclaredPlayerIndex.HasValue && indexToPosition.ContainsKey(riichiDeclaredPlayerIndex.Value)) {
            string pos = indexToPosition[riichiDeclaredPlayerIndex.Value];
            SoundManager.Instance.PlayRiichiVoice(pos);
            Game3DManager.Instance.PlayRiichiTenbouFlight(pos);
        }
    }

    /// <summary>
    /// 宝牌/杠宝牌翻开广播处理。
    /// </summary>
    public void OnDoraUpdated(GameInfo gameInfo) {
        if (gameInfo == null) return;
        OnDoraUpdated(gameInfo.dora_indicators, gameInfo.kan_dora_indicators);
    }

    public void OnDoraUpdated(int[] doraIndicatorsFromServer, int[] kanDoraIndicatorsFromServer) {
        if (doraIndicatorsFromServer != null) {
            doraIndicators = new List<int>(doraIndicatorsFromServer);
        }
        if (kanDoraIndicatorsFromServer != null) {
            kanDoraIndicators = new List<int>(kanDoraIndicatorsFromServer);
        }
        if (RoundPanel.Instance != null) {
            RoundPanel.Instance.RefreshRiichi(honba, riichiSticks, doraIndicators, kanDoraIndicators);
        }
    }

    public void OnRiichiSticksCollected(int collectedCount) {
        if (collectedCount <= 0) return;
        riichiSticks = 0;
        Game3DManager.Instance.ClearAllRiichiTenbous();
        if (RoundPanel.Instance != null) {
            RoundPanel.Instance.RefreshRiichi(honba, riichiSticks, doraIndicators, kanDoraIndicators);
        }
    }
}
