#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

/// <summary>Opt-in Guangdong client regression using saved real protocol frames. Never runs in ordinary launches.</summary>
public sealed class GuangdongRuntimeValidation : MonoBehaviour {
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Launch() {
        var args=Environment.GetCommandLineArgs();int index=Array.IndexOf(args,"--verify-guangdong-client");
        if(index<0||index+1>=args.Length)return;
        string folder=Path.GetFullPath(args[index+1]);
        string mode=index+2<args.Length?args[index+2]:"live";
        new GameObject("GuangdongClientValidation").AddComponent<GuangdongRuntimeValidation>().StartCoroutine(Boot(folder,mode));
    }
    private static IEnumerator Boot(string folder,string mode) {
        double limit=Time.realtimeSinceStartupAsDouble+30;
        while(NetworkManager.Instance==null||GameCanvas.Instance==null||NormalGameStateManager.Instance==null){
            if(Time.realtimeSinceStartupAsDouble>limit){Debug.LogError("Guangdong validation scene failed to initialize");Application.Quit(2);yield break;}yield return null;
        }
        yield return new WaitForSecondsRealtime(1);
        Directory.CreateDirectory(folder);
        string result=Path.Combine(folder,mode=="replay"?"unity-real-replay-results.json":"unity-live-protocol-results.json");
        if(File.Exists(result))File.Move(result,result+".previous-"+DateTime.UtcNow.Ticks);
        if(mode=="replay")RunRecords(folder);else RunProtocol(folder);
        limit=Time.realtimeSinceStartupAsDouble+1200;
        while(Time.realtimeSinceStartupAsDouble<limit){
            if(File.Exists(result)){
                string status=(string)Newtonsoft.Json.Linq.JObject.Parse(File.ReadAllText(result))["status"];
                if(status=="completed"||status=="failed"){Application.Quit(status=="completed"?0:1);yield break;}
            }
            yield return new WaitForSecondsRealtime(.2f);
        }
        Debug.LogError("Guangdong client validation timed out");Application.Quit(2);
    }

