#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using UnityEngine;

/// <summary>编辑器中的协议/适配回归；真实场景与联网回放另行验证。</summary>
public static class GuangdongClientValidation {
    public static string Run() {
        if (Application.isPlaying) throw new InvalidOperationException("请在实机对局前运行客户端适配单元验证");
        typeof(TuidaoRuleBootstrap).GetMethod("Register", BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,null);
        var passed=new List<string>();
        void Check(string name,bool valid) { if(!valid)throw new InvalidOperationException(name); passed.Add(name); }
        var manifest=RuleRegistry.Resolve("guangdong");
        Check("双版本入口及旧默认",manifest.LobbySubRules.Length==2&&manifest.DefaultSubRule==TuidaoRuleBootstrap.SubRule);
        Check("独立状态机",RuleRegistry.CreateGameState(manifest,GuangdongMilRules.SubRule) is GuangdongMilGameState && RuleRegistry.CreateGameState(manifest,TuidaoRuleBootstrap.SubRule) is TuidaoGameState);
        Check("无自动补花且专用通道",!manifest.HasFlowerReplacement&&manifest.OutboundChannel=="guangdong");
        foreach(bool minimum in new[]{false,true}) {
            var detail=GuangdongMilRules.Detail(minimum);
            Check("固定规则配置 "+minimum,(bool)detail["require_minimum_score"]==minimum&&(int)detail["minimum_fan"]==2&&(int)detail["horse_count"]==4);
            var room=new RoomInfo {room_rule="guangdong",sub_rule=GuangdongMilRules.SubRule,detailed_config=detail};
            var rows=RoomConfigContainer.BuildDisplayFields(room);
            Check("房间配置读回 "+minimum,rows.Any(row=>row.Key=="起和条件"&&row.Value==(minimum?"至少2番且4分":"至少2番")));
        }
        Check("白发编码",GuangdongMilRules.TileName(46)=="白"&&GuangdongMilRules.TileName(47)=="发");
        Check("四鬼实体",Enumerable.Range(55,4).All(GuangdongMilRules.IsGhost)&&!GuangdongMilRules.IsGhost(54));
        Check("系数不是番",GuangdongMilRules.FanTotal(new[]{"GD|a|2|碰碰和","GD|coefficient|×8|分数系数"})==2);
        Check("基本分总计",GuangdongMilRules.Total(new SettlementTotalQuery{HuScore=32,HuFan=new[]{"GD|a|2|碰碰和"}}).ScoreText=="基本分 32分");
        Check("鬼自摸不是花胡",!RecordHuHandBuilder.IsFlowerWin(new List<string>{"hu_self","0","4","[]","[0,0,0,0]","55"},GuangdongMilRules.SubRule));
        Check("旧花胡仍兼容",RecordHuHandBuilder.IsFlowerWin(new List<string>{"hu_self","0","4","[]","[0,0,0,0]","55"},"guobiao"));
        int[] hand={11,12,13,21,22,23,31,32,33,41,41,55,56};
        var query=new TingpaiQuery{Hand=new List<int>(hand),Melds=new List<string>(),SubRule=GuangdongMilRules.SubRule};
        GuangdongServerTips.Clear(); GuangdongServerTips.Clear(true);
        Check("无提示不做无癞猜测",GuangdongServerTips.Waiting(query,false).Count==0);
        var payload=new GuangdongTips{hand=hand,melds=Array.Empty<string>(),waits=new[]{new GuangdongWait{tile=57,fan=2,score=8,self_draw=true,self_draw_fan=2,self_draw_score=8}}};
        GuangdongServerTips.Accept(payload);
        Check("实体鬼听牌",GuangdongServerTips.Waiting(query,false).SetEquals(new[]{57}));
        var withWin=new List<int>(hand){57};
        var hint=GuangdongServerTips.Describe(new WaitHintQuery{HandWithWin=withWin,HepaiTile=57,Melds=new List<string>()});
        Check("仅自摸及服务端分值",hint.Kind==WaitTileHint.KindTsumoOnly&&hint.Label.Contains("8分"));
        GuangdongServerTips.Accept(new GuangdongTips{hand=hand,melds=Array.Empty<string>(),waits=new[]{new GuangdongWait{tile=11,fan=3,score=16,ron=true}}},true);
        var localWaiting=GuangdongBaseHints.Waiting(query.Hand,query.Melds);
        Check("牌谱补算且实时缓存隔离",GuangdongServerTips.Waiting(query,true).SetEquals(localWaiting)&&GuangdongServerTips.Waiting(query,false).SetEquals(new[]{57}));
        GuangdongServerTips.Accept(new GuangdongTips{hand=hand.Concat(new[]{58}).ToArray(),melds=Array.Empty<string>(),discard_waits=new Dictionary<int,GuangdongWait[]>{{58,new[]{new GuangdongWait{tile=11,fan=2,score=8,ron=true}}}}});
        Check("摸牌后弃鬼提示",GuangdongServerTips.Waiting(query,false).SetEquals(new[]{11}));
        GuangdongServerTips.Accept(null);
        Check("清空私有提示防残留",GuangdongServerTips.Waiting(query,false).Count==0);
        var round=new Round{seats=new List<int>{0,1,2,3}};
        GameRecordJsonDecoder.AccumulateScoreChangesFromTick(round,new List<string>{"guangdong","kong_score","[3,-1,-1,-1]"});
        Check("即时杠分落盘",round.scoreChanges.SequenceEqual(new[]{3,-1,-1,-1}));
        GameRecordJsonDecoder.AccumulateScoreChangesFromTick(round,new List<string>{"guangdong","refund_kongs","[-3,1,1,1]"});
        GameRecordJsonDecoder.AccumulateScoreChangesFromTick(round,new List<string>{"guangdong","horses","{\"changes\":[3,-1,-1,-1]}"});
        Check("退杠及奖马不重复",round.scoreChanges.All(v=>v==0));
        var response=JsonConvert.DeserializeObject<Response>("{\"game_info\":{\"guangdong_state\":{\"ghost_discard_counts\":[0,1,2,3]},\"guangdong_tips\":{\"hand\":[55],\"discard_waits\":{\"55\":[]}}},\"show_result_info\":{\"guangdong_result\":{\"fan\":2,\"base_score\":16,\"coefficient\":8,\"substitutions\":[{\"physical\":55,\"logical\":11}]}}}");
        Check("JSON实体键与替代解释",response.game_info.guangdong_tips.discard_waits.ContainsKey(55)&&response.show_result_info.guangdong_result.substitutions[0].physical==55);
        Check("旧协议字段可缺省",JsonConvert.DeserializeObject<GameInfo>("{}").guangdong_tips==null);
        Check("GD标签识别抢杠",(bool)typeof(GameRecordManager).GetMethod("ContainsSichuanQianggangFan",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{new[]{"GD|robbed_kong|2|抢杠"}}));
        Check("暗杠公示真实牌值",JsonConvert.DeserializeObject<GuangdongPublicState>("{\"kong_ledger\":[{\"kind\":\"concealed\",\"tile\":11,\"changes\":[3,-1,-1,-1]}]}").kong_ledger[0].tile==11);
        Check("暗杠两侧背面中间正面",GameRecordMeldCodec.BuildAngangMaskFromRemoved(new[]{11,11,11,11},GuangdongMilRules.SubRule).SequenceEqual(new[]{2,11,0,11,0,11,2,11}));
        Check("鬼牌余张唯一",RecordWaitHintCalculator.Remaining(55,new Dictionary<int,int>(),manifest)==1);
        GuangdongServerTips.Accept(payload,true,1);
        GuangdongServerTips.Accept(new GuangdongTips{hand=hand,melds=Array.Empty<string>(),waits=Array.Empty<GuangdongWait>()},true,2);
        Check("牌谱按各座位手牌补算",GuangdongServerTips.Waiting(query,true,1).SetEquals(localWaiting)&&GuangdongServerTips.Waiting(query,true,2).SetEquals(localWaiting));
        GuangdongServerTips.Accept(new GuangdongTips{hand=hand,melds=Array.Empty<string>(),waits=Array.Empty<GuangdongWait>()},true,1);
        Check("旧空快照不屏蔽结构听牌",GuangdongServerTips.Waiting(query,true,1).SetEquals(localWaiting));
        var total=GuangdongMilRules.RoundChanges(new GuangdongResult{kong_changes=new[]{6,-2,-2,-2},win_changes=new[]{36,-12,-12,-12}},null);
        Check("即时杠和牌全局历史净分",total[0]==42&&total[1]==-14);
        total=GuangdongMilRules.RoundChanges(new GuangdongResult{draw=true,kong_changes=new[]{6,-2,-2,-2},refund_changes=new[]{-6,2,2,2}},null);
        Check("流局退杠历史净零",total.Values.All(v=>v==0));
        int[] different={11,11,11,22,22,22,33,33,33,41,41,41,45};
        var separate=JsonConvert.DeserializeObject<GuangdongWait>("{\"tile\":45,\"fan\":6,\"score\":12,\"ron\":true,\"self_draw\":true,\"ron_fan\":5,\"self_draw_fan\":6,\"ron_score\":10,\"self_draw_score\":12}");
        var exact=new GuangdongTips{hand=different,melds=Array.Empty<string>(),waits=new[]{separate}};
        var exactQuery=new WaitHintQuery{HandWithWin=different.Concat(new[]{45}).ToList(),HepaiTile=45,Melds=new List<string>()};
        GuangdongServerTips.Accept(exact);
        Check("点和自摸独立番分",GuangdongServerTips.Describe(exactQuery).Label=="点和5番10分\n自摸6番12分");
        separate.ron_fan=separate.self_draw_fan=separate.ron_score=separate.self_draw_score=null;
        GuangdongServerTips.Accept(exact);
        Check("旧混合提示不误标最高值",GuangdongServerTips.Describe(exactQuery).Label=="点和\n自摸");
        separate.ron_score=10;separate.self_draw_score=12;
        GuangdongServerTips.Accept(exact);
        Check("旧独立分协议保留可确认分数",GuangdongServerTips.Describe(exactQuery).Label=="点和10分\n自摸12分");
        exactQuery.Record=new RecordTipsContext();
        Check("无提示记录的牌谱独立番分",GuangdongServerTips.Describe(exactQuery).Label=="点和5番10分\n自摸6番12分");
        exactQuery.Record.PlayersByPosition=new Dictionary<string,RecordTipsPlayerVisible>{{"self",new RecordTipsPlayerVisible{DiscardTiles=new List<int>{55,56}}}};
        Check("牌谱出两鬼无鬼连续相乘",GuangdongServerTips.Describe(exactQuery).Label=="点和5番40分\n自摸6番48分");
        int[] below={11,12,13,21,22,23,31,32,33,41,41,41,55};
        var belowQuery=new WaitHintQuery{HandWithWin=below.Concat(new[]{45}).ToList(),HepaiTile=45,Melds=new List<string>(),Record=new RecordTipsContext()};
        Check("未达门槛也保留结构候选",GuangdongServerTips.Waiting(new TingpaiQuery{Hand=below.ToList(),Melds=new List<string>(),RecordPlayerIndex=0}).Count==36);
        var belowHint=GuangdongServerTips.Describe(belowQuery);
        Check("牌谱未起和标签和类别",belowHint.Label=="未起和"&&belowHint.Kind==WaitTileHint.KindNone);
        belowQuery.DetailedConfig=GuangdongMilRules.Detail(false);
        Check("关闭四分门槛后自摸",GuangdongServerTips.Describe(belowQuery).Label=="自摸2番2分");
        GuangdongServerTips.Accept(new GuangdongTips{hand=below,melds=Array.Empty<string>(),waits=new[]{new GuangdongWait{tile=45}}});
        belowQuery.Record=null;
        Check("实战未起和标签和类别",GuangdongServerTips.Describe(belowQuery).Label=="未起和"&&GuangdongServerTips.Describe(belowQuery).Kind==WaitTileHint.KindNone);
        var impossible=new List<int>{11,12,13,21,22,23,31,32,33,41,41,41,41};
        Check("第五张字牌不可由鬼替代",!GuangdongBaseHints.Waiting(impossible,new List<string>()).Contains(55));
        Check("重复实体鬼库存无效",GuangdongBaseHints.Waiting(new List<int>{11,12,13,21,22,23,31,32,33,41,55,55,57},new List<string>()).Count==0);
        var copy=GuangdongBaseHints.Waiting(below.ToList(),new List<string>());copy.Clear();
        Check("牌谱候选缓存不泄露可变集合",GuangdongBaseHints.Waiting(below.ToList(),new List<string>()).Count==36);
        void BasicScore(string name, int[] complete, int tile, string[] melds, int ron, int self) {
            var result=GuangdongBaseHints.Evaluate(complete.ToList(),melds.ToList(),tile,true,0);
            Check(name,result.ron==(ron>0)&&result.self_draw==(self>0)&&result.ron_fan==ron&&result.self_draw_fan==self);
        }
        int[] M(string digits)=>digits.Select(d=>10+d-'0').ToArray();
        BasicScore("基础碰碰清一色",M("11122233344455"),15,Array.Empty<string>(),13,14);
        BasicScore("基础幺九不重复碰碰",new[]{11,11,11,19,19,19,41,41,41,42,42,42,21,21},21,Array.Empty<string>(),13,14);
        BasicScore("基础小三元",new[]{11,12,13,21,22,23,45,45,45,46,46,46,47,47},47,Array.Empty<string>(),13,14);
        BasicScore("基础大三元叠加碰碰",new[]{11,11,11,45,45,45,46,46,46,47,47,47,22,22},22,Array.Empty<string>(),21,22);
        BasicScore("基础大三元复合幺九",new[]{11,11,11,45,45,45,46,46,46,47,47,47,21,21},21,Array.Empty<string>(),29,30);
        BasicScore("基础小四喜复合幺九",new[]{41,41,41,42,42,42,43,43,43,11,11,11,44,44},44,Array.Empty<string>(),29,30);
        BasicScore("基础全字大四喜",new[]{41,41,41,42,42,42,43,43,43,44,44,44,45,45},45,Array.Empty<string>(),29,30);
        BasicScore("基础七对不计门清",new[]{11,11,22,22,33,33,44,44,45,45,46,46,47,47},47,Array.Empty<string>(),6,7);
        BasicScore("基础清七对",M("11223344556677"),17,Array.Empty<string>(),14,15);
        BasicScore("基础十三幺不计门清",new[]{11,19,21,29,31,39,41,42,43,44,45,46,47,11},11,Array.Empty<string>(),16,17);
        BasicScore("基础四鬼不加自摸",new[]{11,14,17,21,24,27,31,34,37,41,55,56,57,58},58,Array.Empty<string>(),0,12);
        BasicScore("四鬼另解九莲取高",new[]{11,11,11,12,13,14,15,16,17,18,55,56,57,58},58,Array.Empty<string>(),0,17);
        BasicScore("基础明杠副露计番",new[]{46,46,46,47,47,47,11,12,13,41,41},41,new[]{"g45"},20,21);
        BasicScore("基础暗杠保留门清",new[]{46,46,46,47,47,47,11,12,13,41,41},41,new[]{"G45"},21,22);
        BasicScore("闭手逻辑四张上限",M("1234444556677").Concat(new[]{55}).ToArray(),55,Array.Empty<string>(),0,10);
        BasicScore("副露不占闭手逻辑上限",M("1234556677").Concat(new[]{55}).ToArray(),55,new[]{"k14"},0,9);
        foreach(int tile in Enumerable.Range(11,9)) {
            BasicScore("九莲自然和牌张 "+tile,M("1112345678999").Concat(new[]{tile}).ToArray(),tile,Array.Empty<string>(),16,17);
        }
        BasicScore("九莲鬼和牌张",M("1112345678999").Concat(new[]{55}).ToArray(),55,Array.Empty<string>(),0,17);

        GuangdongServerTips.Clear(); GuangdongServerTips.Clear(true);
        var report=JsonConvert.SerializeObject(new{scope="Guangdong client adapters only; runtime/network tests are separate",passed=passed.Count,checks=passed},Formatting.Indented);
        string path=Path.GetFullPath(Path.Combine(Application.dataPath,"../../.om_workspace/guangdong-hints-fix-20261007/unity-adapter-tests.json"));
        Directory.CreateDirectory(Path.GetDirectoryName(path)); File.WriteAllText(path,report);
        Debug.Log(report); return report;
    }
}
#endif
