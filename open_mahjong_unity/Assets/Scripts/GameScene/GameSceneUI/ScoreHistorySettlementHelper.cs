using System;
using System.Collections.Generic;
using System.Text;

/// <summary>
/// 计分板主番与番种文本格式化、实时对局结算累积。
/// </summary>
public static class ScoreHistorySettlementHelper {
    public static string ResolveSubRule(string rule, string subRuleFallback = null) {
        if (!string.IsNullOrEmpty(subRuleFallback)) return subRuleFallback;
        if (string.IsNullOrEmpty(rule)) return "guobiao/standard";
        string r = rule.ToLowerInvariant();
        return r switch {
            "guobiao" => "guobiao/standard",
            "qingque" => "qingque/standard",
            "classical" => "classical/standard",
            "riichi" => "riichi/standard",
            "sichuan" => "sichuan/standard",
            _ => r
        };
    }

    public static string GetMainFanColumnLabel(string subRule, RoundSettlementSnapshot snapshot, int rowIndex = -1) {
        if (snapshot == null) return "—";
        if (!string.IsNullOrEmpty(snapshot.sichuanRoundLabel)) {
            return SichuanRoundLabel.ToDisplayText(snapshot.sichuanRoundLabel);
        }
        if (subRule != null && subRule.StartsWith("sichuan")) return "—";
        if (snapshot.isLiuju) return "流局";
        if (snapshot.huFan == null || snapshot.huFan.Length == 0) return "—";
        string mainFan = PickMainFanName(subRule, snapshot.huFan);
        return string.IsNullOrEmpty(mainFan) ? "—" : mainFan;
    }

    /// <summary>四川血战终局：根据和牌家数与是否查叫，确定计分板主番类型。</summary>
    public static string ResolveSichuanEndgameRoundLabel(int huCount, bool hadChajiaoStep) {
        if (hadChajiaoStep) return SichuanRoundLabel.Chajiao;
        if (huCount >= 3) return SichuanRoundLabel.ThreeHu;
        return SichuanRoundLabel.Liuju;
    }

    public static RoundSettlementSnapshot CreateSichuanScoreboardSnapshot(string subRule, string roundLabel) {
        return new RoundSettlementSnapshot {
            subRule = subRule,
            sichuanRoundLabel = roundLabel,
            hasWin = false,
            isLiuju = roundLabel == SichuanRoundLabel.Liuju,
            huClass = roundLabel == SichuanRoundLabel.Liuju ? "liuju" : null,
        };
    }

    public static string PickMainFanName(string subRule, string[] huFan) {
        if (huFan == null || huFan.Length == 0) return "";
        // 错和结算时主番固定为"错和"（国标附在番种末尾，日麻有役错和亦然），不应取最大番。
        foreach (string fanKey in huFan) {
            if (fanKey == "错和") return "错和";
        }
        // 服务端已按番数从大到小排序，花牌乘算项排在末尾；直接取第一个非花牌项即可。
        foreach (string fanKey in huFan) {
            if (ShouldExcludeFromMainFanPick(fanKey)) continue;
            return StripFanMultiplier(fanKey);
        }
        return "";
    }

    /// <summary>主番选取时排除花牌乘算项（花牌*1～花牌*8 等），不参与最大番比较。</summary>
    public static bool ShouldExcludeFromMainFanPick(string fanKey) {
        if (string.IsNullOrEmpty(fanKey)) return true;
        return fanKey.StartsWith("花牌");
    }

    /// <summary>
    /// 断线重连后若局号列表与分值行数不一致则对齐（优先保留服务端已下发的完整局号；仅一条局号且多行分值时按 1..N 重建）。
    /// </summary>
    public static void AlignRoundNumberHistory(List<string> scoreHistory, List<int> roundNumberHistory) {
        if (scoreHistory == null || roundNumberHistory == null) return;
        int scoreCount = scoreHistory.Count;
        if (scoreCount == 0) {
            roundNumberHistory.Clear();
            return;
        }
        if (roundNumberHistory.Count == scoreCount) return;

        if (roundNumberHistory.Count == 0) {
            for (int i = 1; i <= scoreCount; i++) {
                roundNumberHistory.Add(i);
            }
            return;
        }

        // 典型重连脏数据：score_history 已从服务端恢复多行，但 round_number_history 仅追加了当前局号一行。
        if (roundNumberHistory.Count == 1 && scoreCount > 1) {
            roundNumberHistory.Clear();
            for (int i = 1; i <= scoreCount; i++) {
                roundNumberHistory.Add(i);
            }
            return;
        }

        if (roundNumberHistory.Count < scoreCount) {
            int last = roundNumberHistory[roundNumberHistory.Count - 1];
            while (roundNumberHistory.Count < scoreCount) {
                last++;
                roundNumberHistory.Add(last);
            }
            return;
        }

        roundNumberHistory.RemoveRange(scoreCount, roundNumberHistory.Count - scoreCount);
    }

