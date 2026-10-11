#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;

/// <summary>规则正文计分、普通提示、建房、八花牌谱与暗杠显示的客户端回归。</summary>
public static class LajiheValidation {
    public static string[] Run() {
        var registry = (Dictionary<string, RuleManifest>)typeof(RuleRegistry)
            .GetField("ManifestsByRule", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
        var saved = new Dictionary<string, RuleManifest>(registry);
        try { return RunChecks(); }
        finally { registry.Clear(); foreach (var item in saved) registry.Add(item.Key, item.Value); }
    }

    private static string[] RunChecks() {
        var checks = new List<string>();
        void Check(string label, bool ok) { if (!ok) throw new InvalidOperationException(label); checks.Add(label); }
        typeof(ShanghaiRuleBootstrap).GetMethod("Register", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, null);
        var manifest = RuleRegistry.Resolve("shanghai", LajiheHandCalculator.SubRule);
        Check("子规则工厂", manifest.SubRuleGameStateFactory(LajiheHandCalculator.SubRule) is LajiheGameState);
        var plain = new List<int> {11,12,13,21,22,23,31,32,33,17,18,19,41,41};
        var pairs = new List<int> {11,11,11,11,14,14,21,21,25,25,31,31,47,47};
        var honors = Enumerable.Range(41, 7).SelectMany(t => new[] { t,t }).ToList();
        var points = new[] {0,0,0,5,5,10,10,15,20,25,30};
        for (int i=0; i<points.Length; i++) Check("花分"+i, LajiheHandCalculator.FlowerPoints(i)==points[i]);
        for (int count=0; count<=8; count++) {
            Check("垃圾和点和门槛"+count, LajiheHandCalculator.Score(plain,null,count,false)==(count<2 ? 0 : 5+points[count]));
            Check("垃圾和自摸"+count, LajiheHandCalculator.Score(plain,null,count,true)==10+points[count]);
            Check("垃圾和抢杠"+count, LajiheHandCalculator.Score(plain,null,count,false,robKong:true)==10+points[count]);
        }
        Check("四张可计两对", LajiheHandCalculator.Score(pairs,null,0,false)==20);
        Check("七对自摸", LajiheHandCalculator.Score(pairs,null,0,true)==30);
        Check("全风向覆盖七对", LajiheHandCalculator.Score(honors,null,0,false)==40);
        var waits = honors.Take(13).ToList();
        Check("全风向不限结构", LajiheHandCalculator.Waits(waits,null).SetEquals(Enumerable.Range(41,7)));
        Check("暗杠大吊车", LajiheHandCalculator.Score(new List<int>{41,41},new List<string>{"s12","s22","s32","G19"},0,true,true)==20);
        var query = new WaitHintQuery {SubRule=LajiheHandCalculator.SubRule, HandWithWin=plain, Melds=new List<string>(), SelfFlowers=new List<int>()};
        var hint = manifest.DescribeWaitingTile(query);
        Check("零花提示仅显示番分且保留自摸颜色", hint.Kind==WaitTileHint.KindTsumoOnly && hint.Label=="0番10分");
        query.SelfFlowers=new List<int>{51,52}; hint=manifest.DescribeWaitingTile(query);
        Check("两花提示仅显示番分",hint.Kind==WaitTileHint.KindRon && hint.Label=="0番5分");
        Check("垃圾和点和危险资格",manifest.RecordDangerQualification(query));
        query.Record=new RecordTipsContext {SelfTags=new List<string>{"lajihe_passed_win"}};
        hint=manifest.DescribeWaitingTile(query);
        Check("牌谱过和提示零分且保留禁和颜色",hint.Kind==WaitTileHint.KindNone && hint.Label=="0番0分");
        query.Record.SelfTags.Clear();
        Check("牌谱摸牌解除过和",manifest.DescribeWaitingTile(query).Kind==WaitTileHint.KindRon);
        query.SelfFlowers.Clear();
        query.HandWithWin=pairs;
        Check("七对零番提示",manifest.DescribeWaitingTile(query).Label=="0番20分");
        query.HandWithWin=honors;
        Check("全风向一番提示",manifest.DescribeWaitingTile(query).Label=="1番40分");
        query.HandWithWin=new List<int>{11,11,11,14,14,14,17,17,17,19,19,19,18,18};
        Check("清碰一番提示",manifest.DescribeWaitingTile(query).Label=="1番40分");
        query.HandWithWin=new List<int>{41,41,41,42,42,42,45,45,45,46,46,46,47,47};
        Check("全风向碰碰和二番含花分提示",manifest.DescribeWaitingTile(query).Label=="2番100分");
        query.HandWithWin=new List<int>{11};
        hint=manifest.DescribeWaitingTile(query);
        Check("未满足提示零分且保留禁和颜色",hint.Kind==WaitTileHint.KindNone && hint.Label=="0番0分");
        Check("番种显示基础番",manifest.FanValueText(LajiheHandCalculator.SubRule,"全风向碰碰和")=="20分·2番");
        Check("番种显示额外分",manifest.FanNameText(LajiheHandCalculator.SubRule,"抢杠（5分）")=="抢杠"
            && manifest.FanValueText(LajiheHandCalculator.SubRule,"抢杠（5分）")=="5分");
        Check("八花选择保护",ActionWords.TryGet("lajihe_flower_end",out var action) && action.BlocksAutoCut);
        Check("暗杠四张公开",GameRecordMeldCodec.BuildAngangMaskFromRemoved(new[]{45,45,45,45},LajiheHandCalculator.SubRule)
            .SequenceEqual(new[]{0,45,0,45,0,45,0,45}));
        Check("原敲麻暗杠保持",GameRecordMeldCodec.BuildAngangMaskFromRemoved(new[]{41,41,41,41},"shanghai/qiaoma")
            .SequenceEqual(new[]{2,41,0,41,0,41,2,41}));
        var tick = new List<string>{"lajihe_eight_flowers","0","30","[\"八花齐\"]","[90,-30,-30,-30]"};
        var round = new Round {roundIndex=1,currentRound=1,seats=new List<int>{1,2,3,0},
            actionTicks=new List<List<string>>{tick,new List<string>{"liuju"}},
            p0Tiles=new List<int>(),p1Tiles=new List<int>(),p2Tiles=new List<int>(),p3Tiles=new List<int>()};
        GameRecordJsonDecoder.AccumulateScoreChangesFromTick(round,tick);
        Check("八花跨局累计按原座位",round.scoreChanges.SequenceEqual(new[]{-30,-30,-30,90}));
        var record = new GameRecord {gameTitle=new Dictionary<string,object>{{"rule","shanghai"},{"sub_rule",LajiheHandCalculator.SubRule}},
            gameRound=new GameRound{rounds=new Dictionary<int,Round>{{1,round}}}};
        var rows=ScoreHistoryRecordSettlementExtractor.ExtractScoreRows(record);
        Check("八花后流局两条计分历史",rows.Count==2 && rows[0].snapshot.huFan[0]=="八花齐" && rows[1].snapshot.isLiuju);
        Check("八花不伪造和牌与亮手",!rows[0].snapshot.hasWin && !rows[0].snapshot.CanShowHandPreview);
        return checks.ToArray();
    }

    // 用服务端生成的物理合法样例对齐全部花数、点和、自摸、补花、抢杠组合。
    public static int RunParity(string json) {
        var rows=JsonConvert.DeserializeObject<List<OracleRow>>(json);
        foreach (var row in rows) {
            int actual=LajiheHandCalculator.Score(row.hand,row.melds,row.flowers,row.self_draw,row.replacement,row.rob_kong);
            if(actual!=row.expected) throw new InvalidOperationException($"计分不一致: {JsonConvert.SerializeObject(row)} => {actual}");
        }
        return rows.Count;
    }

    private class OracleRow {
        public List<int> hand; public List<string> melds; public int flowers;
        public bool self_draw; public bool replacement; public bool rob_kong; public int expected;
    }
}
#endif
