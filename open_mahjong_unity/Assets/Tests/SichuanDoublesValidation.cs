#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

public static class SichuanDoublesValidation {
    public static string Run() {
        int checks = 0;
        void Check(bool condition, string label) {
            if (!condition) throw new InvalidOperationException(label);
            checks++;
        }
        Check(SichuanLobby.SubRules.Any(s => s.Key == "sichuan/doubles"), "双打入口");
        Check(ScoreHistorySettlementHelper.ResolveSichuanEndgameRoundLabel(2, false, SichuanLobby.DoublesSubRule) == SichuanRoundLabel.TeamFirstTwo, "双打提前结束不显示流局");
        Check(ScoreHistorySettlementHelper.ResolveSichuanEndgameRoundLabel(2, false, "sichuan/standard") == SichuanRoundLabel.Liuju, "标准血战标签不变");
        for (int rotation = 0; rotation < 3; rotation++) {
            int Tile(int t) => ((t / 10 - 1 + rotation) % 3 + 1) * 10 + t % 10;
            var three = new[] {11,12,13,14,15,16,21,22,23,31,31,31,29}.Select(Tile).ToList();
            var two = new[] {11,12,13,14,15,16,21,22,23,26,27,28,25}.Select(Tile).ToList();
            HashSet<int> Waits(List<int> hand, List<string> melds = null) => SichuanTips.Tingpai(new TingpaiQuery {
                Hand = hand, Melds = melds, SubRule = SichuanLobby.DoublesSubRule,
            });
            Check(Waits(three).Contains(Tile(29)), "三门牌型成立时保留候选张");
            Check(Waits(two).Contains(Tile(25)), "两门可报听");
            var hint = SichuanTips.Describe(new WaitHintQuery { HandWithWin = new List<int>(three) { Tile(29) }, HepaiTile = Tile(29), SubRule = SichuanLobby.DoublesSubRule });
            Check(hint.Kind == WaitTileHint.KindNone && hint.Label == "需缺门", "三门牌型提示需缺门且不能和牌");
            Check(SichuanTips.Describe(new WaitHintQuery { HandWithWin=new List<int>(two){Tile(25)}, HepaiTile=Tile(25), SubRule=SichuanLobby.DoublesSubRule }).Kind==WaitTileHint.KindRon, "已缺门恢复合法和牌提示");
            var sevenPairs=new[]{11,11,12,12,21,21,22,22,31,31,32,32,35}.Select(Tile).ToList();
            Check(Waits(sevenPairs).Contains(Tile(35)), "三门七对保留候选张");
            var sevenHint=SichuanTips.Describe(new WaitHintQuery { HandWithWin=new List<int>(sevenPairs){Tile(35)}, HepaiTile=Tile(35), SubRule=SichuanLobby.DoublesSubRule });
            Check(sevenHint.Kind==WaitTileHint.KindNone && sevenHint.Label=="需缺门", "三门七对提示需缺门");
            foreach (string sign in new[] {"k", "g", "G"}) {
                Check(!SichuanTips.HasMissingSuit(two, new List<string> {sign + Tile(31)}), "副露暗杠计入缺门");
                var meldHand=new[]{11,12,13,21,22,23,24,25,26,29}.Select(Tile).ToList();
                var melds=new List<string>{sign+Tile(31)};
                Check(Waits(meldHand,melds).Contains(Tile(29)), "副露第三门保留候选张："+sign);
                var meldHint=SichuanTips.Describe(new WaitHintQuery { HandWithWin=new List<int>(meldHand){Tile(29)}, Melds=melds, HepaiTile=Tile(29), SubRule=SichuanLobby.DoublesSubRule });
                Check(meldHint.Kind==WaitTileHint.KindNone && meldHint.Label=="需缺门", "副露第三门提示需缺门："+sign);
            }
            Check(Waits(new[]{11,12,14,16,18,21,23,25,27,31,33,35,37}.Select(Tile).ToList()).Count==0, "未成听牌结构不显示候选张");
        }
        var room = JsonConvert.DeserializeObject<RoomInfo>("{\"sub_rule\":\"sichuan/doubles\",\"team_by_seat\":[0,1,0,1],\"seat_list\":[101,102,0,0],\"roster_revision\":7}");
        Check(RoomPanel.DoublesTeamsComplete(room), "重复机器人席位可分队");
        room.team_by_seat[2] = -1;
        Check(!RoomPanel.DoublesTeamsComplete(room), "未分队不能开局");
        room.team_by_seat = new[] {0,0,0,1};
        Check(!RoomPanel.DoublesTeamsComplete(room), "三人同队不能开局");
        var request = new StartGameRequest { type = "room/start_game", room_id = "1", roster_revision = 7 };
        Check(JsonConvert.SerializeObject(request).Contains("\"roster_revision\":7"), "开局携带阵容版本");
        var snapshot = JsonConvert.DeserializeObject<GameInfo>("{\"sichuan_teams\":{\"team_by_player_index\":[0,1,0,1],\"settled_scores\":{\"0\":8,\"1\":-8}},\"players_info\":[{\"team_id\":0}]}");
        Check(snapshot.sichuan_teams.settled_scores[0] == 8 && snapshot.players_info[0].team_id == 0, "队伍协议反序列化");
        return $"Sichuan doubles: {checks} checks passed";
    }