    /// <summary>计分板局名列：仅当局号列表与分值行数等长时使用服务端局号，否则按行序 1..N 回退。</summary>
    public static int ResolveRoundNumberForRow(int rowIndex, int scoreHistoryCount, List<int> roundNumbers) {
        if (roundNumbers != null && roundNumbers.Count == scoreHistoryCount
            && rowIndex >= 0 && rowIndex < roundNumbers.Count) {
            return roundNumbers[rowIndex];
        }
        return rowIndex + 1;
    }

    /// <summary>
    /// 计分行与结算快照对齐：score_history 最后一行对应当前局最新快照。
    /// </summary>
    public static RoundSettlementSnapshot ResolveSettlementForRow(
        int roundIndex,
        int scoreHistoryCount,
        IReadOnlyList<RoundSettlementSnapshot> roundSettlements) {
        if (roundSettlements == null || roundSettlements.Count == 0) {
            var live = NormalGameStateManager.Instance?.roundSettlementHistory;
            if (live == null || live.Count == 0) return null;
            roundSettlements = live;
        }

        int settlementCount = roundSettlements.Count;
        if (settlementCount == 0 || scoreHistoryCount <= 0) return null;
        if (roundIndex < 0 || roundIndex >= scoreHistoryCount) return null;

        if (settlementCount == scoreHistoryCount) {
            return roundSettlements[roundIndex];
        }

        if (scoreHistoryCount > settlementCount) {
            // 重连后仅有尾部若干快照：只映射到 score_history 末尾对应行，避免唯一快照同时出现在第 0 行与最后一行。
            int settlementIndex = roundIndex - (scoreHistoryCount - settlementCount);
            if (settlementIndex >= 0 && settlementIndex < settlementCount) {
                return roundSettlements[settlementIndex];
            }
            return null;
        }

        // settlementCount > scoreHistoryCount：陈旧本地快照，按索引取前 scoreHistoryCount 条
        return roundSettlements[roundIndex];
    }

    public static string BuildAllFansText(string subRule, RoundSettlementSnapshot snapshot) {
        if (snapshot == null) return "";
        var sb = new StringBuilder();
        bool isClassical = subRule == "classical/standard";
        bool isRiichi = subRule != null && subRule.StartsWith("riichi");

        if (isClassical && snapshot.fuFanList != null) {
            for (int i = 0; i < snapshot.fuFanList.Length; i++) {
                string fuName = snapshot.fuFanList[i];
                if (sb.Length > 0) sb.Append(' ');
                sb.Append(FanTextDictionary.GetFuNameDisplayText(fuName));
                sb.Append(' ');
                sb.Append(FanTextDictionary.GetFuDisplayText(fuName));
            }
        }

        if (snapshot.huFan != null) {
            for (int i = 0; i < snapshot.huFan.Length; i++) {
                string fanKey = snapshot.huFan[i];
                if (sb.Length > 0) sb.Append(' ');
                if (isRiichi) {
                    sb.Append(FanTextDictionary.GetRiichiYakuDisplayName(fanKey));
                } else {
                    sb.Append(StripFanMultiplier(fanKey));
                }
                sb.Append(' ');
                sb.Append(FanTextDictionary.GetFanDisplayText(subRule, fanKey));
            }
        }

        return sb.ToString();
    }

    public static string BuildScoreSummaryText(string subRule, RoundSettlementSnapshot snapshot) {
        if (snapshot == null || !string.IsNullOrEmpty(snapshot.sichuanRoundLabel)) return "";
        if (snapshot.isLiuju) return "";

        bool isClassical = subRule == "classical/standard";
        bool isRiichi = subRule != null && subRule.StartsWith("riichi");
        bool isSichuan = subRule != null && subRule.StartsWith("sichuan");

        string fanPart;
        if (isRiichi && snapshot.han.HasValue) {
            fanPart = $"{snapshot.han.Value}番";
        } else if (isClassical) {
            int fanTotal = CalculateClassicalFanTotal(subRule, snapshot.huFan);
            fanPart = fanTotal >= 0 ? $"{fanTotal}番" : "满贯";
        } else if (isSichuan) {
            fanPart = $"{CalculateSichuanFanTotal(subRule, snapshot.huFan)}番";
        } else {
            fanPart = $"{snapshot.huScore}番";
        }

        int delta = snapshot.winnerScoreDelta;
        string scorePart = delta > 0 ? $"+{delta}分" : delta < 0 ? $"{delta}分" : "0分";
        return $"{fanPart} {scorePart}";
    }

