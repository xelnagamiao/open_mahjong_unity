using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 四川麻将（血战到底）客户端状态机扩展。
///
/// 设计要点：血战续打与国标「错和续打」结构一致——某家和牌后本盘并不结束，服务端继续驱动
/// 其余玩家行牌。客户端只需在收到带 round_continues=true 的结算后挂起，待下一次询问/标签刷新
/// 时关闭结算层、恢复自家操作即可（行牌次序、跳过已和牌者均由服务端控制，客户端被动跟随）。
///
/// 与错和的唯一区别：和牌者不再被 ask 行牌；3D 手牌区保留（仅和牌张移入补花区），终局 reveal_hu 再亮完整手牌。
/// 被自摸/点炮的扣分在终局查牌阶段按和牌顺序统一结算；中途和牌仅补花区标记 + 和牌顺序 tag。
/// </summary>
public partial class NormalGameStateManager {
    // 血战到底：收到 round_continues=true 的和牌结算后挂起，待下次询问时恢复行牌
    public bool pendingSichuanContinueAfterResult;

    /// <summary>血战终局：合并 reveal_hu / settle_hu / chajiao 分步结算，终局只写一行计分板。</summary>
    private Dictionary<int, int> _sichuanEndgameScoreAccum;
    private int _sichuanEndgameHuCount;
    private bool _sichuanEndgameHadChajiao;

    public void ResetSichuanEndgameScoreAccum() {
        _sichuanEndgameScoreAccum = null;
        _sichuanEndgameHuCount = 0;
        _sichuanEndgameHadChajiao = false;
    }

    public void BeginSichuanEndgameScoreAccum() {
        _sichuanEndgameScoreAccum = new Dictionary<int, int>();
        _sichuanEndgameHuCount = 0;
        _sichuanEndgameHadChajiao = false;
    }

    public void AccumulateSichuanEndgameScore(Dictionary<int, int> deltas) {
        if (deltas == null || deltas.Count == 0) return;
        _sichuanEndgameScoreAccum ??= new Dictionary<int, int>();
        foreach (var kvp in deltas) {
            if (!_sichuanEndgameScoreAccum.ContainsKey(kvp.Key)) {
                _sichuanEndgameScoreAccum[kvp.Key] = 0;
            }
            _sichuanEndgameScoreAccum[kvp.Key] += kvp.Value;
        }
    }

    public void RecordSichuanEndgameHu() {
        _sichuanEndgameScoreAccum ??= new Dictionary<int, int>();
        _sichuanEndgameHuCount++;
    }

    public void MarkSichuanEndgameChajiaoStep() {
        _sichuanEndgameHadChajiao = true;
    }

    /// <summary>终局末步：将累积的分差写入计分板（一局一行，主番仅 三家和/查叫/流局）。</summary>
    public bool TryFlushSichuanEndgameScoreToHistory() {
        if (_sichuanEndgameScoreAccum == null) return false;
        var merged = new Dictionary<int, int>(_sichuanEndgameScoreAccum);
        string roundLabel = ScoreHistorySettlementHelper.ResolveSichuanEndgameRoundLabel(
            _sichuanEndgameHuCount, _sichuanEndgameHadChajiao);
        RoundSettlementSnapshot snap = ScoreHistorySettlementHelper.CreateSichuanScoreboardSnapshot(subRule, roundLabel);
        roundSettlementHistory.Add(snap);
        ApplyLocalScoreHistoryFromSettlement(snap, merged);
        ResetSichuanEndgameScoreAccum();
        return true;
    }

    public bool IsSichuanEndgameScoreStep(string liujuStep) {
        return liujuStep == "reveal_hu" || liujuStep == "settle_hu" || liujuStep == "chajiao";
    }

    /// <summary>非血战终局或即时和牌：四川计分板不写番种/手牌，主番留空。</summary>
    public void AppendSichuanSimpleScoreboardSnapshot(string huClass, Dictionary<int, int> scoreChanges) {
        string roundLabel = huClass == "liuju" ? SichuanRoundLabel.Liuju : null;
        RoundSettlementSnapshot snap = ScoreHistorySettlementHelper.CreateSichuanScoreboardSnapshot(subRule, roundLabel);
        roundSettlementHistory.Add(snap);
        ApplyLocalScoreHistoryFromSettlement(snap, scoreChanges);
    }

    // 四川流局：由 HandleShowResult 在调用 ShowResult 前写入，供流局演出按未和家逐个显示状态
    public Dictionary<int, string> sichuanLiujuStatus;
    public Dictionary<int, int[]> sichuanLiujuHands;

    // 四川：自家定缺花色（1万/2饼/3条，0未定）。供切牌听牌提示过滤定缺花色和牌张。
    public int selfDingqueSuit;

    /// <summary>定缺完成/重连时写入自家定缺花色，供本地 tips 过滤。</summary>
    public void SetSelfDingqueFromMap(Dictionary<int, int> playerToDingque) {
        if (playerToDingque == null) return;
        if (playerToDingque.TryGetValue(selfIndex, out int suit)) {
            selfDingqueSuit = suit;
        }
    }

