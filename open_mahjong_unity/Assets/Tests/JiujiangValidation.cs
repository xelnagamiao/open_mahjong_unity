#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class JiujiangValidation {
    const BindingFlags PrivateInstance=BindingFlags.Instance|BindingFlags.NonPublic;
    public static object Run() {
        if(Application.isPlaying) throw new InvalidOperationException("请在编辑模式运行隔离测试");
        var checks=new List<string>();
        void Check(string name,bool valid) {if(!valid) throw new InvalidOperationException(name);checks.Add(name);}
        typeof(JiujiangRuleBootstrap).GetMethod("Register",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,null);
        var manifest=RuleRegistry.Resolve("jiujiang","jiujiang/standard");
        Check("独立建房注册",manifest?.GameStateFactory() is JiujiangGameState && manifest.OutboundChannel=="jiujiang");
        Check("报听按钮",manifest.ActionCaption("riichi_cut")=="报听");
        Check("补发财按钮",manifest.ActionCaption("buhua")=="补发财");
        Check("规则书入口",manifest.RulebookKey=="jiujiang");
        Check("暗杠全背",GameRecordMeldCodec.BuildAngangMaskFromRemoved(new[]{45,45,45,45},"jiujiang").SequenceEqual(new[]{2,45,2,45,2,45,2,45}));
        Check("暗杠不泄露枚数",manifest.VisibleMeldTiles("G45",false).Length==0 && !manifest.PeekAnkan);
        var player=new PlayerInfoClass {combination_tiles=new List<string>{"s12","G0"},combination_masks=new List<int[]>{new[]{0,11,0,12,0,13},new[]{2,0,2,0,2,0,2,0}}};
        JiujiangGameState.RevealConcealedMasks(player,new[]{new[]{2,45,2,45,2,45,2,45}});
        Check("局终暗杠翻明",player.combination_tiles[1]=="G45" && player.combination_masks[1].SequenceEqual(new[]{0,45,0,45,0,45,0,45}));
        Check("番型名称",JiujiangRuleBootstrap.FanName("红旗飘飘（2分）")=="红旗飘飘");
        Check("番型分数",JiujiangRuleBootstrap.FanValue("红旗飘飘（2分）")=="2分");
        Check("无额外单位",JiujiangRuleBootstrap.FanValue("未知")=="");
        var source=new[]{11,12,13,21,22,23,31,32,33,17,18,19,25};
        var query=new WaitHintQuery {HepaiTile=25,HandWithWin=source.Concat(new[]{25}).ToList(),Melds=new List<string>()};
        var cache=new JiujiangHintsCache();
        var preview=EditorSceneManager.NewPreviewScene();
        var root=new GameObject("JiujiangValidation",typeof(RectTransform));
        UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(root,preview);
        var prefab=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/GameScene/TipsFanCount.prefab");
        var view=UnityEngine.Object.Instantiate(prefab,root.transform).GetComponent<TipsFanCount>();
        string[] Labels() => new[]{"tipsFanCountText","secondHintText","thirdHintText"}
            .Select(n=>(TMP_Text)typeof(TipsFanCount).GetField(n,PrivateInstance).GetValue(view))
            .Where(t=>t!=null && t.gameObject.activeSelf && t.text.Length>0).Select(t=>t.text).ToArray();
        try {
            foreach(string kind in new[]{"dianhe","zimo","wuyi"}) foreach(int points in new[]{1,2,10,36}) {
                var hints=new JiujiangHints {source_hand_tiles=source,source_melds=Array.Empty<string>(),waiting_tiles=new[]{25},
                    waiting_details=new Dictionary<int,JiujiangWaitDetail>{{25,new JiujiangWaitDetail{points=points,kind=kind}}}};
                var packet=JsonConvert.DeserializeObject<Response>("{\"game_info\":{\"jiujiang_info\":{\"self_has_draw_slot\":true},\"jiujiang_hints\":"+JsonConvert.SerializeObject(hints)+"}}");
                Check("协议快照/"+kind+points,packet.game_info.jiujiang_info.self_has_draw_slot);
                cache.Accept(packet.game_info.jiujiang_hints);
                var hint=cache.Describe(query);
                Check("纯分数/"+kind+points,hint.Label==$"{points}分" && hint.Kind==kind);
                foreach(bool fan in new[]{false,true}) foreach(bool count in new[]{false,true}) foreach(int remaining in new[]{0,1,4}) {
                    view.SetHint(hint,remaining,fan,count);
                    var actual=Labels();
                    string[] expected=!fan && !count ? Array.Empty<string>() : kind=="wuyi" ? new[]{hint.Label}
                        : fan && count ? new[]{hint.Label,$"{remaining}枚"} : new[]{fan ? hint.Label : $"{remaining}枚"};
                    Check($"提示预制体/{kind}/{points}/{remaining}/{fan}/{count}",actual.SequenceEqual(expected));
                }
            }
            Check("未完成快照",new JiujiangHintsCache().Describe(query)==null);
            Check("锁定提示",cache.Describe(query,true).Label=="0分");
            var tq=new TingpaiQuery {Hand=source.ToList(),Melds=new List<string>()};
            Check("稳定听牌",cache.Waiting(tq).SetEquals(new[]{25}));
            tq.Hand[0]=18;Check("拒绝旧手牌",cache.Waiting(tq).Count==0);
            tq.Hand=source.ToList();tq.Melds.Add("k45");Check("拒绝旧副露",cache.Waiting(tq).Count==0);
            cache.Accept(new JiujiangHints {source_hand_tiles=source.Concat(new[]{29}).ToArray(),source_melds=Array.Empty<string>(),
                waiting_by_discard=new Dictionary<int,int[]>{{29,new[]{25}}},waiting_details_by_discard=new Dictionary<int,Dictionary<int,JiujiangWaitDetail>> {
                    {29,new Dictionary<int,JiujiangWaitDetail>{{25,new JiujiangWaitDetail{points=2,kind="dianhe"}}}}}});
            Check("切牌预览",cache.Describe(query)?.Label=="2分");
            cache.Reset();Check("清除跨局快照",cache.Describe(query)==null);
            CheckSettlement(root.transform,Check);
            return new {passed=checks.Count,checks};
        } finally {EditorSceneManager.ClosePreviewScene(preview);}
    }
    static void CheckSettlement(Transform root,Action<string,bool> check) {
        var properties=new[]{typeof(TableMirror).GetProperty("Current"),typeof(GameSession).GetProperty("Current"),
            typeof(GameSceneUIManager).GetProperty("Instance"),typeof(ScoreHistoryPanel).GetProperty("Instance"),
            typeof(NormalGameStateManager).GetProperty("Instance")};
        var previous=properties.Select(p=>p.GetValue(null)).ToArray();
        var uiObject=new GameObject("Isolated settlement UI");uiObject.SetActive(false);uiObject.transform.SetParent(root,false);
        var ui=uiObject.AddComponent<GameSceneUIManager>();
        var manager=uiObject.AddComponent<NormalGameStateManager>();
        var append=typeof(JiujiangGameState).GetMethod("AppendScoreboard",PrivateInstance);
        try {
            properties[1].SetValue(null,new GameSession {RoomRule=JiujiangGameState.RuleId,SubRule=JiujiangGameState.SubRule});
            properties[2].SetValue(null,ui);properties[3].SetValue(null,null);
            properties[4].SetValue(null,manager);
            var examples=new[] {
                new {name="原有结算",winner=0,score=3,huClass="hu_self",
                    fans=new[]{"自摸（1分）"},changes=new Dictionary<int,int>{{0,12},{1,-4},{2,-4},{3,-4}}},
                new {name="标注图第14副",winner=0,score=1,huClass="hu_self",
                    fans=new[]{"自摸（1分）"},changes=new Dictionary<int,int>{{0,16},{1,-4},{2,-6},{3,-6}}},
                new {name="标注图第7副",winner=1,score=3,huClass="hu_third",
                    fans=new[]{"明箭刻（1分）","明杠（1分）","发财（1分）"},
                    changes=new Dictionary<int,int>{{0,0},{1,4},{2,-4},{3,0}}},
            };
            foreach(var example in examples) foreach(int rotation in Enumerable.Range(0,4)) foreach(int reconnect in Enumerable.Range(0,3)) {
                var mirror=new TableMirror {CurrentRound=3};properties[0].SetValue(null,mirror);
                var changes=example.changes;
                var after=new Dictionary<int,int>();
                string[] positions={"self","right","top","left"};
                for(int seat=0;seat<4;seat++) {
                    mirror.IndexToPosition[seat]=positions[seat];var player=mirror.Info(positions[seat]);
                    int original=(seat+rotation)%4;player.original_player_index=original;
                    player.score=100+(reconnect>0?changes[original]:0);after[seat]=100+changes[original];
                    player.score_history=new List<string>{"+1","-1"};player.round_number_history=new List<int>{1,2};
                    if(reconnect==2) {player.score_history.Add(changes[original].ToString());player.round_number_history.Add(3);}
                }
                for(int i=0;i<(reconnect==2?3:2);i++) mirror.RoundSettlementHistory.Add(new RoundSettlementSnapshot());
                var state=new JiujiangGameState();
                var env=new SettlementEnvelope {WinnerIndex=(example.winner+4-rotation)%4,HuClass=example.huClass,HuScore=example.score,
                    FanLabels=example.fans,ScoreChanges=changes,ScoresAfter=after};
                for(int replay=0;replay<2;replay++) {
                    append.Invoke(state,new object[]{env});
                    for(int seat=0;seat<4;seat++) {
                        var player=mirror.Info(positions[seat]);
                        check($"结算与重连/{example.name}/{rotation}/{reconnect}/{replay}/{seat}",player.score==after[seat] && player.score_history.Count==3
                            && player.round_number_history.SequenceEqual(new[]{1,2,3}) && int.Parse(player.score_history.Last())==changes[player.original_player_index]);
                    }
                    check($"结算快照不重复/{example.name}/{rotation}/{reconnect}/{replay}",mirror.RoundSettlementHistory.Count==3);
                }
            }
        } finally {for(int i=0;i<properties.Length;i++) properties[i].SetValue(null,previous[i]);}
    }
}
#endif
