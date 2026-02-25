using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;

/// <summary>
/// 专门负责解析牌谱 JSON，并把结果转换为结构化的牌谱数据。
/// 使用 Newtonsoft.Json 进行 JSON 解析，直接转换为 GameRecord 数据结构。
/// </summary>
public static class GameRecordJsonDecoder {
    /// <summary>
    /// 解析完整的牌谱 JSON，返回结构化的 GameRecord 对象
    /// </summary>
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
        }
        catch (Exception e) {
            throw new Exception($"解析牌谱 JSON 失败: {e.Message}", e);
        }
    }

    /// <summary>
    /// 解析单个 round_index_x 的数据
    /// </summary>
    private static Round ParseRound(JObject roundData, int roundIndex, Dictionary<int, int> playerUserIds) {
        Round round = new Round();

        // 解析 round_index（如果存在，优先使用 JSON 中的值，否则使用传入的参数）
        if (roundData["round_index"] != null) {
            round.roundIndex = roundData["round_index"].Value<int>();
        } else {
            round.roundIndex = roundIndex;
        }

        // 设置玩家 userid
        if (playerUserIds.ContainsKey(0)) {
            round.p0UserId = playerUserIds[0];
        }
        if (playerUserIds.ContainsKey(1)) {
            round.p1UserId = playerUserIds[1];
        }
        if (playerUserIds.ContainsKey(2)) {
            round.p2UserId = playerUserIds[2];
        }
        if (playerUserIds.ContainsKey(3)) {
            round.p3UserId = playerUserIds[3];
        }

        // 解析 round_random_seed
        if (roundData["round_random_seed"] != null) {
            round.roundRandomSeed = roundData["round_random_seed"].Value<long>();
        }

        // 解析 current_round
        if (roundData["current_round"] != null) {
            round.currentRound = roundData["current_round"].Value<int>();
        }

        // 解析各玩家的手牌
        if (roundData["p0_tiles"] != null) {
            round.p0Tiles = roundData["p0_tiles"].ToObject<List<int>>();
        }
        if (roundData["p1_tiles"] != null) {
            round.p1Tiles = roundData["p1_tiles"].ToObject<List<int>>();
        }
        if (roundData["p2_tiles"] != null) {
            round.p2Tiles = roundData["p2_tiles"].ToObject<List<int>>();
        }
        if (roundData["p3_tiles"] != null) {
            round.p3Tiles = roundData["p3_tiles"].ToObject<List<int>>();
        }

        // 解析 tiles_list（牌堆列表）
        if (roundData["tiles_list"] != null) {
            round.tilesList = roundData["tiles_list"].ToObject<List<int>>();
        }

        // 解析 action_ticks（操作记录）
        JArray actionTicks = roundData["action_ticks"] as JArray;
        if (actionTicks != null) {
            round.actionTicks = actionTicks.ToObject<List<List<string>>>() ?? new List<List<string>>();
        }

        // 解析每局结算分数 score_changes：[p0变化, p1变化, p2变化, p3变化]，用于计分表
        JArray scoreChangesArr = roundData["score_changes"] as JArray;
        if (scoreChangesArr != null && scoreChangesArr.Count >= 4) {
            round.scoreChanges = new List<int>();
            for (int i = 0; i < 4; i++) {
                round.scoreChanges.Add(scoreChangesArr[i].Value<int>());
            }
        }

        return round;
    }
}