    public void SetSichuanLiujuExtras(Dictionary<int, string> status, Dictionary<int, int[]> hands) {
        sichuanLiujuStatus = status;
        sichuanLiujuHands = hands;
    }

    public bool IsSichuanRule() {
        if (roomRule == "sichuan") return true;
        return !string.IsNullOrEmpty(subRule) && subRule.StartsWith("sichuan");
    }

    /// <summary>自家手牌是否仍含定缺花色（与服务端 has_dingque_in_hand 一致）。</summary>
    public bool SelfHasDingqueTileInHand() {
        if (selfDingqueSuit < 1 || selfDingqueSuit > 3 || selfHandTiles == null) return false;
        for (int i = 0; i < selfHandTiles.Count; i++) {
            if (selfHandTiles[i] / 10 == selfDingqueSuit) return true;
        }
        return false;
    }

    /// <summary>四川：手牌仍有定缺花色时须优先打出（类比日麻立直锁手仅可打摸入牌）。</summary>
    public bool MustCutDingqueFirst() {
        if (!IsSichuanRule() || selfDingqueSuit < 1 || selfDingqueSuit > 3) return false;
        if (GameCanvas.Instance != null) {
            return GameCanvas.Instance.SelfHandHasDingqueSuitTile(selfDingqueSuit);
        }
        return SelfHasDingqueTileInHand();
    }

    public bool IsDingqueSuitTile(int tileId) {
        return selfDingqueSuit >= 1 && selfDingqueSuit <= 3 && tileId / 10 == selfDingqueSuit;
    }

    /// <summary>血战到底：标记本盘和牌后继续，等待服务端下一次行牌询问。</summary>
    public void MarkPendingSichuanContinue() {
        if (!IsSichuanRule()) return;
        pendingSichuanContinueAfterResult = true;
    }

    public void ClearPendingSichuanContinue() {
        pendingSichuanContinueAfterResult = false;
    }

    /// <summary>点炮和牌：河牌对象被移入和牌者补花区后，同步本地弃牌列表。</summary>
    public void SyncRonDiscardRemoved(string discarderPos, int tileId) {
        if (string.IsNullOrEmpty(discarderPos) || !player_to_info.TryGetValue(discarderPos, out PlayerInfoClass info)) return;
        if (info.discard_tiles == null || info.discard_tiles.Count == 0) return;
        if (tileId >= 10) {
            int idx = info.discard_tiles.LastIndexOf(tileId);
            if (idx >= 0) {
                info.discard_tiles.RemoveAt(idx);
            } else {
                info.discard_tiles.RemoveAt(info.discard_tiles.Count - 1);
            }
        } else {
            info.discard_tiles.RemoveAt(info.discard_tiles.Count - 1);
        }
        if (info.discard_riichi_flags != null && info.discard_riichi_flags.Count > 0) {
            info.discard_riichi_flags.RemoveAt(info.discard_riichi_flags.Count - 1);
        }
    }

    public string ResolveRonDiscarderPosition(int? ronDiscarderIndex) {
        if (ronDiscarderIndex.HasValue && indexToPosition.TryGetValue(ronDiscarderIndex.Value, out string pos)) {
            return pos;
        }
        return lastDiscardPlayerPosition;
    }

    /// <summary>四川：新一局开始时重置自家定缺。</summary>
    public void ResetSelfDingque() {
        selfDingqueSuit = 0;
    }

    /// <summary>
    /// 血战续打恢复：关闭结算演出与和牌面板，恢复自家操作并重同步 3D 手牌。
    /// 不还原和牌者手牌（和牌者已退场）。在 AskHandAction/AskMingPaiAction/RefreshPlayerTagList 中调用。
    /// </summary>
    public void TryResumeAfterSichuanContinue() {
        if (!pendingSichuanContinueAfterResult) return;
        // 和牌面板仍在倒计时展示时，等面板自行结束后再恢复行牌
        if (EndResultPanel.Instance != null && EndResultPanel.Instance.gameObject.activeSelf) {
            return;
        }
        pendingSichuanContinueAfterResult = false;

        if (RoundEndPresentation.Instance != null) {
            RoundEndPresentation.Instance.StopActiveSequence();
            RoundEndPresentation.Instance.ResetSichuanEndgameQueue();
        }
        if (EndResultPanel.Instance != null) {
            EndResultPanel.Instance.ClearEndResultPanel();
        }
        RoundEndPresentation.Instance?.ShowSelfGameplayControlAndResyncHand3D();
    }

    /// <summary>四川：重连/进入局中时，从 game_info 的 players_info.dingque_suit 恢复各家定缺标记。</summary>
    private void RestoreSichuanDingque(GameInfo gameInfo) {
        if (!IsSichuanRule() || gameInfo?.players_info == null) return;
        var map = new Dictionary<int, int>();
        foreach (PlayerInfo p in gameInfo.players_info) {
            if (p == null) continue;
            map[p.player_index] = p.dingque_suit;
        }
        if (map.Count > 0) {
            GameCanvas.Instance?.UpdatePlayerDingque(map);
            SetSelfDingqueFromMap(map);
        }
    }
}
