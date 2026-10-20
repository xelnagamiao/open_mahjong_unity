#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

/// <summary>Opt-in MainScene integration checks; never connects to a real account.</summary>
public sealed class GuobiaoOpeningFlowersRuntimeValidation : MonoBehaviour {
    static string folder;
    static bool baseline;
    readonly List<object> checks = new List<object>();
    readonly List<string> failures = new List<string>();
    static readonly BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    string currentCase = "boot";
    int completed;
    float deadline;
    NativeWebSocket.WebSocket fixtureSocket;
    int outgoingOrders;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Configure() {
        folder = null;
        var args = Environment.GetCommandLineArgs();
        int index = Array.IndexOf(args, "--verify-guobiao-opening");
        if (index < 0 || index + 1 >= args.Length) return;
        folder = Path.GetFullPath(args[index + 1]);
        baseline = args.Contains("--baseline");
        ConfigManager.BuildForSteam = false;
        Application.runInBackground = true;
    }
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Launch() {
        if (folder == null) return;
        NetworkManager.Instance.enabled = false;
        foreach (var chat in Resources.FindObjectsOfTypeAll<ChatManager>()) chat.enabled = false;
        new GameObject("Guobiao opening validation").AddComponent<GuobiaoOpeningFlowersRuntimeValidation>();
    }
    void Check(bool passed, string name, object detail = null) {
        checks.Add(new { currentCase, name, passed, detail });
        if (!passed) failures.Add(currentCase + ": " + name);
    }
    void Save(string status) => File.WriteAllText(Path.Combine(folder,"runtime-results.json"),
        JsonConvert.SerializeObject(new { status, completed, currentCase, checks, failures }, Formatting.Indented));
    void Log(string text, string stack, LogType type) {
        if (type == LogType.Error || type == LogType.Exception || text.StartsWith("牌池中已无可用对象"))
            failures.Add(currentCase + ": " + text + "\n" + stack);
    }
    void Update() {
#if !UNITY_WEBGL || UNITY_EDITOR
        fixtureSocket?.DispatchMessageQueue();
#endif
        if (deadline > 0 && Time.realtimeSinceStartup > deadline) {
            failures.Add("Runtime deadline exceeded"); Save("failed"); deadline=0; Application.Quit(2);
        }
    }
    IEnumerator Start() {
        Application.targetFrameRate=60; QualitySettings.vSyncCount=0; Time.timeScale=8;
        Application.logMessageReceived+=Log;
        deadline=Time.realtimeSinceStartup+1800;
        yield return null;
        yield return ConnectFixtureTransport();
        var fixtures=JArray.Parse(File.ReadAllText(Path.Combine(folder,"runtime-fixtures.json")));
        foreach (JObject fixture in fixtures) {
            if (baseline && (!((string)fixture["name"]).StartsWith("standard-0-") || !(bool)fixture["spectator"])) continue;
            foreach (bool burst in new[] { false,true }) {
                currentCase=(string)fixture["name"]+(burst ? "-burst" : "-paced"); Save("running");
                yield return Guard(RunLive(fixture,burst)); completed++;
            }
            if (!baseline && (bool)fixture["spectator"] && ((string)fixture["name"]).Contains("chain")) {
                foreach (bool visible in new[] { false,true }) {
                    currentCase=(string)fixture["name"]+"-record-"+visible; Save("running");
                    yield return Guard(RunRecord(fixture,visible)); completed++;
                }
            }
        }
        NormalGameStateManager.Instance.ResetForExit();
        Save(failures.Count==0 ? "completed" : "failed");
        Application.Quit(failures.Count==0 ? 0 : 1);
    }
    IEnumerator Guard(IEnumerator routine) {
        var stack=new Stack<IEnumerator>(); stack.Push(routine);
        while (stack.Count>0) {
            object result=null; bool more;
            try { more=stack.Peek().MoveNext(); if (more) result=stack.Peek().Current; else stack.Pop(); }
            catch (Exception error) { failures.Add(currentCase+": "+error); yield break; }
            if (!more) continue;
            if (result is IEnumerator nested) stack.Push(nested); else yield return result;
        }
    }
    static bool HandoffPending => (bool?)GuobiaoGameState.Active?.GetType()
        .GetProperty("OpeningHandoffPending")?.GetValue(GuobiaoGameState.Active) == true;
    IEnumerator Idle() {
        float limit=Time.realtimeSinceStartup+20;
        // Layout snapshots can schedule an automatic sort on the following frame.
        // Require consecutive quiet frames instead of sampling midway through it.
        int quietFrames=0;
        while (quietFrames<4) {
            if (Time.realtimeSinceStartup>limit) throw new Exception("Table presentation did not finish");
            yield return null;
            bool busy=HandoffPending || GameCanvas.Instance.IsChangeHandCardProcessing
                || GameCanvas.Instance.IsHandReflowAnimating || Game3DManager.Instance.HasPendingRecordTableAnimations;
            quietFrames=busy?0:quietFrames+1;
        }
    }