    public static string ValidateRotatingBoardWinds() {
        if (!Application.isPlaying || !string.IsNullOrEmpty(UserDataManager.Instance.GamestateId))
            throw new InvalidOperationException("需要无在线对局的 Play Mode");
        var board = BoardCanvas.Instance;
        var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
        string[] positions = {"self", "right", "top", "left"};
        string[] winds = {"东", "南", "西", "北"};
        var labels = positions.ToDictionary(s => s, s =>
            (TMPro.TMP_Text)typeof(BoardCanvas).GetField("player_" + s + "_index", flags).GetValue(board));
        var texts = board.GetComponentsInChildren<TMPro.TMP_Text>(true).ToDictionary(t => t, t => t.text);
        var turns = positions.Select(s => (UnityEngine.UI.Image)typeof(BoardCanvas)
            .GetField("player_" + s + "_current_image", flags).GetValue(board))
            .ToDictionary(i => i.gameObject, i => i.gameObject.activeSelf);
        int checks = 0;
        try {
            foreach (string sub in new[] {"sichuan/doubles", "sichuan/doubles_duplicate", "sichuan/standard", "sichuan/xueliu_doubles"})
            for (int view = 0; view < 4; view++)
            for (int round = 1; round <= 8; round++) {
                int dealer = (round - 1) % 4;
                var info = SichuanPresentationValidation.Fixture(sub, view, round);
                info.dealer_index = info.current_player_index = dealer;
                var map = Enumerable.Range(0, 4).ToDictionary(i => i, i => positions[(i - view + 4) % 4]);
                var recordPlayers = info.players_info.Select(p => new GameRecordManager.RecordPlayer {
                    playerIndex=p.player_index, userId=p.user_id,
                }).ToList();
                for (int source = 0; source < 2; source++) {
                    if (source == 0) board.InitializeBoardInfo(info, map);
                    else board.InitializeBoardInfoFromRecord(recordPlayers, map, null, "sichuan", round, 55, sub, dealer);
                    foreach (var player in info.players_info) {
                        int wind = SichuanLobby.IsDoubles(sub) ? (player.player_index - dealer + 4) % 4 : player.player_index;
                        if (labels[map[player.player_index]].text != winds[wind])
                            throw new Exception($"{sub} 第{round}副 视角{view} 来源{source} 座位{player.player_index} 风位错误，应为{winds[wind]}");
                        checks++;
                    }
                }
            }
        } finally {
            foreach (var entry in texts) entry.Key.text = entry.Value;
            foreach (var entry in turns) entry.Key.SetActive(entry.Value);
        }
        return $"Sichuan live/replay board winds: {checks} checks passed";
    }

