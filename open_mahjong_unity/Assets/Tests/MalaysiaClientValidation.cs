#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using TMPro;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine;

/// <summary>Opt-in real-client validation on disposable loopback identities.</summary>
public sealed class MalaysiaClientValidation : MonoBehaviour {
    static string output,endpoint; static int uid;
    NativeWebSocket.WebSocket socket;
    readonly List<string> failures=new List<string>();
    readonly List<object> observations=new List<object>();
    const BindingFlags Flags=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
    static T Find<T>() where T:Component=>Resources.FindObjectsOfTypeAll<T>().First(x=>x.gameObject.scene.IsValid());
    static T Field<T>(object o,string name)=>(T)o.GetType().GetField(name,Flags).GetValue(o);
    int actions,ends; bool ready;
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Configure() {
        output=null;var a=Environment.GetCommandLineArgs();int i=Array.IndexOf(a,"--verify-malaysia");
        if(i<0 || i+3>=a.Length)return;
        output=a[i+1];endpoint=a[i+2];uid=int.Parse(a[i+3]);
        if(!Uri.TryCreate(endpoint,UriKind.Absolute,out var uri)||!uri.IsLoopback||uid<11000001||uid>11000003)throw new InvalidOperationException("Malaysia QA requires disposable loopback users");
        ConfigManager.gameUrl=endpoint+"/"+uid;ConfigManager.BuildForSteam=false;ConfigManager.webApiUrl="http://127.0.0.1:18784";
    }
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Launch(){if(output==null)return;Find<ChatManager>().enabled=false;new GameObject("MalaysiaValidation").AddComponent<MalaysiaClientValidation>();}
    void Save(){Directory.CreateDirectory(output);File.WriteAllText(Path.Combine(output,"state.json"),JsonConvert.SerializeObject(new{ready,uid,actions,ends,failures,observations},Formatting.Indented));}
    void Check(bool ok,string text){if(!ok)failures.Add(text);}
    IEnumerator Start() {
        Application.runInBackground=true;Application.targetFrameRate=30;QualitySettings.vSyncCount=0;
        Screen.SetResolution(1280,720,FullScreenMode.Windowed);
        Application.logMessageReceived+=(m,t,k)=>{if(k==LogType.Error||k==LogType.Exception){failures.Add(m);Save();}};
        float until=Time.realtimeSinceStartup+30;
        while(!NetworkManager.Instance.IsWebSocketOpen && Time.realtimeSinceStartup<until)yield return null;
        Check(NetworkManager.Instance.IsWebSocketOpen,"connection did not open");
        var statistics=PlayerInfoStatsFormatter.GameDetails(new PlayerStatsInfo {rule="malaysia",total_games=1,total_rounds=3,win_count=1,total_fan_score=300,total_fangchong_score=300,total_round_score=200,first_place_count=1});
        Check(!PlayerInfoRuleCatalog.HasFanStats("malaysia"),"fan statistics enabled");
        Check(!statistics.Any(p=>p.Key.Contains("番")||p.Key=="四位率"),"unexpected fan or fourth-place statistics");
        Check(statistics.Any(p=>p.Key=="局均点"&&p.Value=="200.00"),"statistics points missing");
        UserDataManager.Instance.SetUserInfo("马来三麻QA"+(uid-11000000),"qa-only",uid,false);
        Find<UserContainer>().ShowUserSettings(new UserSettings());WindowsManager.Instance.SwitchWindow("menu");
        Bind();ready=true;Save();_=socket.SendText("{\"type\":\"qa/ready\"}");
    }
    void Bind(){if(socket!=null)socket.OnMessage-=Receive;socket=NetworkManager.Instance.GetWebSocket();socket.OnMessage+=Receive;}
    void Receive(byte[] bytes) {
        var m=JObject.Parse(System.Text.Encoding.UTF8.GetString(bytes));string type=(string)m["_qa"] ?? (string)m["type"];
        if(type=="qa/action")StartCoroutine(Act(m));
        if(type=="qa/inspect")StartCoroutine(Inspect(m));
        if(type=="qa/reconnect")StartCoroutine(Reconnect());
        if(type=="qa/replay")StartCoroutine(Replay(m));
        if(type=="qa/quit"){Save();Application.Quit();}
        if(type!=null && type.EndsWith("/game_end")){ends++;Save();}
    }
    IEnumerator Act(JObject m) {
        yield return new WaitForSecondsRealtime(.18f);
        int tick=(int)m["tick"];var state=NormalGameStateManager.Instance;string action=(string)m["action"];
        float until=Time.realtimeSinceStartup+5;
        while(state.LastAskActionTick<tick && Time.realtimeSinceStartup<until)yield return null;
        // Opening flowers can already have been submitted by the normal automatic replacement UI.
        if(state.LastAskActionTick>tick)yield break;
        Check(state.LastAskActionTick==tick,"action tick differs from client");
        if(action!="ready") Check(state.allowActionList.Contains(action),"offered action missing in UI: "+action);
        if(action=="cut") {
            int tile=(int)m["tile"];bool drawn=(bool)m["drawn"];
            TileCard card=null;
            until=Time.realtimeSinceStartup+5;
            while(card==null && Time.realtimeSinceStartup<until) {
                card=UnityEngine.Object.FindObjectsByType<TileCard>(FindObjectsInactive.Exclude,FindObjectsSortMode.None).FirstOrDefault(c=>c.tileId==tile && c.currentGetTile==drawn && c.IsSelectableForCut());
                if(card==null)yield return null;
            }
            Check(card!=null,"physical cut card missing: "+tile+" drawn="+drawn);
            if(card!=null)typeof(TileCard).GetMethod("OnTileClick",Flags).Invoke(card,null);
        } else GameStateNetworkManager.Instance.SendAction(action,(int?)m["tile"] ?? state.currentAskCutTileId);
        actions++;Save();
    }
    IEnumerator Inspect(JObject m) {
        yield return new WaitForSecondsRealtime(.6f);
        string label=(string)m["label"];var state=NormalGameStateManager.Instance;
        var expected=m["snapshot"].ToObject<GameInfo>();var mirror=TableMirror.Current;
        Check(mirror.IndexToPosition.Count==3,label+": fourth seat present");
        Check(mirror.IndexToPosition.Values.OrderBy(x=>x).SequenceEqual(new[]{"left","right","self"}),label+": incorrect empty seat");
        Check(mirror.SelfHandTiles.OrderBy(x=>x).SequenceEqual(expected.self_hand_tiles.OrderBy(x=>x)),label+": hand mismatch");
        var cards=UnityEngine.Object.FindObjectsByType<TileCard>(FindObjectsInactive.Exclude,FindObjectsSortMode.None).Where(c=>c.transform.parent.name=="HandCardsContainer").ToArray();
        foreach(var p in expected.players_info) {
            string seat=mirror.SeatOf(p.player_index);var actual=mirror.Info(seat);
            Check(actual.score==p.score,label+": score mismatch "+seat);
            Check(actual.hand_tiles_count==p.hand_tiles_count,label+": hand count mismatch "+seat);
            Check(actual.huapai_list.SequenceEqual(p.huapai_list),label+": bonus count mismatch "+seat);
            Check(Field<TMP_Text>(BoardCanvas.Instance,"player_"+seat+"_index").text==new[]{"东","南","西"}[p.player_index],label+": wind mismatch");
        }
        foreach(int id in MalaysiaTileVisual.Ids) {
            Check(TileFaceResolver.LoadSprite(id)!=null,label+": missing hand face "+id);
            Check(TileFaceResolver.LoadTableTexture(id)!=null,label+": missing table face "+id);
        }
        Check(!Field<TMP_Text>(BoardCanvas.Instance,"player_top_score").transform.parent.gameObject.activeSelf,label+": empty board score visible");
        observations.Add(new{label,round=expected.current_round,hand=mirror.SelfHandTiles.ToArray(),scores=mirror.IndexToPosition.ToDictionary(p=>p.Key,p=>mirror.Info(p.Value).score),phase=MalaysiaGameState.Active?.Info?.phase});
        Capture(label);Save();
        _=socket.SendText(JsonConvert.SerializeObject(new{type="qa/inspected",label,failures=failures.Count}));
    }
    IEnumerator Reconnect() {
        NetworkManager.Instance.BeginNewConnection();yield return null;
        float until=Time.realtimeSinceStartup+15;
        while(!NetworkManager.Instance.IsWebSocketOpen && Time.realtimeSinceStartup<until)yield return null;
        Check(NetworkManager.Instance.IsWebSocketOpen,"reconnect failed");Bind();_=socket.SendText("{\"type\":\"qa/restored\"}");Save();
    }
    IEnumerator Replay(JObject m) {
        yield return new WaitForSecondsRealtime(.5f);
        PostGameNavigator.ExitToLobby(forceTeardown:true);yield return null;
        bool prepared=WindowsManager.Instance.TryPrepareRecordScene(out GameRecordManager record,out string error);
        Check(prepared,"replay scene preparation failed: "+error);
        if(!prepared)yield break;
        record.LoadRecord(m["record"].ToString());yield return new WaitForSecondsRealtime(.6f);
        Check(WindowsManager.Instance.GetCurrentWindow()=="recordscene","replay scene not visible");
        Check(record.gameObject.activeInHierarchy,"replay controls are hidden");
        var parsed=GameRecordJsonDecoder.ParseGameRecord(m["record"].ToString());
        foreach(var round in parsed.gameRound.rounds.OrderBy(pair=>pair.Key)) {
            record.GotoSelectRound(round.Key,false);yield return new WaitForSecondsRealtime(.4f);
            int tick=round.Value.actionTicks.Count;
            foreach(int n in new[]{tick,0,tick/2,tick}) {
                record.GotoAction(n);yield return new WaitForSecondsRealtime(.4f);
                Check(Field<List<GameRecordManager.RecordPlayer>>(record,"recordPlayerList").Count==3,"replay fourth player");
                Check(record.indexToPosition.Count==3,"replay fourth seat mapping");
            }
            var last=round.Value.actionTicks.LastOrDefault(t=>t.Count>3 && t[0]=="malaysia" && t[1]=="state");
            if(last!=null) {
                int[] scores=JsonConvert.DeserializeObject<int[]>(last[3]);
                foreach(var p in Field<List<GameRecordManager.RecordPlayer>>(record,"recordPlayerList")) Check(p.score==scores[p.playerIndex],"replay final score mismatch round "+round.Key);
            }
            Capture("replay-round-"+round.Key);
        }
        var rows=ScoreHistoryRecordSettlementExtractor.ExtractScoreRows(parsed);
        Check(rows.Count==parsed.gameRound.rounds.Count,"replay scoreboard row count");
        Capture("replay");Save();
        _=socket.SendText("{\"type\":\"qa/replayed\"}");
    }
    // Hidden Windows swap chains may return a black screen. Render the actual
    // enabled scene cameras and UI into an owned target without changing game state.
    void Capture(string label) {
        var cameras=Camera.allCameras.Where(c=>c.enabled && c.gameObject.activeInHierarchy).OrderBy(c=>c.depth).ToArray();
        Check(cameras.Length>0,label+": no active rendering camera");if(cameras.Length==0)return;
        var overlays=UnityEngine.Object.FindObjectsByType<Canvas>(FindObjectsInactive.Exclude,FindObjectsSortMode.None)
            .Where(c=>c.isRootCanvas && c.renderMode==RenderMode.ScreenSpaceOverlay).ToArray();
        var target=new RenderTexture(1280,720,24,RenderTextureFormat.ARGB32);target.Create();
        var previous=RenderTexture.active;
        foreach(var canvas in overlays){canvas.renderMode=RenderMode.ScreenSpaceCamera;canvas.worldCamera=cameras.Last();canvas.planeDistance=1;}
        Canvas.ForceUpdateCanvases();
        foreach(var camera in cameras) RenderPipeline.SubmitRenderRequest(camera,new UniversalRenderPipeline.SingleCameraRequest{destination=target});
        RenderTexture.active=target;
        var pixels=new Texture2D(1280,720,TextureFormat.RGB24,false);pixels.ReadPixels(new Rect(0,0,1280,720),0,0);pixels.Apply();
        Check(pixels.GetPixels32().Count(p=>p.r>12 || p.g>12 || p.b>12)>1280*720/20,label+": rendered frame is blank");
        File.WriteAllBytes(Path.Combine(output,label+".png"),pixels.EncodeToPNG());
        foreach(var canvas in overlays){canvas.renderMode=RenderMode.ScreenSpaceOverlay;canvas.worldCamera=null;}
        RenderTexture.active=previous;target.Release();Destroy(target);Destroy(pixels);
    }
}
#endif
