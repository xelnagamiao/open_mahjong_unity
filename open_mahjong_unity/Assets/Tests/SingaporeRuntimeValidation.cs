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
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>Explicit loopback-only four-client integration test; normal launches never enter it.</summary>
public sealed class SingaporeRuntimeValidation : MonoBehaviour {
    static string output,endpoint;
    static int uid;
    NativeWebSocket.WebSocket socket;
    readonly List<string> failures=new List<string>();
    readonly List<object> checks=new List<object>();
    int version,ends,actions,reconnects;
    bool ready,settlementInspected;
    float deadline;
    const BindingFlags Flags=BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public;
    static T Find<T>() where T:Component=>Resources.FindObjectsOfTypeAll<T>().First(x=>x.gameObject.scene.IsValid());
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Configure() {
        output=null;var args=Environment.GetCommandLineArgs();int i=Array.IndexOf(args,"--verify-singapore");
        if(i<0 || i+3>=args.Length)return;
        output=Path.GetFullPath(args[i+1]);endpoint=args[i+2];uid=int.Parse(args[i+3]);
        if(!Uri.TryCreate(endpoint,UriKind.Absolute,out var uri)||!uri.IsLoopback||uid<11000001||uid>11000004)
            throw new InvalidOperationException("Singapore validation requires disposable loopback users");
        ConfigManager.gameUrl=endpoint+"/"+uid;ConfigManager.BuildForSteam=false;
    }
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Launch() {
        if(output==null)return;
        Find<ChatManager>().enabled=false;
        new GameObject("SingaporeRuntimeValidation").AddComponent<SingaporeRuntimeValidation>();
    }
    void Save(string status="running") {
        Directory.CreateDirectory(output);
        File.WriteAllText(Path.Combine(output,"result.json"),JsonConvert.SerializeObject(new{status,ready,ends,actions,reconnects,checks,failures,user=uid},Formatting.Indented));
    }
    void Check(bool value,string message) {checks.Add(new{message,passed=value});if(!value)failures.Add(message);Save();}
    void Update() {if(deadline>0 && Time.realtimeSinceStartup>deadline){failures.Add("Client integration deadline exceeded");Save("failed");Application.Quit(2);deadline=0;}}
    IEnumerator Start() {
        Application.runInBackground=true;Application.targetFrameRate=30;QualitySettings.vSyncCount=0;
        Screen.SetResolution(1280,720,FullScreenMode.Windowed);deadline=Time.realtimeSinceStartup+900;
        Application.logMessageReceived+=(m,stack,k)=>{if(k==LogType.Error||k==LogType.Exception){failures.Add(m+"\n"+stack);Save();}};
        float until=Time.realtimeSinceStartup+25;
        while(!NetworkManager.Instance.IsWebSocketOpen && Time.realtimeSinceStartup<until)yield return null;
        Check(NetworkManager.Instance.IsWebSocketOpen,"loopback connection opened");
        UserDataManager.Instance.SetUserInfo("新加坡测试"+(uid-11000000),"qa-only",uid,false);
        Find<UserContainer>().ShowUserSettings(new UserSettings());
        Bind();WindowsManager.Instance.SwitchWindow("menu");ready=true;Save();
        _=socket.SendText("{\"type\":\"qa/ready\"}");
    }
    void Bind() {if(socket!=null)socket.OnMessage-=Receive;socket=NetworkManager.Instance.GetWebSocket();socket.OnMessage+=Receive;}
    void Receive(byte[] bytes) {
        var message=JObject.Parse(System.Text.Encoding.UTF8.GetString(bytes));string type=(string)(message["qa"]??message["type"]);
        if(message["game_info"] is JObject game) {version++;settlementInspected=false;StartCoroutine(InspectStart(game,version));}
        if(type=="qa/plan")StartCoroutine(Act(message,version));
        if(type=="qa/reconnect")StartCoroutine(Reconnect());
        if(type=="qa/assert")StartCoroutine(Inspect(message,version));
        if(type=="qa/replay")StartCoroutine(Replay(message));
        if(type=="qa/done")StartCoroutine(Finish());
        if(type!=null && type.EndsWith("/game_end")){ends++;Save();}
    }
    IEnumerator Idle() {
        float until=Time.realtimeSinceStartup+15;int frames=0;
        while(frames<2) {
            bool busy=GameCanvas.Instance?.IsChangeHandCardProcessing==true || Game3DManager.Instance?.HasPendingRecordTableAnimations==true;
            frames=busy?0:frames+1;
            if(Time.realtimeSinceStartup>until){Check(false,"presentation queues drained");yield break;}
            yield return null;
        }
    }
    IEnumerator InspectStart(JObject game,int v) {
        yield return new WaitForSecondsRealtime(.15f);yield return Idle();if(v!=version)yield break;
        Check(RuleRegistry.ActiveGameState is SingaporeGameState,"Singapore state selected");
        Check(((JArray)game["players_info"]).Count==4,"four-player snapshot");
        foreach(JObject p in game["players_info"]) {
            int index=(int)p["player_index"];string pos=TableMirror.Current.SeatOf(index);
            Check(TableMirror.Current.Info(pos).userId==(int)p["user_id"],"seat identity "+index);
        }
        for(int tile=61;tile<=64;tile++)Check(TileFaceResolver.LoadSprite(tile)!=null && TileFaceResolver.LoadTableTexture(tile)!=null,"animal face "+tile);
        Capture("start-"+version);
    }
    IEnumerator Act(JObject message,int v) {
        string action=(string)message["action"];int tick=(int)message["tick"];
        yield return new WaitForSecondsRealtime(action=="ready"?1.2f:.12f);yield return Idle();
        if(v!=version)yield break;
        if(action=="ready") {
            float until=Time.realtimeSinceStartup+25;
            while((!settlementInspected || !ResultCanConfirm()) && v==version && Time.realtimeSinceStartup<until)yield return null;
            if(v!=version)yield break;
            Check(settlementInspected && ResultCanConfirm(),"visible result confirmation");
            if(ResultCanConfirm())EndResultPanel.Instance.EndButtonClick();
            actions++;Save();yield break;
        }
        var manager=NormalGameStateManager.Instance;
        if(action!="ready" && manager.LastAskActionTick!=tick)yield break;
        if(action=="cut") {
            int tile=(int)message["tile"];bool drawn=(bool)message["drawn"];
            var candidates=UnityEngine.Object.FindObjectsByType<TileCard>(FindObjectsInactive.Exclude,FindObjectsSortMode.None)
                .Where(c=>c.tileId==tile && c.IsSelectableForCut()).ToList();
            var card=candidates.FirstOrDefault(c=>c.currentGetTile==drawn)??candidates.FirstOrDefault();
            if(card==null){Check(false,"selectable cut tile "+tile);yield break;}
            typeof(TileCard).GetMethod("OnTileClick",Flags).Invoke(card,null);
        } else GameStateNetworkManager.Instance.SendAction(action,(int?)message["target"]??0);
        actions++;Save();
    }
    IEnumerator Reconnect() {
        NetworkManager.Instance.BeginNewConnection();float until=Time.realtimeSinceStartup+15;
        while(!NetworkManager.Instance.IsWebSocketOpen && Time.realtimeSinceStartup<until)yield return null;
        Check(NetworkManager.Instance.IsWebSocketOpen,"transport reconnected");Bind();reconnects++;
        _=socket.SendText("{\"type\":\"qa/reconnected\"}");Save();
    }
    IEnumerator Inspect(JObject message,int v) {
        yield return new WaitForSecondsRealtime(.8f);yield return Idle();if(v!=version)yield break;
        bool isWin=(string)message["hu_class"]!="liuju";
        if(isWin) {
            float until=Time.realtimeSinceStartup+25;
            while(!ResultCanConfirm() && v==version && Time.realtimeSinceStartup<until)yield return null;
            if(v!=version)yield break;
            Check(ResultCanConfirm(),"settlement panel visible");
        }
        var scores=message["scores"].ToObject<int[]>();int rounds=(int)message["rounds"];
        foreach(var pair in TableMirror.Current.IndexToPosition) {
            var player=TableMirror.Current.Info(pair.Value);
            Check(player.score==scores[pair.Key],"settled score "+rounds+"/"+pair.Key);
            Check(player.score_history.Count==rounds,"history row count "+rounds+"/"+pair.Key);
            var label=(TMP_Text)typeof(BoardCanvas).GetField("player_"+pair.Value+"_score",Flags).GetValue(BoardCanvas.Instance);
            Check(label.text==scores[pair.Key].ToString(),"visible board score "+rounds+"/"+pair.Key);
        }
        Capture("settled-"+rounds);
        settlementInspected=true;
        _=socket.SendText(JsonConvert.SerializeObject(new{type="qa/asserted",round=rounds}));
    }
    IEnumerator Replay(JObject message) {
        yield return new WaitForSecondsRealtime(2);yield return Idle();
        var detail=message["detail"].ToObject<RecordDetail>();
        NormalGameStateManager.Instance.ResetForExit();RecordPanel.OpenRecord(detail,true);
        yield return new WaitForSecondsRealtime(.8f);yield return Idle();
        var manager=GameRecordManager.Instance;
        foreach(JObject round in message["rounds"]) {
            int index=(int)round["index"];manager.GotoSelectRound(index,false);yield return null;yield return Idle();
            int end=manager.gameRecord.gameRound.rounds[index].actionTicks.Count;
            foreach(int node in new[]{end,0,end}) {
                manager.GotoAction(node);yield return null;yield return Idle();
                CheckRecordTiles(manager,index,node);
                if(node==0)continue;
                var scores=round["scores"].ToObject<int[]>();
                foreach(var p in manager.recordPlayer_to_info.Values)
                    Check(p.score==scores[p.playerIndex],"replay score round "+index+" seat "+p.playerIndex);
            }
            Capture("replay-"+index);yield return null;
        }
        var rows=ScoreHistoryRecordSettlementExtractor.ExtractScoreRows(manager.gameRecord);
        Check(rows.Count==message["rounds"].Count(),"replay history rows");
        var net=message["net"].ToObject<int[]>();
        for(int seat=0;seat<4;seat++)Check(rows.Sum(row=>row.scoreChangesByOriginal[seat])==net[seat],"replay history net "+seat);
        _=socket.SendText("{\"type\":\"qa/replayed\"}");
    }
    IEnumerator Finish() {
        yield return new WaitForSecondsRealtime(.5f);Save(failures.Count==0?"completed":"failed");
        Application.Quit(failures.Count==0?0:1);
    }
    static bool ResultCanConfirm() => EndResultPanel.Instance!=null && EndResultPanel.Instance.gameObject.activeInHierarchy
        && ((UnityEngine.UI.Button)typeof(EndResultPanel).GetField("EndButton",Flags).GetValue(EndResultPanel.Instance)).interactable;
    void CheckRecordTiles(GameRecordManager manager,int round,int node) {
        var tiles=new List<int>((List<int>)typeof(GameRecordManager).GetField("currentTilesList",Flags).GetValue(manager));
        foreach(var p in manager.recordPlayer_to_info.Values) {
            tiles.AddRange(p.tileList);tiles.AddRange(p.huapaiList);tiles.AddRange(p.discardTiles);
            foreach(var mask in p.combinationMasks)for(int i=1;i<mask.Length;i+=2)tiles.Add(mask[i]);
        }
        // Shared replay keeps a ron display copy in the winner's hand while the
        // original river tile remains the animation source. Count that entity once.
        int discarder=(int)typeof(GameRecordManager).GetField("lastDiscardPlayerIndex",Flags).GetValue(manager);
        int addedKong=(int)typeof(GameRecordManager).GetField("lastJiagangPlayerIndex",Flags).GetValue(manager);
        foreach(var p in manager.recordPlayer_to_info.Values.Where(p=>p.hasRonWinningTile && p.tileList.Count>0)) {
            if(addedKong>=0)continue;
            int tile=p.tileList[p.tileList.Count-1];
            var source=manager.recordPlayer_to_info.Values.FirstOrDefault(q=>q.playerIndex==discarder);
            bool hasSource=source!=null && source.discardTiles.Count>0 && source.discardTiles[source.discardTiles.Count-1]==tile;
            Check(hasSource,"ron display copy has source "+round+"/"+node);
            if(hasSource)tiles.Remove(tile);
        }
        var counts=tiles.GroupBy(t=>t).ToDictionary(g=>g.Key,g=>g.Count());
        Check(tiles.Count==148 && counts.Count==46 && counts.All(e=>e.Value==(e.Key>=51?1:4)),
            "replay 148 physical tiles "+round+"/"+node);
    }
    // Hidden Windows players do not present a swap chain. Render the real scene and
    // its overlay canvases to a texture, restoring every canvas immediately after capture.
    void Capture(string name) {
        var camera=Camera.main;
        Check(camera!=null,"capture camera "+name);if(camera==null)return;
        var canvases=UnityEngine.Object.FindObjectsByType<Canvas>(FindObjectsInactive.Exclude,FindObjectsSortMode.None)
            .Where(c=>c.isRootCanvas && c.renderMode==RenderMode.ScreenSpaceOverlay)
            .Select(c=>new {canvas=c,mode=c.renderMode,worldCamera=c.worldCamera,distance=c.planeDistance}).ToArray();
        var target=RenderTexture.GetTemporary(1280,720,24,RenderTextureFormat.ARGB32);
        var previous=RenderTexture.active;var pixels=new Texture2D(1280,720,TextureFormat.RGB24,false);
        try {
            foreach(var saved in canvases) {
                saved.canvas.renderMode=RenderMode.ScreenSpaceCamera;
                saved.canvas.worldCamera=camera;saved.canvas.planeDistance=camera.nearClipPlane+.1f;
            }
            Canvas.ForceUpdateCanvases();
            RenderPipeline.SubmitRenderRequest(camera,new RenderPipeline.StandardRequest {destination=target});
            RenderTexture.active=target;pixels.ReadPixels(new Rect(0,0,1280,720),0,0);pixels.Apply();
            File.WriteAllBytes(Path.Combine(output,name+".png"),pixels.EncodeToPNG());
            Check(pixels.GetPixels32().Count(p=>p.r>8 || p.g>8 || p.b>8)>10000,"rendered image "+name);
        } finally {
            foreach(var saved in canvases) {
                saved.canvas.renderMode=saved.mode;saved.canvas.worldCamera=saved.worldCamera;saved.canvas.planeDistance=saved.distance;
            }
            Canvas.ForceUpdateCanvases();RenderTexture.active=previous;
            RenderTexture.ReleaseTemporary(target);Destroy(pixels);
        }
    }
}
#endif
