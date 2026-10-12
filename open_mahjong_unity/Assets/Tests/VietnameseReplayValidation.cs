#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

/// <summary>Runs the real C# replay mutations against server-produced checkpoints.</summary>
public static class VietnameseReplayValidation {
    const BindingFlags Hidden=BindingFlags.Instance|BindingFlags.NonPublic;
    static readonly MethodInfo Apply=typeof(GameRecordManager).GetMethod("ApplyActionToRecordState",Hidden);
    static void Set(GameRecordManager m,string name,object value)=>typeof(GameRecordManager).GetField(name,Hidden).SetValue(m,value);
    static GameRecordManager Create(Transform parent,GameRecord record,Round round,JObject initial) {
        var go=new GameObject("Isolated vietnamese replay");go.SetActive(false);go.transform.SetParent(parent,false);
        var m=go.AddComponent<GameRecordManager>();typeof(GameRecordManager).GetProperty("gameRecord").SetValue(m,record);
        m.currentRoundIndex=round.roundIndex;m.currentPlayerIndex=round.startPlayerIndex;
        var players=new List<GameRecordManager.RecordPlayer>();string[] seats={"self","right","top","left"};
        for(int i=0;i<4;i++) {
            var p=initial["players"][i];
            var player=new GameRecordManager.RecordPlayer {playerIndex=i,originalPlayerIndex=round.seats.IndexOf(i),userId=101+i,
                tileList=p["hand"].ToObject<List<int>>(),score=(int)p["score"],showHandDrawSlotActive=(bool)p["draw_slot"],
                combinationTiles=p["melds"].ToObject<List<string>>(),combinationMasks=p["masks"].ToObject<List<int[]>>(),
                huapaiList=p["flowers"].ToObject<List<int>>(),discardTiles=p["river"].ToObject<List<int>>()};
            players.Add(player);m.indexToPosition[i]=seats[i];m.recordPlayer_to_info[seats[i]]=player;
        }
        Set(m,"recordPlayerList",players);var wall=new List<int>(round.tilesList);
        Set(m,"currentTilesList",wall);Set(m,"currentOriginalIndices",Enumerable.Range(0,wall.Count).ToList());
        typeof(GameRecordManager).GetMethod("ResetRecordRuleState",Hidden).Invoke(m,null);
        return m;
    }
    public static object Run(string[] paths=null) {
        typeof(VietnameseRuleBootstrap).GetMethod("Register",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,null);
        if(paths==null)paths=new[]{Path.Combine(Application.dataPath,"Tests/Fixtures/VietnameseReplay.json")};
        var root=new GameObject("vietnamese replay test root");root.SetActive(false);
        int checks=0,nodes=0,rounds=0,seeks=0;
        void Check(bool ok,string label){checks++;if(!ok)throw new InvalidOperationException(label);}
        void Compare(GameRecordManager m,JObject frame,string label) {
            var wall=(List<int>)typeof(GameRecordManager).GetField("currentTilesList",Hidden).GetValue(m);
            Check(wall.SequenceEqual(frame["wall"].ToObject<int[]>()),label+" wall");
            for(int i=0;i<4;i++) {
                var p=m.recordPlayer_to_info[m.indexToPosition[i]];var e=frame["players"][i];
                Check(p.tileList.OrderBy(t=>t).SequenceEqual(e["hand"].ToObject<int[]>().OrderBy(t=>t)),label+" hand/"+i);
                Check(p.discardTiles.SequenceEqual(e["river"].ToObject<int[]>()),label+" river/"+i);
                Check(p.combinationTiles.SequenceEqual(e["melds"].ToObject<string[]>()),label+" melds/"+i);
                Check(p.huapaiList.OrderBy(t=>t).SequenceEqual(e["flowers"].ToObject<int[]>().OrderBy(t=>t)),label+" flowers/"+i);
                Check(p.score==(int)e["score"],label+" score/"+i+" actual="+p.score+" expected="+e["score"]);
                var masks=e["masks"].ToObject<int[][]>();
                Check(p.combinationMasks.Count==masks.Length,label+" mask count/"+i);
                for(int j=0;j<masks.Length;j++)Check(p.combinationMasks[j].Where((t,k)=>k%2==1).OrderBy(t=>t)
                    .SequenceEqual(masks[j].Where((t,k)=>k%2==1).OrderBy(t=>t)),label+" physical meld/"+i+"/"+j);
            }
        }
        try {
            foreach(var path in paths) {
                var data=JObject.Parse(File.ReadAllText(path));var record=GameRecordJsonDecoder.ParseGameRecord(data["record"].ToString());
                foreach(var round in record.gameRound.rounds.Values) {
                    string key="round_index_"+round.roundIndex;var frames=(JArray)data["frames"][key];var initial=(JObject)data["initial"][key];
                    var m=Create(root.transform,record,round,initial);rounds++;
                    int cursor=0;
                    foreach(JObject frame in frames) {
                        int target=(int)frame["node"];
                        while(cursor<target){Apply.Invoke(m,new object[]{round.actionTicks[cursor++]});m.currentNode++;nodes++;}
                        Compare(m,frame,key+"/"+target);
                    }
                    var final=frames.Last;
                    int[] delta=Enumerable.Range(0,4).Select(i=>(int)final["players"][i]["score"]-(int)initial["players"][i]["score"]).ToArray();
                    Check(round.scoreChanges.SequenceEqual(GameRecordJsonDecoder.ConvertPlayerIndexScoreChangesToOriginal(delta,round.seats)),key+" full round ledger");
                    int[] start=(int[])typeof(GameRecordManager).GetMethod("GetRecordStartingScoresByOriginal",Hidden).Invoke(m,null);
                    Check(start.All(v=>v==300),key+" starting score");
                    foreach(JObject frame in frames.Where((f,i)=>i%Math.Max(1,frames.Count/4)==0)) {
                        var seek=Create(root.transform,record,round,initial);int target=(int)frame["node"];
                        for(int i=0;i<target;i++){Apply.Invoke(seek,new object[]{round.actionTicks[i]});seek.currentNode++;}
                        Compare(seek,frame,key+" seek/"+target);seeks++;UnityEngine.Object.DestroyImmediate(seek.gameObject);
                    }
                    UnityEngine.Object.DestroyImmediate(m.gameObject);
                }
            }
            return new{passed=checks,rounds,nodes,seeks};
        } finally {UnityEngine.Object.DestroyImmediate(root);}
    }
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void BatchAudit() {
        var args=Environment.GetCommandLineArgs();int at=Array.IndexOf(args,"--vietnamese-audit");
        if(at<0 || at+2>=args.Length)return;
        string fixture=args[at+1],output=args[at+2];int code=0;object result;
        try{result=new{passed=true,result=Run(new[]{fixture})};}
        catch(Exception e){code=2;result=new{passed=false,error=e.ToString()};}
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output)));
        File.WriteAllText(output,JsonConvert.SerializeObject(result,Formatting.Indented));Application.Quit(code);
    }
}
#endif