    public static string ShowAndValidateRoom(string subRule = "sichuan/doubles") {
        if (!Application.isPlaying) throw new InvalidOperationException("Room UI validation requires Play Mode");
        int uid = UserDataManager.Instance.UserId;
        var panel = UnityEngine.Resources.FindObjectsOfTypeAll<RoomPanel>().Single(p => p.gameObject.scene.IsValid());
        var cards = panel.GetComponentsInChildren<PlayerRoomPanel>(true).OrderBy(p => p.transform.GetSiblingIndex()).ToArray();
        var before = cards.Select(p => JsonUtility.ToJson(new RectSnapshot((RectTransform)p.transform))).ToArray();
        var info = new RoomInfo {
            room_id = "900009", room_name = "血战双打测试", room_rule = "sichuan", room_type = "custom", sub_rule = subRule,
            host_user_id = uid, max_player = 4, player_list = new[] {uid,101,102,103}, seat_list = new[] {uid,101,102,103},
            team_by_seat = new[] {0,1,0,1}, ready_list = new[] {101,102,103}, roster_revision = 3,
            game_round = 1, round_timer = 20, step_timer = 5, blood_battle = true,
            player_settings = new Dictionary<string,UserSettings>(),
        };
        foreach (int id in info.player_list) info.player_settings[id.ToString()] = new UserSettings { username = id == uid ? "房主 · A队" : "测试玩家" + id };
        WindowsManager.Instance.SwitchWindow("room");
        RoomWindowsManager.Instance.SwitchRoomWindow("roomInfo");
        panel.GetRoomInfoResponse(true,"",info);
        Canvas.ForceUpdateCanvases();
        if (panel.transform.Find("DoublesHint") || panel.transform.Find("RandomDoublesTeams"))
            throw new Exception("房间仍有额外分队说明或随机分队按钮");
        for (int i=0;i<cards.Length;i++) {
            if (before[i] != JsonUtility.ToJson(new RectSnapshot((RectTransform)cards[i].transform))) throw new Exception("四栏布局发生变化");
            var dropdown = cards[i].GetComponentInChildren<TMPro.TMP_Dropdown>(true);
            if (!dropdown || dropdown.value != info.team_by_seat[i]+1) throw new Exception("队伍未同步");
            var flags = System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic;
            var name = (TMPro.TMP_Text)typeof(PlayerRoomPanel).GetField("playerName", flags).GetValue(cards[i]);
            var ready = (GameObject)typeof(PlayerRoomPanel).GetField("readyIcon", flags).GetValue(cards[i]);
            Bounds selectorBounds = RectTransformUtility.CalculateRelativeRectTransformBounds(cards[i].transform, dropdown.transform);
            foreach (Transform protectedLabel in new[] {name.transform, ready.transform}) {
                Bounds labelBounds = RectTransformUtility.CalculateRelativeRectTransformBounds(cards[i].transform, protectedLabel);
                if (selectorBounds.Intersects(labelBounds)) throw new Exception("分队控件遮挡玩家名或准备标记");
            }
        }
        info.roster_revision++;
        info.team_by_seat[2] = -1;
        panel.GetRoomInfoResponse(true,"",info);
        var start = (UnityEngine.UI.Button)typeof(RoomPanel).GetField("startButton", System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).GetValue(panel);
        if (start.interactable) throw new Exception("未完成分队可开始");
        info.team_by_seat[2] = 0;
        panel.GetRoomInfoResponse(true,"",info);
        if (!start.interactable) throw new Exception("完整分队无法开始");
        return "Four original card rectangles preserved; labels unobstructed; team synchronization and start gating passed";
    }

    public static string ValidateSavedMatch(string path) {
        var root = JObject.Parse(System.IO.File.ReadAllText(path));
        var record = root["record"] as JObject ?? root;
        var game = GameRecordJsonDecoder.ParseGameRecord(record.ToString());
        var rows = ScoreHistoryRecordSettlementExtractor.ExtractScoreRows(game);
        int[] teams = record["game_title"]["team_by_player_index"].ToObject<int[]>();
        int[] totals = new int[2];
        foreach (var row in rows)
            for (int i=0;i<4;i++) totals[teams[i]] += row.scoreChangesByOriginal[i];
        for (int team=0;team<2;team++)
            if (totals[team] != record["doubles_result"]["scores"][team.ToString()].Value<int>())
                throw new Exception("回放计分板与团队终局分不一致");
        if (game.gameRound.rounds.Count != 4) throw new Exception("完整测试牌谱必须为四副");
        return $"Replay ledger: 4 boards, {rows.Count} rows, team totals {totals[0]} / {totals[1]} verified";
    }

    [Serializable] private class RectSnapshot {
        public Vector2 position, size, anchorMin, anchorMax;
        public RectSnapshot(RectTransform rect) {position=rect.anchoredPosition;size=rect.sizeDelta;anchorMin=rect.anchorMin;anchorMax=rect.anchorMax;}
    }
}
#endif
