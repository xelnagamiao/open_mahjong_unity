using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;

/// <summary>
/// 专门负责解析牌谱 JSON，并把结果转换为结构化的牌谱数据。
/// </summary>
public static class GameRecordJsonDecoder {
    public static GameRecord ParseGameRecord(string recordJson) {
        if (string.IsNullOrEmpty(recordJson)) {
            throw new ArgumentException("牌谱 JSON 字符串不能为空", nameof(recordJson));
        }

        try {
            JObject root = JObject.Parse(recordJson);
            GameRecord gameRecord = new GameRecord();

            if (root["game_title"] != null) {
                gameRecord.gameTitle = root["game_title"].ToObject<Dictionary<string, object>>();
            }

            JObject gameRoundObj = root["game_round"] as JObject;
            if (gameRoundObj == null) {
                throw new Exception("未找到 game_round 节点");
            }

            // 从 game_title 中提取玩家 userid
            Dictionary<int, int> playerUserIds = new Dictionary<int, int>();
            if (gameRecord.gameTitle != null) {
                if (gameRecord.gameTitle.ContainsKey("p0_uid") && gameRecord.gameTitle["p0_uid"] != null) {
                    playerUserIds[0] = Convert.ToInt32(gameRecord.gameTitle["p0_uid"]);
                }
                if (gameRecord.gameTitle.ContainsKey("p1_uid") && gameRecord.gameTitle["p1_uid"] != null) {
                    playerUserIds[1] = Convert.ToInt32(gameRecord.gameTitle["p1_uid"]);
                }
                if (gameRecord.gameTitle.ContainsKey("p2_uid") && gameRecord.gameTitle["p2_uid"] != null) {
                    playerUserIds[2] = Convert.ToInt32(gameRecord.gameTitle["p2_uid"]);
                }
                if (gameRecord.gameTitle.ContainsKey("p3_uid") && gameRecord.gameTitle["p3_uid"] != null) {
                    playerUserIds[3] = Convert.ToInt32(gameRecord.gameTitle["p3_uid"]);
                }
            }

            int roundIndex = 1;
            while (true) {
                string roundKey = $"round_index_{roundIndex}";
                JObject roundData = gameRoundObj[roundKey] as JObject;
                if (roundData == null) {
                    break;
                }

                Round round = ParseRound(roundData, roundIndex, playerUserIds);
                gameRecord.gameRound.rounds[roundIndex] = round;
                roundIndex++;
            }

            return gameRecord;
        } catch (Exception e) {
            throw new Exception($"解析牌谱 JSON 失败: {e.Message}", e);
        }
    }

    public static void ApplyRoundHeader(Round round, JObject roundData, int roundIndex) {
        if (roundData["round_index"] != null) {
            round.roundIndex = roundData["round_index"].Value<int>();
        } else {
            round.roundIndex = roundIndex;
        }

        if (roundData["current_round"] != null) {
            round.currentRound = roundData["current_round"].Value<int>();
        }

        if (roundData["seats"] == null) {
            throw new Exception($"round_index_{roundIndex} 缺少 seats 字段");
        }
        round.seats = roundData["seats"].ToObject<List<int>>();
        if (round.seats == null || round.seats.Count != 4) {
            throw new Exception($"round_index_{roundIndex} seats 必须为长度 4 的数组");
        }
        if (roundData["dealer_index"] == null || roundData["start_player_index"] == null) {
            throw new Exception($"round_index_{roundIndex} 缺少 dealer_index 或 start_player_index");
        }
        round.dealerIndex = roundData["dealer_index"].Value<int>();
        round.startPlayerIndex = roundData["start_player_index"].Value<int>();

        if (roundData["riichi"] is JObject riichiObj) {
            round.riichi = new RiichiRoundExtras {
                honba = riichiObj["honba"]?.Value<int>() ?? 0,
                riichiSticks = riichiObj["riichi_sticks"]?.Value<int>() ?? 0,
            };
        }

        if (roundData["p0_tiles"] != null) round.p0Tiles = roundData["p0_tiles"].ToObject<List<int>>() ?? new List<int>();
        if (roundData["p1_tiles"] != null) round.p1Tiles = roundData["p1_tiles"].ToObject<List<int>>() ?? new List<int>();
        if (roundData["p2_tiles"] != null) round.p2Tiles = roundData["p2_tiles"].ToObject<List<int>>() ?? new List<int>();
        if (roundData["p3_tiles"] != null) round.p3Tiles = roundData["p3_tiles"].ToObject<List<int>>() ?? new List<int>();
        if (roundData["tiles_list"] != null) round.tilesList = roundData["tiles_list"].ToObject<List<int>>() ?? new List<int>();
    }

    public static void ApplyPlayerUserIds(Round round, Dictionary<int, int> playerUserIds) {
        if (playerUserIds.ContainsKey(0)) round.p0UserId = playerUserIds[0];
        if (playerUserIds.ContainsKey(1)) round.p1UserId = playerUserIds[1];
        if (playerUserIds.ContainsKey(2)) round.p2UserId = playerUserIds[2];
        if (playerUserIds.ContainsKey(3)) round.p3UserId = playerUserIds[3];
    }

    private static Round ParseRound(JObject roundData, int roundIndex, Dictionary<int, int> playerUserIds) {
        Round round = new Round();
        ApplyPlayerUserIds(round, playerUserIds);
        ApplyRoundHeader(round, roundData, roundIndex);

        JArray actionTicks = roundData["action_ticks"] as JArray;
        if (actionTicks != null) {
            round.actionTicks = new List<List<string>>();
            foreach (JToken tickToken in actionTicks) {
                JArray tickArr = tickToken as JArray;
                if (tickArr == null) continue;
                var tick = new List<string>();
                foreach (JToken elem in tickArr) {
                    if (elem is JArray arr) {
                        tick.Add(arr.ToString());
                    } else {
                        tick.Add(elem?.ToString() ?? "");
                    }
                }
                round.actionTicks.Add(tick);
                ValidateKanMoGangTick(tick, $"round_index_{roundIndex}");
                AccumulateScoreChangesFromTick(round, tick);
            }
        }

        return round;
    }

