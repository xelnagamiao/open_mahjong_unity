#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>用真实回放重建器核对服务端完整对局快照；不加载或修改活动场景。</summary>
public static class JiujiangReplayValidation {
    const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    static readonly MethodInfo Apply = typeof(GameRecordManager).GetMethod("ApplyActionToRecordState", Hidden);
    static void Set(GameRecordManager manager,string field,object value) => typeof(GameRecordManager).GetField(field,Hidden).SetValue(manager,value);
    static GameRecordManager Build(Transform root,GameRecord record,Round round) {
        var go=new GameObject("Isolated Jiujiang replay");go.SetActive(false);go.transform.SetParent(root,false);
        var manager=go.AddComponent<GameRecordManager>();
        typeof(GameRecordManager).GetProperty("gameRecord").SetValue(manager,record);
        manager.currentRoundIndex=round.roundIndex;manager.currentPlayerIndex=round.startPlayerIndex;
        var players=new List<GameRecordManager.RecordPlayer>();
        var hands=new[]{round.p0Tiles,round.p1Tiles,round.p2Tiles,round.p3Tiles};
        string[] positions={"self","right","top","left"};
        for(int seat=0;seat<4;seat++) {
            var player=new GameRecordManager.RecordPlayer {playerIndex=seat,originalPlayerIndex=round.seats.IndexOf(seat),
                userId=101+seat,tileList=new List<int>(hands[seat]),showHandDrawSlotActive=hands[seat].Count%3==2};
            players.Add(player);manager.indexToPosition[seat]=positions[seat];manager.recordPlayer_to_info[positions[seat]]=player;
        }
        Set(manager,"recordPlayerList",players);
        var wall=new List<int>(round.tilesList??new List<int>());
        Set(manager,"currentTilesList",wall);Set(manager,"currentOriginalIndices",Enumerable.Range(0,wall.Count).ToList());
        return manager;
    }
    static void Step(GameRecordManager manager,List<string> tick) {Apply.Invoke(manager,new object[]{tick});manager.currentNode++;}
    static GameRecordManager.RecordPlayer Player(GameRecordManager manager,int seat) => manager.recordPlayer_to_info[manager.indexToPosition[seat]];
    public static object Run(string[] recordPaths) {
        if(Application.isPlaying) throw new InvalidOperationException("请在编辑模式运行隔离测试");
        typeof(JiujiangRuleBootstrap).GetMethod("Register",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,null);
        int checks=0,rounds=0,nodes=0;
        void Check(bool condition,string label) {if(!condition) throw new InvalidOperationException(label);checks++;}
        var scene=EditorSceneManager.NewPreviewScene();
        var root=new GameObject("Jiujiang replay validation");root.SetActive(false);
        UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(root,scene);
        try {
            foreach(string path in recordPaths) {
                var record=GameRecordJsonDecoder.ParseGameRecord(JObject.Parse(File.ReadAllText(path))["record"].ToString());
                foreach(var round in record.gameRound.rounds.Values) {
                    var manager=Build(root.transform,record,round);rounds++;
                    for(int i=0;i<round.actionTicks.Count;i++) {
                        var tick=round.actionTicks[i];Step(manager,tick);nodes++;
                        if(tick.Count<4 || tick[0]!="jiujiang" || tick[1]!="hints") continue;
                        int seat=int.Parse(tick[2]);var hints=JsonConvert.DeserializeObject<JiujiangHints>(tick[3]);
                        var player=Player(manager,seat);
                        bool Matches() => player.tileList.OrderBy(t=>t).SequenceEqual(hints.source_hand_tiles.OrderBy(t=>t));
                        // 补花移除快照先随实时消息发出，随后才写对应 bh；只允许延至该动作核对。
                        if(!Matches() && i+1<round.actionTicks.Count && round.actionTicks[i+1][0]=="bh") {
                            Step(manager,round.actionTicks[++i]);nodes++;
                        }
                        Check(Matches(),$"{Path.GetFileName(path)}/{round.roundIndex}/{i}: 手牌");
                        Check(player.combinationTiles.OrderBy(t=>t).SequenceEqual(hints.source_melds.OrderBy(t=>t)),
                            $"{Path.GetFileName(path)}/{round.roundIndex}/{i}: 副露");
                    }
                }
            }
            var edgeRecord=new GameRecord {gameTitle=new Dictionary<string,object>{{"rule","jiujiang"},{"sub_rule","jiujiang/standard"}}};
            var edge=new Round {roundIndex=1,currentRound=1,startPlayerIndex=0,seats=new List<int>{0,1,2,3},
                p0Tiles=new List<int>{11,11,11,11,25},p1Tiles=new List<int>(),p2Tiles=new List<int>(),p3Tiles=new List<int>(),tilesList=new List<int>{21,22,23,24}};
            edgeRecord.gameRound.rounds[1]=edge;
            var kong=Build(root.transform,edgeRecord,edge);
            Step(kong,new List<string>{"ag","11","F"});
            Check(!Player(kong,0).showHandDrawSlotActive,"旧手牌暗杠收起摸牌槽");
            Check(Player(kong,0).combinationMasks[0].Where((v,i)=>i%2==0).All(v=>v==2),"暗杠全背");
            Step(kong,new List<string>{"gd","23"});
            Check(Player(kong,0).showHandDrawSlotActive,"杠后摸牌槽恢复");
            Step(kong,new List<string>{"jiujiang","reveal_kongs","[[[0,11,0,11,0,11,0,11]],[],[],[]]"});
            Check(Player(kong,0).combinationMasks[0].Where((v,i)=>i%2==0).All(v=>v==0),"局终暗杠翻明");
            return new {passed=checks,rounds,nodes};
        } finally {EditorSceneManager.ClosePreviewScene(scene);}
    }
}
#endif
