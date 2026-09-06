using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 结算呈现轮子：把 <see cref="SettlementEnvelope"/> 落到镜像分数、计分板历史与结算面板/倒牌动画。
///
/// 无流程决策：走哪条呈现路径（和牌序列 / 流局字幕 / 荒牌流局罚符 / 川麻终局子步骤）由族 GameState 决定后
/// 调用这里的具名方法；本类只保证"怎么呈现"与"分数/历史怎么记"一致。
/// </summary>
public sealed class SettlementPresenter {
    public static SettlementPresenter Current { get; private set; } = new SettlementPresenter();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() {
        Current = new SettlementPresenter();
    }

    private static GameSession Session => GameSession.Current;
    private static TableMirror Mirror => TableMirror.Current;

    // ---- 开场 ----

    /// <summary>
    /// 每条结算的公共开场：结算面板进入本轮生命周期（先于倒牌/渐显动画，确保不漏动画期间到达的准备广播）。
    /// </summary>
    public void BeginLifecycle(SettlementEnvelope env) {
        if (EndResultPanel.Instance != null) {
            EndResultPanel.Instance.SetNextStatus(env.NextStatus);
            EndResultPanel.Instance.BeginGameResultLifecycle();
        }
    }

    /// <summary>关闭本家询问并收起所有听牌提示。</summary>
    public void CloseTableForSettlement() {
        TurnClock.Current.Clear("ShowResult");
        TipsBlock.Instance.HideTipsBlock();
        TipsContainer.Instance.HideTips();
        TipsContainer.Instance.HideRyuukyokuTenpaiChoice();
    }

    // ---- 分数 ----

    /// <summary>把结算后分数写入镜像并刷新计分盘。</summary>
    public void ApplyScores(Dictionary<int, int> scoresAfter) {
        if (scoresAfter == null) return;
        foreach (KeyValuePair<int, string> kvp in Mirror.IndexToPosition) {
            if (scoresAfter.TryGetValue(kvp.Key, out int score) && Mirror.PlayerToInfo.TryGetValue(kvp.Value, out PlayerInfoClass info)) {
                info.score = score;
            }
        }
        BoardCanvas.Instance.UpdatePlayerScores(scoresAfter, Mirror.IndexToPosition);
    }

    /// <summary>即时分变（杠分/退杠分）：累加到镜像并刷新计分盘。</summary>
    public void ApplyScoreDeltas(Dictionary<int, int> changes) {
        if (changes == null || changes.Count == 0) return;
        var scoreBySeat = new Dictionary<int, int>();
        foreach (KeyValuePair<int, string> kvp in Mirror.IndexToPosition) {
            if (!Mirror.PlayerToInfo.TryGetValue(kvp.Value, out PlayerInfoClass info)) continue;
            int delta = changes.TryGetValue(kvp.Key, out int d) ? d : 0;
            info.score += delta;
            scoreBySeat[kvp.Key] = info.score;
        }
        if (scoreBySeat.Count > 0) {
            BoardCanvas.Instance.UpdatePlayerScores(scoreBySeat, Mirror.IndexToPosition);
        }
    }

    // ---- 计分板历史 ----

    /// <summary>
    /// 追加一条计分板结算快照并同步各家本地分数/历史列（通用路径；川麻简化计分板由族自行追加）。
    /// scoreChanges 缺省时依次回退：族 extras 里的分变 → 按 ScoresAfter 与镜像现分推算。
    /// </summary>
    public void AppendScoreboard(SettlementEnvelope env, Dictionary<int, int> extrasScoreChanges = null) {
        string winnerUsername = "";
        if (env.WinnerIndex >= 0 && Mirror.IndexToPosition.TryGetValue(env.WinnerIndex, out string huPos)
            && Mirror.PlayerToInfo.TryGetValue(huPos, out PlayerInfoClass winnerInfo)) {
            winnerUsername = winnerInfo.username;
        }

        Dictionary<int, int> scoreChanges = env.ScoreChanges;
        if (scoreChanges == null || scoreChanges.Count == 0) {
            scoreChanges = extrasScoreChanges;
        }
        if (scoreChanges == null || scoreChanges.Count == 0) {
            scoreChanges = BuildScoreChangesFromScoresAfter(env.ScoresAfter);
        }

        RoundSettlementSnapshot snapshot = ScoreHistorySettlementHelper.CreateFromShowResult(
            Session.SubRule, env.HuClass, env.WinnerIndex, winnerUsername, env.HuScore, env.FanLabels,
            env.WinnerHand, env.WinnerMelds, env.BaseFu, env.FuFanList,
            env.ExtrasAs<RiichiEndResultExtras>(), scoreChanges);
        Mirror.RoundSettlementHistory.Add(snapshot);
        ApplyLocalScoreHistory(snapshot, scoreChanges);
        GameSceneUIManager.Instance.UpdateScoreRecord();
    }

    /// <summary>用已建好的快照追加计分板（数和尾等由族自行构造快照的场景）。</summary>
    public void AppendScoreboard(RoundSettlementSnapshot snapshot, Dictionary<int, int> scoreChanges) {
        Mirror.RoundSettlementHistory.Add(snapshot);
        ApplyLocalScoreHistory(snapshot, scoreChanges);
        GameSceneUIManager.Instance.UpdateScoreRecord();
    }