    IEnumerator ConnectFixtureTransport() {
        var args=Environment.GetCommandLineArgs();
        int index=Array.IndexOf(args,"--fixture-ws");
        if(index<0) yield break;
        var uri=new Uri(args[index+1]);
        if(!uri.IsLoopback) throw new Exception("Fixture transport must be local");
        fixtureSocket=new NativeWebSocket.WebSocket(uri.ToString());
        fixtureSocket.OnMessage+=bytes=> {
            var request=JObject.Parse(System.Text.Encoding.UTF8.GetString(bytes));
            // The normal table also refreshes its spectator roster on entry.
            if((string)request["type"]=="friend/list_realtime_spectators") return;
            Check((string)request["type"]=="gamestate/guobiao/reorder_hand","only hand order is sent to fixture transport",request);
            outgoingOrders++;
            if((string)request["gamestate_id"]!=UserDataManager.Instance.GamestateId) return;
            var layout=(WashizuHandLayout)typeof(GameCanvas).GetField("washizuSelfLayout",Hidden).GetValue(GameCanvas.Instance);
            GameCanvas.Instance.ReceiveWashizuSelfLayout(layout,new Response { success=true,hand_order_request_id=(int)request["request_id"] });
        };
        typeof(NetworkManager).GetField("websocket",Hidden).SetValue(NetworkManager.Instance,fixtureSocket);
        _=fixtureSocket.Connect();
        float timeout=Time.realtimeSinceStartup+10;
        while(fixtureSocket.State!=NativeWebSocket.WebSocketState.Open) {
            if(Time.realtimeSinceStartup>timeout) throw new Exception("Fixture WebSocket did not connect");
            yield return null;
        }
    }
    void Send(JToken packet) => GameStateNetworkManager.Instance.HandleGameStateMessage(packet.ToObject<Response>());
    void CheckRows(JObject fixture, bool record = false, bool visible = false, bool separateDraw = false) {
        var expected=(JArray)fixture["expected"];
        int self=(int)fixture["seat"];
        var mapping=record ? GameRecordManager.Instance.indexToPosition : TableMirror.Current.IndexToPosition;
        foreach (var item in mapping) {
            int[] hand=expected[item.Key]["hand"].ToObject<int[]>();
            int? draw=(int?)expected[item.Key]["draw"];
            bool pinned=separateDraw && draw.HasValue;
            string seat=item.Value;
            if(expected[item.Key]["flowers"] is JArray flowers)
                Check(Game3DManager.Instance.GetPosPanel(seat).buhuaPosition.GetComponentsInChildren<Tile3D>()
                    .Select(t=>t.GetTileId()).OrderBy(t=>t).SequenceEqual(flowers.ToObject<int[]>().OrderBy(t=>t)),"exposed flower identities "+seat);
            if (seat=="self") {
                var cards=GameCanvas.Instance.HandCardsContainer.GetComponentsInChildren<TileCard>();
                Check(cards.Select(c=>c.tileId).OrderBy(t=>t).SequenceEqual(hand.OrderBy(t=>t)),"2D tile multiset");
                Check(cards.Count(c=>c.isDrawSlotPinned)==(pinned?1:0),"correct number of independent 2D draw slots");
                Check(cards.Count(c=>c.currentGetTile)==(draw.HasValue?1:0),"semantic draw source is retained only for first actor");
                if(draw.HasValue) Check(cards.SingleOrDefault(c=>c.currentGetTile)?.tileId==draw,"correct replacement owns moqie source");
                var xs=cards.Where(c=>!c.isDrawSlotPinned).Select(c=>((RectTransform)c.transform).anchoredPosition.x).OrderBy(x=>x).ToArray();
                var gaps=xs.Skip(1).Select((x,i)=>x-xs[i]).ToArray();
                Check(gaps.Length<2 || gaps.All(x=>x>0 && Math.Abs(x-gaps[0])<.05f),"2D hand has no gap",gaps);
            } else {
                var panel=Game3DManager.Instance.GetPosPanel(seat);
                Transform row=record && visible ? panel.ShowCardsPosition : panel.cardsPosition;
                var dir=seat=="left"?Vector3.back:seat=="top"?Vector3.left:Vector3.forward;
                var positions=row.Cast<Transform>().Select(t=>Vector3.Dot(t.position,dir)).OrderBy(x=>x).ToArray();
                var gaps=positions.Skip(1).Select((x,i)=>x-positions[i]).ToArray();
                float step=(float)typeof(Game3DManager).GetField("handStep",Hidden).GetValue(Game3DManager.Instance);
                Check(positions.Length==hand.Length,"3D count "+seat,new {expected=hand.Length,actual=positions.Length});
                Check((pinned?gaps.Take(gaps.Length-1):gaps).All(x=>Math.Abs(x-step)<.025f),"3D contiguous row "+seat,gaps);
                if(pinned) Check(gaps.Last()>step,"new normal draw stays separate "+seat,gaps.Last());
                if(record && visible) Check(row.GetComponentsInChildren<Tile3D>().Select(t=>t.GetTileId()).OrderBy(t=>t)
                    .SequenceEqual(hand.OrderBy(t=>t)),"revealed hand identities "+seat);
            }
        }
    }
    IEnumerator RunLive(JObject fixture,bool burst) {
        Time.timeScale=(float?)fixture["timeScale"]??8;
        var manager=NormalGameStateManager.Instance;
        manager.ResetForExit();
        int seat=(int)fixture["seat"];
        UserDataManager.Instance.SetUserInfo("国标补花回归","fixture-only",101+seat,false);
        GameSession.Current.IsRealtimeSpectator=(bool)fixture["spectator"];
        Send(fixture["initial"]); yield return Idle();
        // Packet fixtures drive actions; do not let locally saved automation submit network requests.
        foreach (string field in new[] { "isAutoBuhua", "isAutoHepai", "isAutoPass", "isAutoCut" })
            typeof(AutoAction).GetField(field,Hidden).SetValue(AutoAction.Instance,false);
        AutoAction.Instance.SetAutoArrangeHandCards((bool)fixture["autoArrange"]);
        foreach (var packet in (JArray)fixture["packets"]) {
            Send(packet);
            if (!burst) yield return Idle();
        }
        yield return Idle(); CheckRows(fixture); CheckOpeningAsk(fixture);
        Check(GuobiaoGameState.Active!=null,"factory owns Guobiao session");
        Check(GameSession.Current.IsRealtimeSpectator==(bool)fixture["spectator"],"spectator mode survives game start");
        if (((string)fixture["name"]).StartsWith("standard-0-") && (bool)fixture["spectator"] && burst) Capture("opening-seat"+seat+".png");
        // Reconnect and repeated end-of-opening prompt must converge to the same hand.
        Send(fixture["restore"]);
        // Idle viewers receive the snapshot without a pending action prompt.
        if(seat!=0) { yield return Idle(); CheckRows(fixture); }
        Send(((JArray)fixture["packets"]).Last);
        Send(((JArray)fixture["packets"]).Last);
        yield return Idle(); CheckRows(fixture); CheckOpeningAsk(fixture);
        if (((string)fixture["name"]).StartsWith("standard-0-") && (bool)fixture["spectator"] && burst) Capture("reconnected-seat"+seat+".png");
        if (!baseline && fixture["midround"] is JArray midround) {
            List<int> beforeFlower=null;
            foreach(var packet in midround) {
                var action=packet["do_action_info"];
                if(action!=null && (int)action["action_player"]==seat && action["buhua_tile"]?.Type==JTokenType.Integer) {
                    yield return Idle();
                    beforeFlower=GameCanvas.Instance.HandCardsContainer.GetComponentsInChildren<TileCard>().Select(c=>c.tileId).ToList();
                    beforeFlower.Remove((int)action["buhua_tile"]);
                }
                Send(packet);
                if(!burst) yield return Idle();
            }
            yield return Idle();
            var expected=(JObject)fixture.DeepClone(); expected["expected"]=fixture["midroundExpected"].DeepClone();
            CheckRows(expected,separateDraw:true);
            if(beforeFlower!=null && !(bool)fixture["autoArrange"])
                Check(GameCanvas.Instance.GetMainHandCardsOrdered().Select(c=>c.tileId).SequenceEqual(beforeFlower),"hand flower replacement preserves manual survivor order");
            if(burst && (bool)fixture["spectator"]) Capture(((string)fixture["name"]).Split('-')[1]+"-midround-seat"+seat+".png");
        }
        if (!baseline && burst && (bool)fixture["spectator"] && ((string)fixture["name"]).Contains("-chain-"))
            yield return ProbeHandoffLifecycle(fixture);
    }
    IEnumerator RunRecord(JObject fixture,bool visible) {
        var manager=NormalGameStateManager.Instance; manager.ResetForExit();
        manager.StopAsRealtimeSpectator(); PlayerSession.Current.SetGamestateId("");
        GameHost.Current.SwitchWindow("game");
        var record=GameRecordManager.Instance; record.ResetForSessionEnd();
        typeof(RecordSetting).GetField("isShowCardsMode",Hidden).SetValue(RecordSetting.Instance,visible);
        record.LoadRecord(fixture["record"].ToString(),fixture["players"].ToObject<PlayerRecordInfo[]>());
        yield return Idle(); record.SwitchRecordPerspectiveToOriginalPlayerIndex((int)fixture["seat"]); yield return Idle();
        var ticks=record.gameRecord.gameRound.rounds[1].actionTicks;
        int reset=ticks.FindIndex(t=>t[0]=="reset");
        Check(reset>=0,"existing record contains explicit opening boundary");
        record.GotoAction(reset+1); yield return Idle(); CheckRows(fixture,true,visible);
        record.GotoAction(0); yield return Idle();
        int guard=0;
        while(record.currentNode<ticks.Count && guard++<ticks.Count+2) {
            record.AdvanceToNextAction(); yield return Idle();
        }
        Check(record.currentNode==ticks.Count,"sequential playback reaches opening boundary");
        CheckRows(fixture,true,visible);
        if ((string)fixture["name"]=="standard-chain-seat0-spectatorTrue" && visible) Capture("replay-expanded.png");
        record.ResetForSessionEnd(); record.HideGameRecord(); manager.ResetForExit();
        var source=(JObject)fixture["record"].DeepClone();
        var updates=new JArray(new JObject { ["type"]="ticks", ["round_index"]=1,
            ["new_ticks"]=source["game_round"]["round_index_1"]["action_ticks"].DeepClone() });
        source["game_round"]["round_index_1"]["action_ticks"]=new JArray();
        GameRecordManager.PrepareDelayedSpectatorSession("guobiao-opening-validation");
        record.StartSpectating(source.ToString());
        record.SwitchRecordPerspectiveToOriginalPlayerIndex((int)fixture["seat"]); yield return Idle();
        record.AppendSpectatorTicks(updates.ToString());
        float deadline=Time.realtimeSinceStartup+40;
        int target=record.gameRecord.gameRound.rounds[1].actionTicks.Count;
        while(record.currentNode<target && Time.realtimeSinceStartup<deadline) yield return null;
        yield return Idle();
        Check(record.currentNode==target,"delayed spectator consumes complete opening round");
        CheckRows(fixture,true,visible);
        record.SwitchToRecordMode(); record.GotoAction(0); yield return Idle();
        record.SwitchToLiveMode(); yield return Idle();
        CheckRows(fixture,true,visible);
        record.ResetForSessionEnd(); record.HideGameRecord(); manager.ResetForExit();
    }