    public static object RunProtocol(string folder) {
        if(!UnityEngine.Application.isPlaying)throw new Exception("Play Mode required");
        var root=Path.GetFullPath(folder)+Path.DirectorySeparatorChar;
        var flags=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.NonPublic;
        var checks=new List<object>();var failures=new List<object>();var runtimeErrors=new List<string>();var voices=new List<string>();int eventsChecked=0,rounds=0;float peak=0;
        var volume=typeof(ConfigManager).GetProperty("VoiceVolume");var effects=typeof(ConfigManager).GetProperty("SoundEffectVolume");int oldVolume=ConfigManager.Instance.VoiceVolume,oldEffects=ConfigManager.Instance.SoundEffectVolume;
        var source=(UnityEngine.AudioSource)typeof(SoundManager).GetField("audioSource",flags).GetValue(SoundManager.Instance);
        void Log(string text,string stack,UnityEngine.LogType kind){if(text.StartsWith("播放音效:"))voices.Add(text);if(kind==UnityEngine.LogType.Error||kind==UnityEngine.LogType.Exception)runtimeErrors.Add(text+"\n"+stack);}
        void Save(string status)=>System.IO.File.WriteAllText(root+"unity-live-protocol-results.json",Newtonsoft.Json.JsonConvert.SerializeObject(new{status,rounds,eventsChecked,checks,failures,runtimeErrors},Newtonsoft.Json.Formatting.Indented));
        void Check(bool valid,string name,object detail=null){checks.Add(new{name,valid,detail});if(!valid)failures.Add(new{name,detail});}
        System.Collections.IEnumerator Idle(){double end=UnityEngine.Time.realtimeSinceStartupAsDouble+10;while(GameCanvas.Instance.IsChangeHandCardProcessing){if(UnityEngine.Time.realtimeSinceStartupAsDouble>end)throw new Exception("Hand queue stalled");yield return null;}}
        System.Collections.IEnumerator Sample(float duration){peak=0;var buffer=new float[1024];double end=UnityEngine.Time.realtimeSinceStartupAsDouble+duration;while(UnityEngine.Time.realtimeSinceStartupAsDouble<end){source.GetOutputData(buffer,0);foreach(float v in buffer)peak=Math.Max(peak,Math.Abs(v));yield return null;}}
        System.Collections.IEnumerator Run(){
         UnityEngine.Application.logMessageReceived+=Log;volume.SetValue(ConfigManager.Instance,100);effects.SetValue(ConfigManager.Instance,0);Save("running");
         var fixtures=Newtonsoft.Json.Linq.JArray.Parse(System.IO.File.ReadAllText(root+"live-protocol-fixtures.json"));var gsm=NormalGameStateManager.Instance;
         foreach(var fixture in fixtures){
          string group=(string)fixture["group"];int round=(int)fixture["round"];int voice=group=="replay_actions"||group=="promotion"?2:1;
          gsm.ResetForExit();var start=fixture["start"].ToObject<Response>();var own=start.game_info.players_info.First(p=>p.hand_tiles!=null&&p.hand_tiles.Length>0);own.user_id=PlayerSession.Current.UserId;
          foreach(var player in start.game_info.players_info)player.voice_used=voice;
          start.game_info.player_entry_order=start.game_info.players_info.Select(p=>p.user_id).ToArray();
          gsm.InitializeGame(true,"广东真实协议 "+group+" "+round,start.game_info);yield return new UnityEngine.WaitForSecondsRealtime(.5f);yield return Idle();
          Check(RuleRegistry.ActiveGameState is GuangdongMilGameState,"Guangdong state factory",new{group,round});
          foreach(var e in fixture["events"]){
           var response=e.ToObject<Response>();var before=TableMirror.Current.IndexToPosition.ToDictionary(x=>x.Key,x=>TableMirror.Current.Info(x.Value).score);
           voices.Clear();source.Stop();RuleRegistry.ActiveGameState.HandleMessage(response.type.Split('/')[2],response);
           var action=response.do_action_info;bool announced=action?.action_list?.Any(x=>x=="peng"||x=="gang"||x=="angang"||x=="jiagang")==true;
           if(announced)yield return Sample(.65f);else yield return null;yield return Idle();eventsChecked++;
           if(action!=null){
            Check(action.action_list==null||!action.action_list.Any(x=>x.Contains("buhua")),"ghost no replacement action",new{group,round,action.action_tick});
            Check(!voices.Any(v=>v.Contains("/buhua")||v.Contains("/bupai")),"ghost no flower audio",new{group,round,action.action_tick});
            if(action.gang_score_changes!=null)foreach(var pair in TableMirror.Current.IndexToPosition){int delta=action.gang_score_changes.TryGetValue(pair.Key,out int value)?value:0;Check(TableMirror.Current.Info(pair.Value).score==before[pair.Key]+delta,"immediate kong applied once",new{group,round,seat=pair.Key,actual=TableMirror.Current.Info(pair.Value).score,expected=before[pair.Key]+delta});}
            if(announced){string expectedFolder=voice==1?"204_xiaoxiao":"1513_qiuqiu";Check(voices.Any(v=>v.Contains(expectedFolder))&&peak>.0001f,"actual action voice "+voice,new{group,round,action=action.action_list,voices=voices.ToArray(),peak});}
           }
          }
          var final=fixture["result"].ToObject<Response>();voices.Clear();source.Stop();RuleRegistry.ActiveGameState.HandleMessage("show_result",final);yield return Sample(final.show_result_info.guangdong_result.draw?.7f:3.8f);yield return Idle();
          var info=final.show_result_info;var result=info.guangdong_result;
          if(!result.draw)Check(voices.Any(v=>v.Contains(voice==1?"204_xiaoxiao":"1513_qiuqiu"))&&peak>.0001f,"actual win voice "+voice,new{group,round,voices=voices.ToArray(),peak});
          foreach(var pair in TableMirror.Current.IndexToPosition){
           var player=TableMirror.Current.Info(pair.Value);int expected=info.player_to_score[pair.Key];
           Check(player.score==expected,"absolute terminal score",new{group,round,seat=pair.Key,player.score,expected});
           int net=(result.kong_changes?[pair.Key]??0)+(result.draw?(result.refund_changes?[pair.Key]??0):(result.win_changes?[pair.Key]??0));
           int row=player.round_number_history.IndexOf(start.game_info.current_round);string label=net>0?"+"+net:net.ToString();
           Check(row>=0&&player.score_history[row]==label,"full round score history",new{group,round,seat=pair.Key,expected=label,actual=player.score_history});
           var panel=Game3DManager.Instance.GetPosPanel(pair.Value);
           var tiles=panel.cardsPosition.GetComponentsInChildren<Tile3D>().Concat(panel.ShowCardsPosition.GetComponentsInChildren<Tile3D>()).Select(t=>t.GetTileId()).Where(t=>t>0).OrderBy(t=>t).ToArray();
           Check(tiles.SequenceEqual(info.revealed_hands[pair.Key].OrderBy(t=>t)),"all four physical hands publicly revealed",new{group,round,seat=pair.Key,tiles,expected=info.revealed_hands[pair.Key]});
           Check(player.huapai_list.Count==0,"no flower pool after settlement",new{group,round,seat=pair.Key});
          }
          if(!result.draw){int winner=info.hepai_player_index;int winnerNet=result.win_changes[winner]+result.kong_changes[winner];Check(TableMirror.Current.RoundSettlementHistory.Last().winnerScoreDelta==winnerNet,"rotated original-seat winner tooltip",new{group,round,expected=winnerNet,actual=TableMirror.Current.RoundSettlementHistory.Last().winnerScoreDelta});}
          var endPanel=result.draw?EndLiujuPanel.Instance.gameObject:EndResultPanel.Instance.gameObject;
          Check(endPanel.activeInHierarchy,"result panel open",new{group,round});
          foreach(var label in endPanel.GetComponentsInChildren<TMPro.TMP_Text>())if(label.enabled&&!string.IsNullOrEmpty(label.text)){label.ForceMeshUpdate();Check(label.font.HasCharacters(label.text,out uint[] missing,true,false),"settlement static glyphs",new{group,round,label=label.name,label.text,missing});}
          if((group=="basic"&&round==4)||(group=="responsibility"&&round==3)||(group=="kongs"&&round==1)){UnityEngine.ScreenCapture.CaptureScreenshot(root+"live-"+group+"-"+round+".png");yield return null;}
          int historyCount=TableMirror.Current.Self.score_history.Count;RuleRegistry.ActiveGameState.HandleMessage("show_result",final);yield return new UnityEngine.WaitForSecondsRealtime(.1f);
          Check(TableMirror.Current.Self.score_history.Count==historyCount,"duplicate settlement history idempotent",new{group,round});
          rounds++;Save("running");if(failures.Count>0){Save("failed");yield break;}
         }
         Save(runtimeErrors.Count==0?"completed":"failed");
        }
        System.Collections.IEnumerator Safe(){var iterator=Run();while(true){bool next=false;object value=null;try{next=iterator.MoveNext();if(next)value=iterator.Current;}catch(Exception error){failures.Add(new{name="exception",detail=error.ToString()});Save("failed");break;}if(!next)break;yield return value;}volume.SetValue(ConfigManager.Instance,oldVolume);effects.SetValue(ConfigManager.Instance,oldEffects);UnityEngine.Application.logMessageReceived-=Log;}
        NetworkManager.Instance.StartCoroutine(Safe());return new{started=true};
    }