    public Dictionary<int, int> BuildScoreChangesFromScoresAfter(Dictionary<int, int> scoresAfter) {
        if (scoresAfter == null) return null;
        var changes = new Dictionary<int, int>();
        foreach (KeyValuePair<int, string> kvp in Mirror.IndexToPosition) {
            if (!Mirror.PlayerToInfo.TryGetValue(kvp.Value, out PlayerInfoClass info)) continue;
            if (ShowResultPlayerScoreResolver.TryGetAfterScore(scoresAfter, kvp.Key, info.original_player_index, out int afterScore)) {
                changes[kvp.Key] = afterScore - info.score;
            }
        }
        return changes;
    }

    /// <summary>按分变更新各家镜像分数与 score_history / round_number_history 两列。</summary>
    public void ApplyLocalScoreHistory(RoundSettlementSnapshot snapshot, Dictionary<int, int> scoreChanges) {
        if (scoreChanges == null || scoreChanges.Count == 0) {
            if (!snapshot.isLiuju) return;
            scoreChanges = new Dictionary<int, int>();
            foreach (KeyValuePair<int, string> kvp in Mirror.IndexToPosition) {
                scoreChanges[kvp.Key] = 0;
            }
        }

        foreach (KeyValuePair<int, string> kvp in Mirror.IndexToPosition) {
            if (!Mirror.PlayerToInfo.TryGetValue(kvp.Value, out PlayerInfoClass info)) continue;
            int delta = 0;
            if (ShowResultPlayerScoreResolver.TryGetDelta(scoreChanges, kvp.Key, info.original_player_index, out int resolvedDelta)) {
                delta = resolvedDelta;
            }
            info.score += delta;
            info.score_history ??= new List<string>();
            info.score_history.Add(FormatLocalScoreChange(delta));
        }
        foreach (PlayerInfoClass info in Mirror.PlayerToInfo.Values) {
            info.round_number_history ??= new List<int>();
            info.round_number_history.Add(Mirror.CurrentRound);
            ScoreHistorySettlementHelper.AlignRoundNumberHistory(info.score_history, info.round_number_history);
        }
    }

    private static string FormatLocalScoreChange(int delta) {
        if (delta > 0) return "+" + delta;
        if (delta < 0) return delta.ToString();
        return "0";
    }

    // ---- 呈现 ----

    /// <summary>标准和牌序列：倒牌 → 渐显 → 番数面板 → 确认/准备。</summary>
    public void PresentHu(SettlementEnvelope env) {
        bool recycleDiscard = env.RecycleDiscard ?? (env.DeferScoreSettlement && env.IsHu && env.HuClass != "hu_self" && !env.MultiRon);
        RoundEndPresentation.Instance.PresentHuResultSequence(
            env.WinnerIndex, env.ScoresAfter, env.HuScore, env.FanLabels, env.HuClass,
            env.WinnerHand, env.WinnerFlowers, env.WinnerMelds,
            env.BaseFu, env.FuFanList, env.ExtrasAs<RiichiEndResultExtras>(), env.ScoreChanges, env.Silent,
            playPresentationEffects: true,
            suppressHandReveal: env.SuppressHandReveal,
            hepaiTile: env.WinTile,
            multiRon: env.MultiRon,
            deferScoreSettlement: env.DeferScoreSettlement,
            ronDiscarderIndex: env.RonDiscarderIndex,
            recycleDiscard: recycleDiscard,
            isQianggang: env.IsQianggang,
            endgameScoreOnly: false,
            // 多家和中间结算（round_continue）：面板自动关闭、不出确认按钮；
            // 最后一家（round_end_by_ready / match_end）才进入确认/准备。
            finalPanel: env.FinalPanel,
            simultaneousHuHands: env.SimultaneousHuHands,
            skipHandReveal: env.SkipHandReveal);
    }

    /// <summary>流局字幕。</summary>
    public void PresentLiuju(string caption) {
        RoundEndPresentation.Instance.PresentLiuju(caption);
    }

    /// <summary>荒牌流局：亮听牌手 + 罚符面板。</summary>
    public void PresentDrawWall(string caption, SettlementEnvelope env) {
        ApplyScores(env.ScoresAfter);
        RoundEndPresentation.Instance.PresentDrawWallLiujuAndPenalty(caption, env.ScoresAfter, env.ExtrasAs<RiichiEndResultExtras>());
    }

    /// <summary>起手胡：只刷分数、动作字与飘分，不进结算面板。</summary>
    public void PresentInitialHu(SettlementEnvelope env) {
        ApplyScores(env.ScoresAfter);
        if (!env.Silent && Mirror.IndexToPosition.TryGetValue(env.WinnerIndex, out string huPos)) {
            GameCanvas.Instance.ShowActionDisplay(huPos, "initial_hu", Session.RoomRule);
        }
        if (GameCanvas.HasNonZeroGangScoreChanges(env.ScoreChanges)) {
            GameCanvas.Instance.ShowGangScoreFloats(env.ScoreChanges, 0f);
        }
    }
}