    public static RoundSettlementSnapshot CreateFromShowResult(
        string subRule,
        string huClass,
        int hepaiPlayerIndex,
        string winnerUsername,
        int huScore,
        string[] huFan,
        int[] hepaiPlayerHand,
        int[][] combinationMask,
        int? baseFu,
        string[] fuFanList,
        RiichiEndResultExtras riichiExtras,
        Dictionary<int, int> scoreChanges) {
        // show_result 广播的 score_changes 键为 original_player_index，与牌谱 tick 按 seat 排列不同
        int winnerDelta = 0;
        if (scoreChanges != null && hepaiPlayerIndex >= 0) {
            int originalPlayerIndex = hepaiPlayerIndex;
            if (NormalGameStateManager.Instance != null
                && NormalGameStateManager.Instance.indexToPosition != null
                && NormalGameStateManager.Instance.indexToPosition.TryGetValue(hepaiPlayerIndex, out string huPos)
                && NormalGameStateManager.Instance.player_to_info != null
                && NormalGameStateManager.Instance.player_to_info.TryGetValue(huPos, out PlayerInfoClass winnerInfo)) {
                originalPlayerIndex = winnerInfo.original_player_index;
            }
            ShowResultPlayerScoreResolver.TryGetDelta(scoreChanges, hepaiPlayerIndex, originalPlayerIndex, out winnerDelta);
        }

        bool isLiuju = huClass == "liuju" || huClass == "ryuukyoku"
            || NormalGameStateManager.IsRiichiSpecialLiujuHuClass(huClass)
            || huClass == "jiuzhongjiupai";
        bool hasWin = !isLiuju && huFan != null && huFan.Length > 0;

        return new RoundSettlementSnapshot {
            hasWin = hasWin,
            isLiuju = isLiuju,
            huClass = huClass,
            hepaiPlayerIndex = hepaiPlayerIndex,
            winnerUsername = winnerUsername ?? "",
            huScore = huScore,
            winnerScoreDelta = winnerDelta,
            huFan = huFan != null ? (string[])huFan.Clone() : null,
            fuFanList = fuFanList,
            baseFu = baseFu,
            han = riichiExtras?.Han,
            fu = riichiExtras?.Fu,
            hepaiPlayerHand = hepaiPlayerHand != null ? (int[])hepaiPlayerHand.Clone() : null,
            combinationMask = CloneCombinationMask(combinationMask),
            subRule = subRule,
        };
    }

    /// <summary>古典数和尾：和牌局仅 show_shuhewei，由此创建整局计分板快照。</summary>
    public static RoundSettlementSnapshot CreateFromShuhewei(
        string subRule,
        string huClass,
        int? hepaiPlayerIndex,
        string winnerUsername,
        string[] huFan,
        string[] fuFanList,
        Dictionary<int, int> scoreChanges,
        int[] hepaiPlayerHand = null,
        int[][] combinationMask = null) {
        bool isLiuju = huClass == "liuju" || huClass == "jiuzhongjiupai";
        bool hasWin = !isLiuju && huFan != null && huFan.Length > 0;

        int winnerDelta = 0;
        if (scoreChanges != null && hepaiPlayerIndex.HasValue) {
            int originalPlayerIndex = hepaiPlayerIndex.Value;
            if (NormalGameStateManager.Instance != null
                && NormalGameStateManager.Instance.indexToPosition != null
                && NormalGameStateManager.Instance.indexToPosition.TryGetValue(hepaiPlayerIndex.Value, out string huPos)
                && NormalGameStateManager.Instance.player_to_info != null
                && NormalGameStateManager.Instance.player_to_info.TryGetValue(huPos, out PlayerInfoClass winnerInfo)) {
                originalPlayerIndex = winnerInfo.original_player_index;
            }
            ShowResultPlayerScoreResolver.TryGetDelta(
                scoreChanges, hepaiPlayerIndex.Value, originalPlayerIndex, out winnerDelta);
        }

        return new RoundSettlementSnapshot {
            subRule = subRule,
            huClass = huClass,
            hepaiPlayerIndex = hepaiPlayerIndex ?? -1,
            winnerUsername = winnerUsername ?? "",
            hasWin = hasWin,
            isLiuju = isLiuju,
            winnerScoreDelta = winnerDelta,
            huFan = huFan != null ? (string[])huFan.Clone() : null,
            fuFanList = fuFanList,
            hepaiPlayerHand = hepaiPlayerHand != null ? (int[])hepaiPlayerHand.Clone() : null,
            combinationMask = CloneCombinationMask(combinationMask),
        };
    }

