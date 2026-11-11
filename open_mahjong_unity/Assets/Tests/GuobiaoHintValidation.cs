#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;

/// <summary>国标提示的时机、天气、点和/自摸边界；不修改运行中牌桌。</summary>
public static class GuobiaoHintValidation {
    private static readonly List<int> LowHand = new List<int> { 11, 12, 13, 24, 25, 26, 36, 37, 38, 45, 45, 45, 28, 28 };

    private static WaitHintQuery Query(List<int> hand, int self = 0) {
        var baseHand = new List<int>(hand);
        int tile = baseHand[baseHand.Count - 1];
        baseHand.RemoveAt(baseHand.Count - 1);
        var ways = new List<string> { "场风东", "自风" + "东南西北"[self] };
        if (GBtingpai.TingpaiCheck(baseHand, new List<string>(), false).Count == 1) ways.Add("和单张");
        return new WaitHintQuery {
            SubRule = "guobiao/standard", HandWithWin = new List<int>(hand), HepaiTile = tile,
            Melds = new List<string>(), WayToHepai = ways, SingleTileWay = new List<string>(),
            MergedWay = new List<string>(ways) { "点和" }, HepaiLimit = 8, SelfIndex = self, CurrentRound = 1,
        };
    }

    private static GuobiaoHintTiming Timing(int self = 0, int total = 0, int own = 0,
        int draw = 28, int ron = 0, int discarder = -1, bool enabled = true, bool meld = false,
        bool kong = false, bool robbing = false, bool last = false) => GuobiaoHintTiming.Create(
            "guobiao/standard", enabled, self, 0, own, total, total == 1 ? 1 : 0, meld,
            draw, ron, discarder, kong, robbing, last, last);

