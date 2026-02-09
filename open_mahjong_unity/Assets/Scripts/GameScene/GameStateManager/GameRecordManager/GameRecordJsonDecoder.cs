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

            int roundIndex = 1;
            while (true) {
                string roundKey = $"round_index_{roundIndex}";
                JObject roundData = gameRoundObj[roundKey] as JObject;
                if (roundData == null) {
                    break;
                }

                Round round = ParseRound(roundData, roundIndex);
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
    private static Round ParseRound(JObject roundData, int roundIndex) {
        Round round = new Round {
            roundIndex = roundIndex
        };

        if (roundData["round_random_seed"] != null) {
            round.roundRandomSeed = roundData["round_random_seed"].Value<long>();
        }
        if (roundData["current_round"] != null) {
            round.currentRound = roundData["current_round"].Value<int>();
        }
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
        if (roundData["tiles_list"] != null) {
            round.tilesList = roundData["tiles_list"].ToObject<List<int>>();
        }

        JArray actionTicks = roundData["action_ticks"] as JArray;
        if (actionTicks != null) {
            ParseActionTicks(actionTicks, round);
        }

        return round;
    }

    /// <summary>
    /// 解析 action_ticks 数组，填充到 Round 对象中
    /// </summary>
    private static void ParseActionTicks(JArray actionTicks, Round round) {
        int currentXunmu = 0;
        bool hasActionInCurrentXunmu = false;

        foreach (JToken actionToken in actionTicks) {
            JArray actionArray = actionToken as JArray;
            if (actionArray == null || actionArray.Count == 0) {
                continue;
            }

            string actionTypeStr = actionArray[0]?.ToString();
            if (string.IsNullOrEmpty(actionTypeStr)) {
                continue;
            }

            if (actionTypeStr == "Next") {
                if (hasActionInCurrentXunmu) {
                    currentXunmu++;
                    hasActionInCurrentXunmu = false;
                } else {
                    currentXunmu++;
                }
                continue;
            }

            List<string> actionList = new List<string>();
            foreach (JToken token in actionArray) {
                if (token.Type == JTokenType.String) {
                    actionList.Add(token.Value<string>());
                } else if (token.Type == JTokenType.Integer) {
                    actionList.Add(token.Value<int>().ToString());
                } else if (token.Type == JTokenType.Boolean) {
                    actionList.Add(token.Value<bool>().ToString().ToLower());
                } else if (token.Type == JTokenType.Array) {
                    actionList.Add(token.ToString(Newtonsoft.Json.Formatting.None));
                } else {
                    actionList.Add(token.ToString());
                }
            }

            if (!hasActionInCurrentXunmu) {
                round.xunmuToActionIndex[currentXunmu] = round.actionTicks.Count;
                hasActionInCurrentXunmu = true;
            }

            round.actionTicks.Add(actionList);
        }
    }

    /// <summary>
    /// 兼容旧接口：解析为原始格式（用于向后兼容）
    /// </summary>
    [Obsolete("请使用 ParseGameRecord 方法获取结构化的 GameRecord 对象")]
    public static void ParseAllRoundsFromJson(
        string recordJson,
        List<List<List<string>>> allRoundActionNodes,
        List<Dictionary<int, int>> allRoundXunmuToAction
    ) {
        if (allRoundActionNodes == null) throw new ArgumentNullException(nameof(allRoundActionNodes));
        if (allRoundXunmuToAction == null) throw new ArgumentNullException(nameof(allRoundXunmuToAction));

        allRoundActionNodes.Clear();
        allRoundXunmuToAction.Clear();

        GameRecord gameRecord = ParseGameRecord(recordJson);
        foreach (var round in gameRecord.gameRound.GetRoundsList()) {
            allRoundActionNodes.Add(round.actionTicks);
            allRoundXunmuToAction.Add(round.xunmuToActionIndex);
        }
    }
}