    public static void UpdateLastFromShuhewei(
        List<RoundSettlementSnapshot> history,
        int? hepaiPlayerIndex,
        Dictionary<int, string[]> playerFan,
        Dictionary<int, int> scoreChanges,
        int[] hepaiPlayerHand = null,
        int[][] combinationMask = null,
        string huClass = null) {
        if (history == null || history.Count == 0) return;
        RoundSettlementSnapshot last = history[history.Count - 1];
        if (!string.IsNullOrEmpty(huClass)) {
            last.huClass = huClass;
            last.isLiuju = huClass == "liuju" || huClass == "jiuzhongjiupai";
        }
        if (hepaiPlayerIndex.HasValue) {
            last.hepaiPlayerIndex = hepaiPlayerIndex.Value;
        }
        if (playerFan != null && hepaiPlayerIndex.HasValue
            && playerFan.TryGetValue(hepaiPlayerIndex.Value, out string[] fan) && fan != null && fan.Length > 0) {
            last.huFan = fan;
            last.hasWin = true;
            last.isLiuju = false;
        }
        if (scoreChanges != null && hepaiPlayerIndex.HasValue) {
            int originalPlayerIndex = hepaiPlayerIndex.Value;
            if (NormalGameStateManager.Instance != null
                && NormalGameStateManager.Instance.indexToPosition != null
                && NormalGameStateManager.Instance.indexToPosition.TryGetValue(hepaiPlayerIndex.Value, out string huPos)
                && NormalGameStateManager.Instance.player_to_info != null
                && NormalGameStateManager.Instance.player_to_info.TryGetValue(huPos, out PlayerInfoClass winnerInfo)) {
                originalPlayerIndex = winnerInfo.original_player_index;
            }
            if (ShowResultPlayerScoreResolver.TryGetDelta(
                    scoreChanges, hepaiPlayerIndex.Value, originalPlayerIndex, out int delta)) {
                last.winnerScoreDelta = delta;
            }
        }
        if (hepaiPlayerHand != null && hepaiPlayerHand.Length > 0) {
            last.hepaiPlayerHand = (int[])hepaiPlayerHand.Clone();
        }
        if (combinationMask != null) {
            last.combinationMask = new int[combinationMask.Length][];
            for (int i = 0; i < combinationMask.Length; i++) {
                last.combinationMask[i] = combinationMask[i] != null ? (int[])combinationMask[i].Clone() : System.Array.Empty<int>();
            }
        }
    }

    private static int[][] CloneCombinationMask(int[][] source) {
        if (source == null) return null;
        var result = new int[source.Length][];
        for (int i = 0; i < source.Length; i++) {
            result[i] = source[i] != null ? (int[])source[i].Clone() : Array.Empty<int>();
        }
        return result;
    }

    private static string StripFanMultiplier(string fanKey) {
        if (string.IsNullOrEmpty(fanKey)) return "";
        int star = fanKey.IndexOf('*');
        return star >= 0 ? fanKey.Substring(0, star) : fanKey;
    }

    private static int ParseFanNumericValue(string subRule, string fanKey) {
        string display = FanTextDictionary.GetFanDisplayText(subRule, fanKey);
        if (display == "满贯" || display == "役满") return 10000;
        if (display.EndsWith("番") && int.TryParse(display.Replace("番", ""), out int val)) {
            return val;
        }
        if (display.EndsWith("符") && int.TryParse(display.Replace("符", ""), out int fuVal)) {
            return fuVal;
        }
        return 0;
    }

    public static int CalculateSichuanFanTotal(string subRule, string[] huFan) {
        if (huFan == null) return 0;
        int total = 0;
        foreach (string fan in huFan) {
            string display = FanTextDictionary.GetFanDisplayText(subRule, fan);
            if (display.EndsWith("番") && int.TryParse(display.Replace("番", ""), out int val)) {
                total += val;
            }
        }
        return total;
    }

    private static int CalculateClassicalFanTotal(string subRule, string[] huFan) {
        if (huFan == null) return 0;
        int total = 0;
        foreach (string fan in huFan) {
            string display = FanTextDictionary.GetFanDisplayText(subRule, fan);
            if (display == "满贯") return -1;
            if (display.EndsWith("翻") && int.TryParse(display.Replace("翻", ""), out int val)) {
                total += val;
            }
        }
        return total;
    }
}