    /// <summary>
    /// 从单条 tick 累计本局 score_changes（兼容 hu_* / hu_riichi / ryuukyoku，含国标错和 hu_* tick）。
    /// 供牌谱初次解析与观战增量解析共用，确保观战分值列不为 0。
    /// </summary>
    public static void AccumulateScoreChangesFromTick(Round round, List<string> tick) {
        if (round == null || tick == null || tick.Count == 0) return;
        string act = tick[0];
        int[] sc;
        if (tick.Count >= 5 && (act == "hu_self" || act == "hu_first" || act == "hu_second" || act == "hu_third")) {
            sc = ParseScoreChangesFromTick(tick, 4);
        } else if (act == "hu_riichi" && tick.Count >= 7) {
            sc = ParseScoreChangesFromTick(tick, 6);
        } else if (act == "ryuukyoku" && tick.Count >= 3) {
            sc = ParseScoreChangesFromTick(tick, 2);
        } else {
            return;
        }
        AccumulateScoreChanges(round, ConvertPlayerIndexScoreChangesToOriginal(sc, round.seats));
    }

    private static void AccumulateScoreChanges(Round round, int[] sc) {
        if (sc == null) return;
        if (round.scoreChanges == null) {
            round.scoreChanges = new List<int>(sc);
        } else {
            for (int j = 0; j < 4 && j < sc.Length; j++) {
                round.scoreChanges[j] += sc[j];
            }
        }
    }

    /// <summary>
    /// tick 内 score_changes 按 player_index 排列，转换为 original 顺序。
    /// seats[original_i] = player_index。
    /// </summary>
    public static int[] ConvertPlayerIndexScoreChangesToOriginal(int[] byPlayerIndex, List<int> seats) {
        if (byPlayerIndex == null || seats == null || seats.Count < 4) return null;
        int[] byOriginal = new int[4];
        for (int orig = 0; orig < 4; orig++) {
            int seat = seats[orig];
            if (seat >= 0 && seat < byPlayerIndex.Length) {
                byOriginal[orig] = byPlayerIndex[seat];
            }
        }
        return byOriginal;
    }

    /// <summary>
    /// 解析补花 tick 第 4 段 T/F（摸补/手补）。旧牌谱仅 3 段时默认手补。
    /// </summary>
    public static bool ParseBuhuaMoFlag(List<string> tick, bool legacyDefault = false) {
        if (tick == null || tick.Count < 4) {
            return legacyDefault;
        }
        string flag = tick[3].ToUpperInvariant();
        if (flag != "T" && flag != "F") {
            throw new Exception($"补花 tick 第四段必须为 T 或 F: [{string.Join(", ", tick)}]");
        }
        return flag == "T";
    }

    /// <summary>
    /// 牌谱 tick 内显式行动者：bh/bd/鸣牌/和牌等；无则返回 defaultPlayer。
    /// </summary>
    public static int ResolveRecordActingPlayerIndex(List<string> tick, string action, int defaultPlayer) {
        if (tick == null || tick.Count == 0) return defaultPlayer;
        if ((action == "bh" || action == "bd" || action == "cl" || action == "cm" || action == "cr"
             || action == "p" || action == "g") && tick.Count >= 3) {
            if (!int.TryParse(tick[2]?.Trim(), out int seat)) return defaultPlayer;
            return seat;
        }
        if (action == "ca" && tick.Count >= 2) {
            if (!int.TryParse(tick[1]?.Trim(), out int seat)) return defaultPlayer;
            return seat;
        }
        if ((action == "hu_self" || action == "hu_first" || action == "hu_second" || action == "hu_third"
             || action == "riichi") && tick.Count >= 2) {
            if (!int.TryParse(tick[1]?.Trim(), out int seat)) return defaultPlayer;
            return seat;
        }
        return defaultPlayer;
    }

    /// <summary>
    /// 解析暗杠/加杠 tick 第三段 T/F（必填，不接受两段格式）。
    /// </summary>
    public static bool ParseKanMoGangFlag(List<string> tick) {
        if (tick == null || tick.Count < 3) {
            throw new Exception($"暗杠/加杠 tick 缺少摸杠/手杠标记: [{string.Join(", ", tick ?? new List<string>())}]");
        }
        string flag = tick[2].ToUpperInvariant();
        if (flag != "T" && flag != "F") {
            throw new Exception($"暗杠/加杠 tick 第三段必须为 T 或 F: [{string.Join(", ", tick)}]");
        }
        return flag == "T";
    }

    public static void ValidateKanMoGangTick(List<string> tick, string context) {
        if (tick == null || tick.Count == 0) return;
        string act = tick[0];
        if (act != "ag" && act != "jg") return;
        try {
            ParseKanMoGangFlag(tick);
        } catch (Exception e) {
            throw new Exception($"{context}: {e.Message}", e);
        }
    }

    private static int[] ParseScoreChangesFromTick(List<string> tick, int index) {
        if (index >= tick.Count || string.IsNullOrEmpty(tick[index])) return null;
        try {
            JArray arr = JArray.Parse(tick[index]);
            int[] result = new int[arr.Count];
            for (int i = 0; i < arr.Count; i++) {
                result[i] = arr[i].Value<int>();
            }
            return result;
        } catch {
            return null;
        }
    }
}
