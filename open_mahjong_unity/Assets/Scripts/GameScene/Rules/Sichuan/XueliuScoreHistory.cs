using System.Collections.Generic;

/// <summary>血流每局一行：以总分减去前局累计，覆盖本局分差，不再次修改玩家分数。</summary>
public static class XueliuScoreHistory {
    public static void Refresh(string subRule, int currentRound, IEnumerable<PlayerInfoClass> players,
        List<RoundSettlementSnapshot> snapshots, string roundLabel = null) {
        if (!SichuanLobby.IsXueliu(subRule)) return;
        List<int> roundNumbers = null;
        foreach (PlayerInfoClass player in players) {
            player.score_history ??= new List<string>();
            player.round_number_history ??= new List<int>();
            ScoreHistorySettlementHelper.AlignRoundNumberHistory(player.score_history, player.round_number_history);
            int row = player.score_history.Count - 1;
            if (row < 0 || player.round_number_history[row] != currentRound) {
                player.score_history.Add("0");
                player.round_number_history.Add(currentRound);
                row++;
            }
            // 血流比赛起分为 0。绝对分已包含和牌、杠分与退分；历史只负责展示。
            int delta = player.score;
            for (int i = 0; i < row; i++) {
                if (int.TryParse(player.score_history[i], out int previous)) delta -= previous;
            }
            player.score_history[row] = delta > 0 ? "+" + delta : delta.ToString();
            if (roundNumbers == null || player.round_number_history.Count > roundNumbers.Count)
                roundNumbers = player.round_number_history;
        }
        if (roundNumbers == null) return;
        // 重连仅恢复分数历史时，补齐主番列；血流已结束的局都经过查叫。
        while (snapshots.Count < roundNumbers.Count) {
            string label = roundNumbers[snapshots.Count] < currentRound ? SichuanRoundLabel.Chajiao : null;
            snapshots.Add(ScoreHistorySettlementHelper.CreateSichuanScoreboardSnapshot(subRule, label));
        }
        if (!string.IsNullOrEmpty(roundLabel) && snapshots.Count > 0) {
            RoundSettlementSnapshot snapshot = snapshots[snapshots.Count - 1];
            snapshot.sichuanRoundLabel = roundLabel;
            snapshot.isLiuju = roundLabel == SichuanRoundLabel.Liuju;
        }
    }
}
