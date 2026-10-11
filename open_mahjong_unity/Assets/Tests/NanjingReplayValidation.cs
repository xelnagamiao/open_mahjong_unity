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

public static class NanjingReplayValidation {
    const BindingFlags Hidden=BindingFlags.Instance|BindingFlags.NonPublic;
    static readonly MethodInfo Apply=typeof(GameRecordManager).GetMethod("ApplyActionToRecordState",Hidden);
    static void Set(GameRecordManager manager,string field,object value)=>typeof(GameRecordManager).GetField(field,Hidden).SetValue(manager,value);
    public static object Run(string[] paths) {
        if(Application.isPlaying)throw new InvalidOperationException("需要编辑模式隔离验证");
        typeof(NanjingRuleBootstrap).GetMethod("Register",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,null);
        var scene=EditorSceneManager.NewPreviewScene();var root=new GameObject("NanjingReplayValidation");root.SetActive(false);
        UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(root,scene);
        int nodes=0,rounds=0,checks=0;
        void Check(bool ok,string label){checks++;if(!ok)throw new InvalidOperationException(label);}
        try {
            foreach(string path in paths) {
                var fixture=JObject.Parse(File.ReadAllText(path));
                var record=GameRecordJsonDecoder.ParseGameRecord(fixture["record"].ToString());
                var scoreRows=ScoreHistoryRecordSettlementExtractor.ExtractScoreRows(record);
                Check(scoreRows.Count==record.gameRound.rounds.Count,Path.GetFileName(path)+" 每副计分板仅一行");
                foreach(var round in record.gameRound.rounds.Values) {
                    var final=round.actionTicks.Last(t=>t.Count>=4 && t[0]=="nanjing" && t[1]=="state");
                    var finalScores=JsonConvert.DeserializeObject<int[]>(final[3]);
                    var deltas=Enumerable.Range(0,4).Select(i=>finalScores[i]-round.nanjing.start_scores[i]).ToArray();
                    var expectedScores=GameRecordJsonDecoder.ConvertPlayerIndexScoreChangesToOriginal(deltas,round.seats);
                    Check(round.scoreChanges.SequenceEqual(expectedScores),Path.GetFileName(path)+" 换庄累计分/"+round.roundIndex);
                    var row=scoreRows.Single(r=>r.roundIndex==round.roundIndex);
                    Check(row.scoreChangesByOriginal.SequenceEqual(expectedScores),Path.GetFileName(path)+" 计分板含全部即时分/"+round.roundIndex);
                    if(row.snapshot.hepaiPlayerIndex>=0) {
                        var finalInfo=JsonConvert.DeserializeObject<NanjingInfo>(final[2]);
                        Check(row.snapshot.hepaiPlayerHand.OrderBy(t=>t).SequenceEqual(finalInfo.final_hands[row.snapshot.hepaiPlayerIndex].OrderBy(t=>t)),Path.GetFileName(path)+" 计分板终局手牌/"+round.roundIndex);
                    }
                    var go=new GameObject("Replay");go.SetActive(false);go.transform.SetParent(root.transform,false);
                    var manager=go.AddComponent<GameRecordManager>();
                    typeof(GameRecordManager).GetProperty("gameRecord").SetValue(manager,record);
                    manager.currentRoundIndex=round.roundIndex;manager.currentPlayerIndex=round.startPlayerIndex;
                    var players=new List<GameRecordManager.RecordPlayer>();var hands=new[]{round.p0Tiles,round.p1Tiles,round.p2Tiles,round.p3Tiles};
                    string[] positions={"self","right","top","left"};
                    for(int i=0;i<4;i++) {
                        var player=new GameRecordManager.RecordPlayer {playerIndex=i,originalPlayerIndex=round.seats.IndexOf(i),userId=101+i,
                            score=round.nanjing.start_scores[i],tileList=new List<int>(hands[i]),showHandDrawSlotActive=hands[i].Count==14};
                        var initial=fixture["initial_players"]?[i];
                        if(initial!=null) {
                            player.combinationTiles=initial["melds"].ToObject<List<string>>();player.combinationMasks=initial["masks"].ToObject<List<int[]>>();
                            player.huapaiList=initial["flowers"].ToObject<List<int>>();player.showHandDrawSlotActive=(bool)initial["draw_slot"];
                        }
                        players.Add(player);manager.indexToPosition[i]=positions[i];manager.recordPlayer_to_info[positions[i]]=player;
                    }
                    Set(manager,"recordPlayerList",players);var wall=new List<int>(round.tilesList);
                    Set(manager,"currentTilesList",wall);Set(manager,"currentOriginalIndices",Enumerable.Range(0,wall.Count).ToList());
                    rounds++;
                    foreach(var tick in round.actionTicks) {
                        Apply.Invoke(manager,new object[]{tick});manager.currentNode++;nodes++;
                        string label=$"{Path.GetFileName(path)}/{round.roundIndex}/{manager.currentNode}";
                        if(tick.Count<3 || tick[0]!="nanjing")continue;
                        if(tick[1]=="hints") {
                            var hints=JsonConvert.DeserializeObject<NanjingHints[]>(tick[2]);
                            for(int i=0;i<4;i++) {
                                if(players[i].isHu)continue;
                                Check(players[i].tileList.OrderBy(t=>t).SequenceEqual(hints[i].source_hand_tiles.OrderBy(t=>t)),label+" 手牌/"+i+" 实际="+string.Join(",",players[i].tileList)+" 预期="+string.Join(",",hints[i].source_hand_tiles));
                                Check(players[i].combinationTiles.OrderBy(t=>t).SequenceEqual(hints[i].source_melds.OrderBy(t=>t)),label+" 副露/"+i);
                            }
                        }
                        if(tick[1]=="state") {
                            var info=JsonConvert.DeserializeObject<NanjingInfo>(tick[2]);var scores=JsonConvert.DeserializeObject<int[]>(tick[3]);
                            for(int i=0;i<4;i++) {
                                Check(players[i].score==scores[i],label+" 终局总分/"+i);
                                Check(players[i].tileList.OrderBy(t=>t).SequenceEqual(info.final_hands[i].OrderBy(t=>t)),label+" 终局手牌/"+i);
                            }
                        }
                    }
                    UnityEngine.Object.DestroyImmediate(go);
                }
            }
            return new {passed=checks,rounds,nodes};
        } finally {EditorSceneManager.ClosePreviewScene(scene);}
    }
}
#endif