    public static object RunRecords(string folder) {
        if(!UnityEngine.Application.isPlaying)throw new Exception("Play Mode required");
        var root=Path.GetFullPath(folder)+Path.DirectorySeparatorChar;
        var flags=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.NonPublic;
        var failures=new List<object>();var completed=new List<string>();int nodes=0,masks=0,ghosts=0;
        void Save(string status)=>System.IO.File.WriteAllText(root+"unity-real-replay-results.json",Newtonsoft.Json.JsonConvert.SerializeObject(new{status,nodes,masks,ghosts,completed,failures},Newtonsoft.Json.Formatting.Indented));
        void Check(bool condition,string label,object detail){if(!condition)failures.Add(new{label,detail});}
        System.Collections.IEnumerator Idle(){double end=UnityEngine.Time.realtimeSinceStartupAsDouble+10;while(GameCanvas.Instance.IsChangeHandCardProcessing){if(UnityEngine.Time.realtimeSinceStartupAsDouble>end)throw new Exception("Hand animation queue stalled");yield return null;}}
        System.Collections.IEnumerator Run(){
         Save("running");NormalGameStateManager.Instance.ResetForExit();
         var groups=Newtonsoft.Json.Linq.JArray.Parse(System.IO.File.ReadAllText(root+"unity-real-replay-oracle.json"));
         var wallField=typeof(GameRecordManager).GetField("currentTilesList",flags);var remainingMethod=typeof(GameRecordManager).GetMethod("GetRecordRemainTiles",flags);
         foreach(var group in groups){
          string name=(string)group["group"];var detail=Newtonsoft.Json.JsonConvert.DeserializeObject<RecordDetail>(System.IO.File.ReadAllText(root+"real-"+name+"-detail.json"));
          RecordPanel.OpenRecord(detail,true);yield return new UnityEngine.WaitForSecondsRealtime(.4f);yield return Idle();
          var manager=GameRecordManager.Instance;int previousRound=-1;
          Check(RuleRegistry.CurrentSubRule=="guangdong/mil2023","exact replay subrule",name);
          foreach(var expected in group["checkpoints"]){
           int round=(int)expected["round"],node=(int)expected["node"];
           if(previousRound!=round){manager.GotoSelectRound(round,false);previousRound=round;yield return null;yield return Idle();}
           manager.GotoAction(node);yield return null;yield return Idle();
           var wall=(List<int>)wallField.GetValue(manager);
           Check(wall.SequenceEqual(expected["wall"].ToObject<int[]>()),"exact physical wall",new{name,round,node,wall,expected=expected["wall"]});
           Check((int)remainingMethod.Invoke(manager,null)==(int)expected["remaining"],"no dead wall",new{name,round,node,actual=remainingMethod.Invoke(manager,null),expected=expected["remaining"]});
           foreach(var p in manager.recordPlayer_to_info.Values){
            var target=expected["players"][p.playerIndex];
            Check(p.score==(int)target["score"],"score",new{name,round,node,p.playerIndex,p.score,expected=target["score"]});
            Check(p.tileList.OrderBy(x=>x).SequenceEqual(target["hand"].ToObject<int[]>()),"physical hand",new{name,round,node,p.playerIndex,actual=p.tileList,expected=target["hand"]});
            Check(p.discardTiles.SequenceEqual(target["rivers"].ToObject<int[]>()),"river",new{name,round,node,p.playerIndex,actual=p.discardTiles,expected=target["rivers"]});
            Check(p.huapaiList.Count==0,"ghost never goes to flower zone",new{name,round,node,p.playerIndex,actual=p.huapaiList});ghosts+=p.tileList.Count(t=>t>=55&&t<=58);
            for(int m=0;m<target["melds"].Count();m++)if((bool)target["melds"][m]["concealed"]){int tile=(int)target["melds"][m]["tile"];Check(p.combinationMasks[m].SequenceEqual(new[]{2,tile,0,tile,0,tile,2,tile}),"public middle concealed kong",new{name,round,node,p.playerIndex,mask=p.combinationMasks[m]});masks++;}
           }
           nodes++;Save("running");if(failures.Count>0){Save("failed");yield break;}
          }
          completed.Add(name);Save("running");
         }
         Save("completed");
        }
        System.Collections.IEnumerator Safe(){var run=Run();while(true){bool more=false;object current=null;try{more=run.MoveNext();if(more)current=run.Current;}catch(Exception error){failures.Add(new{label="exception",detail=error.ToString()});Save("failed");yield break;}if(!more)yield break;yield return current;}}
        NetworkManager.Instance.StartCoroutine(Safe());return new{started=true};
    }
}
#endif