    public static string Run() {
        var checks = new List<string>();
        void Check(bool valid, string name) {
            if (!valid) throw new InvalidOperationException("国标提示回归：" + name);
            checks.Add(name);
        }
        var q = Query(LowHand);
        Check(GuobiaoTips.Describe(q).Kind == WaitTileHint.KindNone, "普通不足8番不可和");
        q.GuobiaoTiming = Timing();
        var heaven = GuobiaoTips.Describe(q);
        Check(heaven.Kind == WaitTileHint.KindTsumoOnly && heaven.Label.StartsWith("天和"), "庄家实际起手天和自摸提示");
        var normal = q.BuildZimoWay();
        Timing(enabled: false).AddTo(normal, q.HepaiTile, true);
        var opening = q.BuildZimoWay();
        q.GuobiaoTiming.AddTo(opening, q.HepaiTile, true);
        int baseScore = GBhepai.HepaiCheck(q.HandWithWin, q.Melds, normal, q.HepaiTile, false).Item1;
        int openingScore = GBhepai.HepaiCheck(q.HandWithWin, q.Melds, opening, q.HepaiTile, false).Item1;
        Check(openingScore == baseScore + 8 && heaven.Label.Contains(openingScore + "番"), "实际天和恰好加8番");
        q.GuobiaoTiming = Timing(enabled: false);
        Check(GuobiaoTips.Describe(q).Kind == WaitTileHint.KindNone, "关闭设置不加天和且缓存失效");
        var delayedFlower = Query(new List<int> { 11, 12, 13, 24, 25, 26, 26, 27, 28, 45, 45, 45, 22, 22 });
        delayedFlower.GuobiaoTiming = GuobiaoHintTiming.Create("guobiao/standard", false, 0, 0, 0, 0, 0, false,
            22, 0, -1, false, false, false, false, true);
        Check(GuobiaoTips.Describe(delayedFlower).Kind == WaitTileHint.KindNone, "首操作窗口去独听后7番不能和");
        delayedFlower.GuobiaoTiming = GuobiaoHintTiming.Create("guobiao/standard", false, 0, 0, 0, 0, 0, false,
            22, 0, -1, false, false, false, false, false);
        Check(GuobiaoTips.Describe(delayedFlower).Label == "8番", "延后补花即使零弃牌也恢复独听8番");
        q = Query(LowHand, 1);
        q.GuobiaoTiming = Timing(self: 1, total: 1);
        Check(GuobiaoTips.Describe(q).Label.StartsWith("人和"), "闲家首次实际自摸人和");
        q.GuobiaoTiming = Timing(self: 1, total: 3, own: 1);
        Check(GuobiaoTips.Describe(q).Kind == WaitTileHint.KindNone, "自己出过牌不能人和");
        q.GuobiaoTiming = Timing(self: 1, total: 1, draw: 0, ron: 28, discarder: 0);
        Check(GuobiaoTips.Describe(q).Label.StartsWith("地和"), "庄家首弃实际点和地和");
        var wrongTile = new List<string>();
        q.GuobiaoTiming.AddTo(wrongTile, 29, false);
        Check(wrongTile.Count == 0, "首弃地和不能加给其它候选和张");
        q.GuobiaoTiming = Timing(self: 1, total: 1, draw: 0);
        Check(GuobiaoTips.Describe(q).Kind == WaitTileHint.KindNone, "下一家摸牌后地和窗口消失");
        Check(Timing(meld: true).DrawOpening == null && Timing(self: 1, meld: true).DrawOpening == null
            && Timing(self: 1, total: 1, draw: 0, ron: 28, discarder: 0, meld: true).RonOpening == null,
            "任意鸣牌包括暗杠打断三种开局和");
        Check(Timing(kong: true).DrawOpening == null && Timing().DrawOpening == "天和", "杠后摸牌打断开局和，普通补花不打断");
        var eventWays = new List<string>();
        Timing(total: 3, kong: true, last: true).AddTo(eventWays, 28, true);
        Check(eventWays.Contains("杠上开花") && eventWays.Contains("last_deal"), "已发生杠开和末张自摸加入事件番");
        eventWays.Clear();
        Timing(total: 3, draw: 0, ron: 28, discarder: 2, last: true).AddTo(eventWays, 28, false);
        Check(eventWays.SequenceEqual(new[] { "last_cut" }), "已发生末张弃牌加入海底");
        eventWays.Clear();
        Timing(total: 3, kong: true, last: true).AddTo(eventWays, 29, true);
        Check(eventWays.Count == 0, "未来其它摸牌不预支杠开或妙手");

        var pairs27 = new[] { 12, 14, 17, 22, 24, 26, 28 }.SelectMany(tile => new[] { tile, tile }).ToList();
        q = Query(pairs27);
        q.JinluoMode = true;
        q.JinluoWeather = "dragon_pairs";
        var selfOnly = GuobiaoTips.Describe(q);
        Check(selfOnly.Kind == WaitTileHint.KindTsumoOnly && selfOnly.Label == "仅自摸", "七对27番自摸达28时提示仅自摸");
        q.JinluoWeather = "missing_suit";
        Check(GuobiaoTips.Describe(q).CanRon, "无字缺门天气认可断幺缺门且天气缓存失效");
        q = Query(new[] { 12, 14, 17, 22, 24, 26, 35 }.SelectMany(tile => new[] { tile, tile }).ToList());
        q.JinluoMode = true;
        Check(GuobiaoTips.Describe(q).Label == "天气不符", "未达到28且天气不符的七对提示原因");
        q.JinluoMode = false;
        Check(GuobiaoTips.Describe(q).CanRon, "退出金锣恢复普通国标且缓存失效");
        q = Query(LowHand);
        q.JinluoMode = true;
        Check(GuobiaoTips.Describe(q).Label == "未达金锣", "其它未达资格提示金锣限制");
        q.GuobiaoTiming = Timing();
        Check(GuobiaoTips.Describe(q).Label.StartsWith("天和"), "真实天和按独立金锣方法豁免限制");

        var record = new RecordTipsContext {
            RoomRule = "guobiao", SubRule = "guobiao/standard", HepaiLimit = 8, CurrentRound = 1,
            GuobiaoTiming = Timing(), PlayersByPosition = new Dictionary<string, RecordTipsPlayerVisible>(),
        };
        var hand13 = new List<int>(LowHand); hand13.RemoveAt(hand13.Count - 1);
        var actual = RecordWaitHintCalculator.BuildQueries(record, hand13, new List<int> { 28 })[0];
        var preview = RecordWaitHintCalculator.BuildQueries(record, hand13, new List<int> { 28 }, 28)[0];
        Check(actual.GuobiaoTiming != null && preview.GuobiaoTiming == null, "切牌预览清除当前真实时机");
        Check(GuobiaoTips.Describe(actual).Label.StartsWith("天和") && GuobiaoTips.Describe(preview).Kind == WaitTileHint.KindNone,
            "同牌现手与切牌预测不会互串缓存");
        Check(GuobiaoHintTiming.Create("guobiao/standard", true, 0, -1, 0, 0, 0, false, 28, 0, -1, false, false, false, false).DrawOpening == null,
            "缺少旧牌谱庄家信息时保守不加开局番");
        return JsonConvert.SerializeObject(new { passed = checks.Count, checks });
    }

#if UNITY_EDITOR
    public static string RunRecordContext() {
        if (UnityEngine.Application.isPlaying) throw new InvalidOperationException("请在 Edit Mode 运行牌谱上下文验证");
        var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
        var scene = UnityEditor.SceneManagement.EditorSceneManager.NewPreviewScene();
        var root = new UnityEngine.GameObject("Guobiao hint record context");
        root.SetActive(false);
        UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(root, scene);
        var manager = root.AddComponent<GameRecordManager>();
        var checks = new List<string>();
        void Check(bool valid, string name) {
            if (!valid) throw new InvalidOperationException(name);
            checks.Add(name);
        }
        void Set(string field, object value) => typeof(GameRecordManager).GetField(field, flags).SetValue(manager, value);
        try {
            var record = new GameRecord { gameTitle = new Dictionary<string, object> {
                ["rule"] = "guobiao", ["sub_rule"] = "guobiao/standard", ["tian_di_ren_he"] = true,
            } };
            typeof(GameRecordManager).GetProperty("gameRecord").SetValue(manager, record);
            var round = new Round { dealerIndex = 0, actionTicks = new List<List<string>> {
                new List<string> { "reset", "0" },
                new List<string> { "c", "28", "False" }, new List<string> { "d", "28" },
                new List<string> { "c", "28", "True" },
            } };
            var players = Enumerable.Range(0, 4).Select(index => new GameRecordManager.RecordPlayer {
                playerIndex = index, tileList = new List<int>(LowHand.Take(13)),
                combinationTiles = new List<string>(), discardTiles = new List<int>(),
            }).ToArray();
            void Rotate(int viewer) {
                manager.indexToPosition.Clear(); manager.recordPlayer_to_info.Clear();
                foreach (var player in players) {
                    string seat = TableMirror.RelativeSeat(viewer, player.playerIndex, 4);
                    manager.indexToPosition[player.playerIndex] = seat;
                    manager.recordPlayer_to_info[seat] = player;
                }
            }
            GuobiaoHintTiming TimingFor(int index) => (GuobiaoHintTiming)typeof(GameRecordManager)
                .GetMethod("BuildGuobiaoRecordTiming", flags).Invoke(manager, new object[] { players[index], round,
                    new RecordTipsContext { SubRule = "guobiao/standard", RemainTiles = 40 } });
            Rotate(0);
            manager.currentPlayerIndex = 0;
            players[0].tileList.Add(28);
            Check(TimingFor(0).DrawOpening == "天和" && TimingFor(1).DrawTile == 0, "牌谱起手只给庄家实际14张天和");
            manager.currentNode = 1;
            Check(TimingFor(0).DrawOpening == "天和" && TimingFor(0).InitialDraw, "牌谱开局补花后reset仍识别庄家起手并去独听");
            round.actionTicks.Insert(1, new List<string> { "bd", "28" });
            manager.currentNode = 2;
            Check(TimingFor(0).DrawOpening == "天和" && !TimingFor(0).InitialDraw, "牌谱首操作后补花保留天和且恢复独听");
            round.actionTicks.RemoveAt(1);
            players[0].tileList.RemoveAt(13); players[0].discardTiles.Add(28);
            manager.currentNode = 2; manager.currentPlayerIndex = 1;
            Set("lastDiscardPlayerIndex", 0); Set("lastDiscardTileId", 28); Set("waitingForDrawAfterCut", true);
            foreach (int viewer in Enumerable.Range(0, 4)) {
                Rotate(viewer);
                var timing = TimingFor(1);
                Check(timing.RonTile == 28 && timing.RonOpening == "地和" && timing.DrawTile == 0,
                    "牌谱视角" + viewer + "首弃按真实弃牌者判地和");
            }
            manager.currentNode = 3; players[1].tileList.Add(28); Set("waitingForDrawAfterCut", false);
            Check(TimingFor(1).DrawOpening == "人和" && TimingFor(1).RonTile == 0, "牌谱下一家摸牌结束地和并识别人和");
            record.gameTitle.Remove("tian_di_ren_he");
            Check(TimingFor(1).DrawOpening == null, "旧牌谱缺失开关不假设开启");
            record.gameTitle["tian_di_ren_he"] = true;
            players[2].combinationTiles.Add("G11");
            Check(TimingFor(1).DrawOpening == null, "牌谱他家暗杠打断开局和");
            players[2].combinationTiles.Clear();
            manager.currentNode = 2; players[1].tileList.RemoveAt(13); Set("waitingForDrawAfterCut", true);
            Check(TimingFor(1).RonOpening == "地和", "牌谱向前跳转重新恢复首弃时机");
            manager.currentNode = 0; manager.currentPlayerIndex = 0; players[0].discardTiles.Clear(); players[0].tileList.Add(28);
            Set("waitingForDrawAfterCut", false);
            Check(TimingFor(0).DrawOpening == "天和" && TimingFor(1).RonOpening == null, "牌谱跳回开局清除旧地和窗口");
            return JsonConvert.SerializeObject(new { passed = checks.Count, checks });
        } finally {
            UnityEditor.SceneManagement.EditorSceneManager.ClosePreviewScene(scene);
        }
    }
#endif
}
#endif
