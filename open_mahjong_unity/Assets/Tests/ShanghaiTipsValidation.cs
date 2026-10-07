#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

/// <summary>敲麻结构听牌、普通资格及牌谱放铳提示；不涉及过水、偶然番或场景状态。</summary>
public static class ShanghaiTipsValidation {
    public static string[] Run() {
        var registry = (Dictionary<string, RuleManifest>)typeof(RuleRegistry)
            .GetField("ManifestsByRule", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
        var saved = new Dictionary<string, RuleManifest>(registry);
        try {
            return RunChecks();
        } finally {
            registry.Clear();
            foreach (var entry in saved) registry.Add(entry.Key, entry.Value);
        }
    }

    private static string[] RunChecks() {
        typeof(ShanghaiRuleBootstrap).GetMethod("Register", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, null);
        var manifest = RuleRegistry.Resolve("shanghai", "shanghai/qiaoma");
        var checks = new List<string>();
        void Check(string name, bool valid) {
            if (!valid) throw new InvalidOperationException(name);
            checks.Add(name);
        }
        List<int> plain = new List<int> {11,12,13,21,22,23,31,32,33,17,18,19,44};
        List<int> open = plain.Skip(3).ToList();
        var melds = new List<string> {"s12"};
        RecordTipsContext Context(GameRecordManager.RecordPlayer player, int minimum) => new RecordTipsContext {
            RoomRule = "shanghai", SubRule = "shanghai/qiaoma", HepaiLimit = minimum,
            CurrentRound = 1, SelfPlayerIndex = player.playerIndex,
            SelfHuapaiList = player.huapaiList, SelfTags = new List<string>(player.tagList ?? new List<string>()),
            SelfCombinationMasks = player.combinationMasks,
            PlayersByPosition = new Dictionary<string, RecordTipsPlayerVisible> {
                {"self", new RecordTipsPlayerVisible {DiscardTiles = player.discardTiles, CombinationTiles = player.combinationTiles}},
            },
        };
        WaitHintQuery Query(List<int> hand, List<string> codes, int flowers, int minimum) => new WaitHintQuery {
            SubRule = "shanghai/qiaoma", HepaiTile = 44, HandWithWin = new List<int>(hand) {44},
            Melds = codes, SelfFlowers = Enumerable.Range(51, flowers).ToList(), HepaiLimit = minimum,
        };
        GameRecordManager.RecordPlayer Player(List<int> hand, List<string> codes, int flowers, bool ready) => new GameRecordManager.RecordPlayer {
            playerIndex = 1, tileList = new List<int>(hand), combinationTiles = codes,
            huapaiList = Enumerable.Range(51, flowers).ToList(),
            tagList = ready ? new List<string> {"declared_ready"} : new List<string>(),
        };
        HashSet<int> Danger(GameRecordManager.RecordPlayer player, int minimum, bool wall = false) {
            var players = new Dictionary<string, GameRecordManager.RecordPlayer> { {"left", player} };
            Func<GameRecordManager.RecordPlayer, RecordTipsContext> context = p => Context(p, minimum);
            return wall ? RecordChongHintCalculator.ComputeDangerTiles(players, "shanghai", null, "shanghai/qiaoma", context)
                : RecordChongHintCalculator.ComputeRonDangerForHandOwner(players, "self", "shanghai", null, "shanghai/qiaoma", context);
        }
        Check("敲麻注册普通提示放铳筛选", manifest.RecordDangerUsesWaitHint);
        Check("敲麻注册真实敲牌资格筛选", manifest.RecordDangerQualification != null);
        foreach (var row in new[] {
            new {Flowers=1, Minimum=0, Kind=WaitTileHint.KindNone, Label="未满足"},
            new {Flowers=2, Minimum=0, Kind=WaitTileHint.KindTsumoOnly, Label="敲牌后仅自摸 3分"},
            new {Flowers=3, Minimum=0, Kind=WaitTileHint.KindRon, Label="敲牌后 4分"},
            new {Flowers=3, Minimum=1, Kind=WaitTileHint.KindNone, Label="未起和"},
            new {Flowers=2, Minimum=1, Kind=WaitTileHint.KindNone, Label="未起和"},
            new {Flowers=1, Minimum=1, Kind=WaitTileHint.KindNone, Label="未满足"},
        }) {
            string suffix = $"（{row.Flowers}花／门槛{row.Minimum}）";
            var query = Query(open, melds, row.Flowers, row.Minimum);
            var waiting = RuleTips.ComputeWaiting(manifest, new TingpaiQuery {SubRule=query.SubRule, Hand=open, Melds=melds});
            Check("未起和也保留结构待牌" + suffix, waiting.SetEquals(new[] {44}));
            var hint = RuleTips.DescribeWaitingTile(manifest, query);
            Check("实时普通提示" + suffix, hint.Kind == row.Kind && hint.Label == row.Label);
            var cut = new List<int>(open) {29}; cut.Remove(29);
            Check("切牌预览保留结构待牌" + suffix, manifest.Tingpai(new TingpaiQuery {SubRule=query.SubRule, Hand=cut, Melds=melds}).SetEquals(waiting));
            var player = Player(open, melds, row.Flowers, true);
            var recordQuery = RecordWaitHintCalculator.BuildQueries(Context(player, row.Minimum), open, waiting.ToList()).Single();
            var recordHint = RuleTips.DescribeWaitingTile(manifest, recordQuery);
            Check("牌谱和实时标签一致" + suffix, recordHint.Kind == hint.Kind && recordHint.Label == hint.Label);
            Check("手牌放铳只保留普通点和" + suffix, Danger(player, row.Minimum).Contains(44) == (row.Kind == WaitTileHint.KindRon));
            Check("牌山放铳只保留普通点和" + suffix, Danger(player, row.Minimum, true).Contains(44) == (row.Kind == WaitTileHint.KindRon));
        }
        var invalid = RuleTips.DescribeWaitingTile(manifest, Query(new List<int> {11,12,13}, new List<string>(), 3, 1));
        Check("牌型条件未满足不误标为起和番不足", invalid.Kind == WaitTileHint.KindNone && invalid.Label == "未满足");
        var undeclared = Player(plain, new List<string>(), 0, false);
        Check("未敲牌仍显示条件性22分", RuleTips.DescribeWaitingTile(manifest, Query(plain, new List<string>(), 0, 0)).Label == "敲牌后 22分");
        Check("未敲牌不标实际放铳", Danger(undeclared, 0).Count == 0 && Danger(undeclared, 0, true).Count == 0);
        var fallbackPlayers = new Dictionary<string, GameRecordManager.RecordPlayer> { {"left", undeclared} };
        Check("无上下文回调时仍排除未敲牌", RecordChongHintCalculator.ComputeDangerTiles(fallbackPlayers, "shanghai", null, "shanghai/qiaoma").Count == 0);
        undeclared.tagList.Add("declared_ready");
        Check("敲牌后门清点和恢复", Danger(undeclared, 1).SetEquals(new[] {44}));
        Check("无上下文回调仍读取真实敲牌标签", RecordChongHintCalculator.ComputeDangerTiles(fallbackPlayers, "shanghai", null, "shanghai/qiaoma").SetEquals(new[] {44}));
        Check("放铳集合不含手牌所有者自身待牌", RecordChongHintCalculator.ComputeRonDangerForHandOwner(fallbackPlayers, "left", "shanghai", null, "shanghai/qiaoma").Count == 0);
        undeclared.tileList.Add(29);
        Check("摸牌槽移除后仍正确筛选", Danger(undeclared, 1).SetEquals(new[] {44}));
        undeclared.tagList.Clear();
        Check("牌谱回退到未敲状态不会沿用危险牌", Danger(undeclared, 1).Count == 0);
        Check("缺少旧牌谱标签时仍保留结构听张", RecordChongHintCalculator.ComputeWaitingTilesForPlayer(undeclared, "shanghai", null, "shanghai/qiaoma").SetEquals(new[] {44}));
        undeclared.tagList = null;
        Check("旧牌谱空标签不误标或报错", Danger(undeclared, 1).Count == 0
            && RecordChongHintCalculator.ComputeDangerTiles(fallbackPlayers, "shanghai", null, "shanghai/qiaoma").Count == 0);
        var mixed = Player(new List<int> {11,11,12,13,14,15,16,17,41,41}, new List<string> {"s22"}, 2, true);
        var mixedWaiting = RecordChongHintCalculator.ComputeWaitingTilesForPlayer(mixed, "shanghai", null, "shanghai/qiaoma");
        Check("多面听结构包括序数和风牌", mixedWaiting.Contains(11) && mixedWaiting.Contains(41));
        Check("逐张资格筛选只保留风刻三花点和", Danger(mixed, 0).SetEquals(new[] {41}));
        Check("多面听牌山使用相同逐张筛选", Danger(mixed, 0, true).SetEquals(new[] {41}));
        Check("低花多面听不会被危险筛选清空结构提示", RecordChongHintCalculator.ComputeWaitingTilesForPlayer(mixed, "shanghai", null, "shanghai/qiaoma").SetEquals(mixedWaiting));
        Check("清混碰不套用敲牌门槛", manifest.RecordDangerQualification(new WaitHintQuery {SubRule="shanghai/qinghunpeng", Record=new RecordTipsContext()}));
        var savedQualification = manifest.RecordDangerQualification;
        try {
            manifest.RecordDangerQualification = null;
            Check("其他普通提示筛选规则不新增报敲门槛", Danger(Player(plain, new List<string>(), 0, false), 0).SetEquals(new[] {44}));
        } finally { manifest.RecordDangerQualification = savedQualification; }
        bool savedFilter = manifest.RecordDangerUsesWaitHint;
        try {
            manifest.RecordDangerUsesWaitHint = false;
            Check("未接入普通提示筛选的规则路径保持结构语义", Danger(Player(open, melds, 2, false), 1).SetEquals(new[] {44}));
        } finally { manifest.RecordDangerUsesWaitHint = savedFilter; }
        return checks.ToArray();
    }
}
#endif
