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

public static class SingaporeValidation {
    public static void Batch() {
        var args=Environment.GetCommandLineArgs();int i=Array.IndexOf(args,"--sg-report");
        string path=args[i+1];
        try {System.IO.File.WriteAllText(path,JsonConvert.SerializeObject(Run(),Formatting.Indented));EditorApplication.Exit(0);}
        catch(Exception error) {System.IO.File.WriteAllText(path,error.ToString());Debug.LogException(error);EditorApplication.Exit(1);}
    }
    const BindingFlags PrivateInstance=BindingFlags.Instance|BindingFlags.NonPublic;
    public static object Run() {
        if(Application.isPlaying) throw new InvalidOperationException("请在编辑模式运行隔离测试");
        var checks=new List<string>();
        void Check(string name,bool valid) {if(!valid) throw new InvalidOperationException(name);checks.Add(name);}
        typeof(SingaporeRuleBootstrap).GetMethod("Register",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,null);
        var manifest=RuleRegistry.Resolve("singapore","singapore/spgg2024");
        Check("独立建房注册",manifest?.GameStateFactory() is SingaporeGameState && manifest.OutboundChannel=="singapore");
        Check("补花按钮",manifest.ActionCaption("buhua")=="补花");
        Check("规则书入口",manifest.RulebookKey=="singapore");
        Check("暗杠全背",GameRecordMeldCodec.BuildAngangMaskFromRemoved(new[]{45,45,45,45},"singapore").SequenceEqual(new[]{2,45,2,45,2,45,2,45}));
        Check("暗杠不泄露枚数",manifest.VisibleMeldTiles("G45",false).Length==0 && !manifest.PeekAnkan);
        var player=new PlayerInfoClass {combination_tiles=new List<string>{"s12","G0"},combination_masks=new List<int[]>{new[]{0,11,0,12,0,13},new[]{2,0,2,0,2,0,2,0}}};
        SingaporeGameState.RevealConcealedMasks(player,new[]{new[]{2,45,2,45,2,45,2,45}});
        Check("局终暗杠翻明",player.combination_tiles[1]=="G45" && player.combination_masks[1].SequenceEqual(new[]{0,45,0,45,0,45,0,45}));
        Check("番型名称",SingaporeRuleBootstrap.FanName("红旗飘飘（2台）")=="红旗飘飘");
        Check("番型台数",SingaporeRuleBootstrap.FanValue("红旗飘飘（2台）")=="2台");
        Check("无额外单位",SingaporeRuleBootstrap.FanValue("未知")=="");
        var source=new[]{11,12,13,21,22,23,31,32,33,17,18,19,25};
        var query=new WaitHintQuery {HepaiTile=25,HandWithWin=source.Concat(new[]{25}).ToList(),Melds=new List<string>()};
        var cache=new SingaporeHintsCache();
        var preview=EditorSceneManager.NewPreviewScene();
        var root=new GameObject("SingaporeValidation",typeof(RectTransform));
        UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(root,preview);
        var prefab=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/GameScene/TipsFanCount.prefab");
        var view=UnityEngine.Object.Instantiate(prefab,root.transform).GetComponent<TipsFanCount>();
        string[] Labels() => new[]{"tipsFanCountText","secondHintText","thirdHintText"}
            .Select(n=>(TMP_Text)typeof(TipsFanCount).GetField(n,PrivateInstance).GetValue(view))
            .Where(t=>t!=null && t.gameObject.activeSelf && t.text.Length>0).Select(t=>t.text).ToArray();
        try {
            foreach(string kind in new[]{"dianhe","zimo","wuyi"}) foreach(int points in new[]{1,2,4,5}) {
                var hints=new SingaporeHints {source_hand_tiles=source,source_melds=Array.Empty<string>(),waiting_tiles=new[]{25},
                    waiting_details=new Dictionary<int,SingaporeWaitDetail>{{25,new SingaporeWaitDetail{points=points,kind=kind}}}};
                var packet=JsonConvert.DeserializeObject<Response>("{\"game_info\":{\"singapore_info\":{\"self_has_draw_slot\":true},\"singapore_hints\":"+JsonConvert.SerializeObject(hints)+"}}");
                Check("协议快照/"+kind+points,packet.game_info.singapore_info.self_has_draw_slot);
                cache.Accept(packet.game_info.singapore_hints);
                var hint=cache.Describe(query);
                Check("纯台数/"+kind+points,hint.Label==$"{points}台" && hint.Kind==kind);
                foreach(bool fan in new[]{false,true}) foreach(bool count in new[]{false,true}) foreach(int remaining in new[]{0,1,4}) {
                    view.SetHint(hint,remaining,fan,count);
                    var actual=Labels();
                    string[] expected=!fan && !count ? Array.Empty<string>() : kind=="wuyi" ? new[]{hint.Label}
                        : fan && count ? new[]{hint.Label,$"{remaining}枚"} : new[]{fan ? hint.Label : $"{remaining}枚"};
                    Check($"提示预制体/{kind}/{points}/{remaining}/{fan}/{count}",actual.SequenceEqual(expected));
                }
            }
            Check("未完成快照",new SingaporeHintsCache().Describe(query)==null);
            Check("锁定提示",cache.Describe(query,true).Label=="0台");
            var tq=new TingpaiQuery {Hand=source.ToList(),Melds=new List<string>()};
            Check("稳定听牌",cache.Waiting(tq).SetEquals(new[]{25}));
            tq.Hand[0]=18;Check("拒绝旧手牌",cache.Waiting(tq).Count==0);
            tq.Hand=source.ToList();tq.Melds.Add("k45");Check("拒绝旧副露",cache.Waiting(tq).Count==0);
            cache.Accept(new SingaporeHints {source_hand_tiles=source.Concat(new[]{29}).ToArray(),source_melds=Array.Empty<string>(),
                waiting_by_discard=new Dictionary<int,int[]>{{29,new[]{25}}},waiting_details_by_discard=new Dictionary<int,Dictionary<int,SingaporeWaitDetail>> {
                    {29,new Dictionary<int,SingaporeWaitDetail>{{25,new SingaporeWaitDetail{points=2,kind="dianhe"}}}}}});
            Check("切牌预览",cache.Describe(query)?.Label=="2台");
            cache.Reset();Check("清除跨局快照",cache.Describe(query)==null);
            CheckSettlement(root.transform,Check);
            for(int tile=61;tile<=64;tile++) {
                Check("动物牌手牌/"+tile,TileFaceResolver.LoadSprite(tile)!=null && TileFaceResolver.ShouldLayerHandFace(tile));
                Check("动物牌桌面/"+tile,TileFaceResolver.LoadTableTexture(tile)!=null && TileFaceResolver.LoadTableSprite(tile)!=null);
            }
            Check("自动和无多余和牌张",manifest.SettlementHasNoWinTile(new[]{"大三元自动和（5台）"}) && manifest.RecordFlowerWin(new[]{"hu_first","1","5","[\"八花和（5台）\"]"}));
            Check("普通大三元保留和牌张",!manifest.SettlementHasNoWinTile(new[]{"大三元（5台）"}));
            var rd=new Round {seats=new[]{1,2,3,0}.ToList(),scoreChanges=new List<int>{0,0,0,0}};
            GameRecordJsonDecoder.AccumulateScoreChangesFromTick(rd,new List<string>{"singapore","payment","[6,-2,-2,-2]"});
            Check("即时支付转换原始座位",rd.scoreChanges.SequenceEqual(new[]{-2,-2,-2,6}));
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
        var append=typeof(SingaporeGameState).GetMethod("AppendScoreboard",PrivateInstance);
        try {
            properties[1].SetValue(null,new GameSession {RoomRule=SingaporeGameState.RuleId,SubRule=SingaporeGameState.SubRule});
            properties[2].SetValue(null,ui);properties[3].SetValue(null,null);
            properties[4].SetValue(null,manager);
            var examples=new[] {
                new {name="普通自摸",winner=0,score=3,huClass="hu_self",
                    fans=new[]{"自摸（1台）"},changes=new Dictionary<int,int>{{0,12},{1,-4},{2,-4},{3,-4}}},
                new {name="即时支付结算",winner=0,score=1,huClass="hu_self",
                    fans=new[]{"自摸（1台）"},changes=new Dictionary<int,int>{{0,16},{1,-4},{2,-6},{3,-6}}},
                new {name="点和轮庄",winner=1,score=3,huClass="hu_third",
                    fans=new[]{"明箭刻（1台）","明杠（1台）","发财（1台）"},
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
                    player.score_history=new List<string>{"+1","-1"};player.round_number_history=new List<int>{3,3};
                    if(reconnect==2) {player.score_history.Add(changes[original].ToString());player.round_number_history.Add(3);}
                }
                for(int i=0;i<(reconnect==2?3:2);i++) mirror.RoundSettlementHistory.Add(new RoundSettlementSnapshot());
                var state=new SingaporeGameState();
                var env=new SettlementEnvelope {WinnerIndex=(example.winner+4-rotation)%4,HuClass=example.huClass,HuScore=example.score,
                    FanLabels=example.fans,ScoreChanges=changes,ScoresAfter=after};
                for(int replay=0;replay<1;replay++) {
                    typeof(SingaporeGameState).GetField("snapshotContainsSettlement",PrivateInstance).SetValue(state,reconnect==2);
                    append.Invoke(state,new object[]{env});
                    for(int seat=0;seat<4;seat++) {
                        var player=mirror.Info(positions[seat]);
                        check($"结算与重连/{example.name}/{rotation}/{reconnect}/{replay}/{seat}",player.score==after[seat] && player.score_history.Count==3
                            && player.round_number_history.SequenceEqual(new[]{3,3,3}) && int.Parse(player.score_history.Last())==changes[player.original_player_index]);
                    }
                    check($"结算快照不重复/{example.name}/{rotation}/{reconnect}/{replay}",mirror.RoundSettlementHistory.Count==3);
                }
            }
        } finally {for(int i=0;i<properties.Length;i++) properties[i].SetValue(null,previous[i]);}
    }
}
#endif