    void CheckOpeningAsk(JObject fixture) {
        int seat=(int)fixture["seat"];
        var ask=((JArray)fixture["packets"]).Last["ask_hand_action_info"];
        int actor=(int)ask["player_index"];
        var clock=TurnClock.Current;
        Check(clock.CurrentPlayer==TableMirror.Current.IndexToPosition[actor],"opening completion restores acting player indicator");
        Check(clock.LastAskActionTick==(int)ask["action_tick"],"opening completion retains authoritative action tick");
        Check(clock.IsSelfActionRequired==(seat==actor),"opening completion restores the correct input owner");
        if(seat==actor) {
            Check(clock.AllowActionList.SequenceEqual(ask["action_list"].ToObject<string[]>()),"opening completion restores allowed actions");
            Check(GameCanvas.Instance.IsCountdownRunning,"opening completion starts decision clock");
        }
        Check(TableMirror.Current.LastDealTileId==((int?)fixture["expected"][seat]["draw"]??0),"mirror and hand agree on semantic draw source");
    }

    IEnumerator ProbeHandoffLifecycle(JObject fixture) {
        var continuation=(JArray)fixture["continuation"];
        int drawn=(int)fixture["continuationDraw"];
        Send(fixture["initial"]);
        foreach(var packet in (JArray)fixture["packets"]) Send(packet);
        Check(HandoffPending,"opening barrier captures same-frame completion");
        foreach(var packet in continuation) Send(packet);
        yield return Idle();
        foreach(var pair in TableMirror.Current.IndexToPosition) {
            int size=fixture["expected"][pair.Key]["hand"].Count()+(pair.Key==0?-1:pair.Key==1?1:0);
            if(pair.Value=="self") {
                var cards=GameCanvas.Instance.HandCardsContainer.GetComponentsInChildren<TileCard>();
                Check(cards.Length==size,"queued action preserves 2D count");
                Check(cards.Count(c=>c.isDrawSlotPinned)==(pair.Key==1?1:0),"next normal draw keeps its independent slot");
                if(pair.Key==1) Check(cards.SingleOrDefault(c=>c.currentGetTile)?.tileId==drawn,"next normal draw owns semantic marker");
            } else {
                var row=Game3DManager.Instance.GetPosPanel(pair.Value).cardsPosition;
                Check(row.childCount==size,"queued action preserves 3D count "+pair.Value);
                Vector3 dir=pair.Value=="left"?Vector3.back:pair.Value=="top"?Vector3.left:Vector3.forward;
                var xs=row.Cast<Transform>().Select(t=>Vector3.Dot(t.position,dir)).OrderBy(x=>x).ToArray();
                float step=(float)typeof(Game3DManager).GetField("handStep",Hidden).GetValue(Game3DManager.Instance);
                Check(pair.Key==1 ? xs.Last()-xs[xs.Length-2]>step : xs.Skip(1).Select((x,i)=>x-xs[i]).All(x=>Math.Abs(x-step)<.03f),
                    "handoff cannot consume the following normal draw "+pair.Value);
            }
        }
        // A snapshot cancels the old state's queued actions and animation ownership.
        Send(fixture["initial"]);
        foreach(var packet in (JArray)fixture["packets"]) Send(packet);
        Send(continuation[0]);
        Send(fixture["restore"]); Send(((JArray)fixture["packets"]).Last);
        yield return Idle(); CheckRows(fixture);
        // Exiting at the barrier must not reopen an ask window later.
        Send(fixture["initial"]);
        foreach(var packet in (JArray)fixture["packets"]) Send(packet);
        NormalGameStateManager.Instance.ResetForExit();
        yield return Idle();
        Check(GuobiaoGameState.Active==null && !NormalGameStateManager.Instance.IsSelfActionRequired,"exit cancels phase handoff");
    }
    void Capture(string filename) {
        if(SystemInfo.graphicsDeviceType==UnityEngine.Rendering.GraphicsDeviceType.Null) return;
        var camera=Camera.main;
        if(camera==null) throw new Exception("No table camera for visual capture");
        var canvases=FindObjectsByType<Canvas>(FindObjectsSortMode.None).Where(c=>c.isRootCanvas && c.renderMode==RenderMode.ScreenSpaceOverlay).ToArray();
        var oldCameras=canvases.Select(c=>c.worldCamera).ToArray();
        var oldDistances=canvases.Select(c=>c.planeDistance).ToArray();
        var texture=new RenderTexture(1600,900,24);
        var active=RenderTexture.active; var target=camera.targetTexture;
        try {
            camera.targetTexture=texture;
            foreach(var canvas in canvases) {canvas.renderMode=RenderMode.ScreenSpaceCamera;canvas.worldCamera=camera;canvas.planeDistance=camera.nearClipPlane+.1f;}
            Canvas.ForceUpdateCanvases(); camera.Render(); RenderTexture.active=texture;
            var png=new Texture2D(1600,900,TextureFormat.RGB24,false);
            png.ReadPixels(new Rect(0,0,1600,900),0,0); png.Apply();
            File.WriteAllBytes(Path.Combine(folder,filename),png.EncodeToPNG()); Destroy(png);
        } finally {
            for(int i=0;i<canvases.Length;i++){canvases[i].renderMode=RenderMode.ScreenSpaceOverlay;canvases[i].worldCamera=oldCameras[i];canvases[i].planeDistance=oldDistances[i];}
            camera.targetTexture=target; RenderTexture.active=active; Destroy(texture);
        }
    }
}
#endif
