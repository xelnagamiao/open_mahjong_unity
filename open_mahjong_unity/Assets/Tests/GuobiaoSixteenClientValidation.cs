#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

public static class GuobiaoSixteenClientValidation {
    static string Root, Accounts, Replay;
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    static string Label;
    static bool Bound, AutoPlay;
    static float ActionDelay = .18f;
    static NativeWebSocket.WebSocket Socket;
    static int LastActed = -999, Starts, Settlements, Ends, Logins, WinningPanels, ExpectedWinningPanels;
    static readonly List<string> Failures = new List<string>();
    static readonly List<string> Checks = new List<string>();
    static CreatePanel Panel;
    static JObject LastRoom, LastGame;
    static object Call(string name, params object[] args) => typeof(CreatePanel).GetMethod(name, Private).Invoke(Panel, args);
    static T Field<T>(string name) => (T)typeof(CreatePanel).GetField(name, Private).GetValue(Panel);
    static void Check(bool condition, string name) {
        (condition ? Checks : Failures).Add(name);
        Save();
    }
    static void Save() {
        File.WriteAllText(Root + "/" + Label + "-state.json", JsonConvert.SerializeObject(new {
            checks=Checks, failures=Failures, starts=Starts, settlements=Settlements, ends=Ends,
            winningPanels=WinningPanels, expectedWinningPanels=ExpectedWinningPanels,
            lastRoom=LastRoom, lastGame=LastGame,
            user=UserDataManager.Instance?.UserId, room=UserDataManager.Instance?.RoomId,
            game=UserDataManager.Instance?.GamestateId
        }, Formatting.Indented));
    }
    public static object Start(string label) {
        if (!Application.isPlaying || Bound) throw new InvalidOperationException("Start once in running MainScene");
        if (ConfigManager.gameUrl != "ws://localhost:8081/game") throw new InvalidOperationException("Local server required");
        Label=label; Bound=true; Directory.CreateDirectory(Root);
        Application.runInBackground=true; Application.targetFrameRate=30; QualitySettings.vSyncCount=0;
        Screen.SetResolution(1920,1080,FullScreenMode.Windowed);
        Application.logMessageReceived += (message,stack,type) => {
            if (type==LogType.Error || type==LogType.Exception || type==LogType.Assert)
                File.AppendAllText(Root+"/"+Label+"-errors.txt",message+"\n"+stack+"\n");
        };
        NetworkManager.Instance.StartCoroutine(Loop());
        Save(); return "Audit attached";
    }
    static IEnumerator Loop() {
        while(Bound) {
            var net=NetworkManager.Instance;
            if(Socket!=net.GetWebSocket()) {
                if(Socket!=null) Socket.OnMessage-=Receive;
                Socket=net.GetWebSocket();
                if(Socket!=null)Socket.OnMessage+=Receive;
            }
            var game=NormalGameStateManager.Instance;
            if(AutoPlay && game!=null && game.allowActionList!=null && game.allowActionList.Count>0 && game.LastAskActionTick!=LastActed) {
                int tick=game.LastAskActionTick;
                yield return new WaitForSecondsRealtime(ActionDelay);
                if(tick==game.LastAskActionTick && AutoPlay) {
                    var actions=game.allowActionList;
                    string chosen=new[]{"buhua","hu_self","hu_first","hu_second","hu_third","pass"}.FirstOrDefault(actions.Contains);
                    if(chosen!=null) {
                        LastActed=tick;
                        GameStateNetworkManager.Instance.SendAction(chosen,game.currentAskCutTileId);
                    } else if(actions.Contains("cut")) {
                        var cards=UnityEngine.Object.FindObjectsByType<TileCard>(FindObjectsInactive.Exclude,FindObjectsSortMode.None);
                        var card=cards.Where(c=>c.IsSelectableForCut() && c.tileId!=59).OrderByDescending(c=>c.currentGetTile).FirstOrDefault();
                        if(card!=null) {
                            LastActed=tick;
                            typeof(TileCard).GetMethod("OnTileClick",Private).Invoke(card,null);
                        }
                    }
                }
            }
            yield return null;
        }
    }
    static void Receive(byte[] bytes) {
        var message=JObject.Parse(System.Text.Encoding.UTF8.GetString(bytes));
        string type=(string)message["type"];
        if(type=="login" && (bool?)message["success"]==true)Logins++;
        if(type.StartsWith("room/") || type.StartsWith("gamestate/") || type.StartsWith("spectator/"))
            File.AppendAllText(Root+"/"+Label+"-messages.jsonl",message.ToString(Formatting.None)+"\n");
        if(message["room_info"] is JObject room)LastRoom=room;
        if(message["game_info"] is JObject game) { LastGame=game; Starts++; }
        if(type.EndsWith("/show_result")) {
            Settlements++;
            if((int?)message["show_result_info"]?["hepai_player_index"]>=0) {
                ExpectedWinningPanels++;
                NetworkManager.Instance.StartCoroutine(Guard(ReadyAfterResult(Settlements)));
            }
        }
        if(type.EndsWith("/game_end"))Ends++;
        Save();
    }
    public static object SetAuto(bool value) { AutoPlay=value; LastActed=-999; return value; }
    public static object SetDelay(float value) { ActionDelay=value; return value; }
    public static object Login(int account) {
        var item=JArray.Parse(File.ReadAllText(Accounts))[account];
        NetworkManager.Instance.Login((string)item["username"],(string)item["password"]);
        return "Local test login requested";
    }
    public static object Reconnect(int account) {
        NetworkManager.Instance.StartCoroutine(ReconnectRoutine(account)); return "Reconnect started";
    }
    static IEnumerator ReconnectRoutine(int account) {
        bool wasAuto=AutoPlay; AutoPlay=false;
        int previousStarts=Starts; string previousGame=UserDataManager.Instance.GamestateId;
        var previousRules=LastGame?["detailed_config"]?.DeepClone();
        NetworkManager.Instance.BeginNewConnection();
        double until=Time.realtimeSinceStartupAsDouble+15;
        while(!NetworkManager.Instance.IsWebSocketOpen && Time.realtimeSinceStartupAsDouble<until)yield return null;
        Check(NetworkManager.Instance.IsWebSocketOpen,"reconnect-websocket-open");
        int previousLogins=Logins;Login(account);
        yield return WaitFor(()=>Logins>previousLogins,"reconnect login confirmed");
        Button ReconnectButton()=>UnityEngine.Object.FindObjectsByType<MessagePrefab>(FindObjectsSortMode.None).SelectMany(p=>p.GetComponentsInChildren<Button>()).FirstOrDefault(b=>b.GetComponentsInChildren<TMP_Text>().Any(t=>t.text=="重新连接"));
        yield return WaitFor(()=>ReconnectButton()!=null,"reconnect-labelled-button-present");
        ReconnectButton().onClick.Invoke();
        until=Time.realtimeSinceStartupAsDouble+15;
        while(Starts<=previousStarts && Time.realtimeSinceStartupAsDouble<until)yield return null;
        Check(Starts>previousStarts && UserDataManager.Instance.GamestateId==previousGame,"reconnect-same-game-restored");
        Check(JToken.DeepEquals(LastGame?["detailed_config"],previousRules),"reconnect-detailed-rules-preserved");
        Check((string)LastGame?["sub_rule"]=="guobiao/sixteen","reconnect sixteen subrule restored");
        Check((string)LastGame?["four_jokers_hints"]?["sub_rule"]=="guobiao/sixteen","reconnect sixteen hints restored");
        AutoPlay=wasAuto; LastActed=-999; Save();
    }
    public static object OpenCreate() {
        WindowsManager.Instance.SwitchWindow("room");
        RoomWindowsManager.Instance.SwitchRoomWindow("createRoom");
        return "Opened";
    }
    public static object PrepareCreate() {
        Panel=UnityEngine.Object.FindObjectsByType<CreatePanel>(FindObjectsInactive.Include,FindObjectsSortMode.None).First(p=>p.gameObject.activeInHierarchy);
        var rules=Field<TMP_Dropdown>("chooseRule");
        rules.value=rules.options.FindIndex(o=>o.text.Contains("国标"));
        var sub=Field<TMP_Dropdown>("SubRuleDropdown");
        int index=sub.options.FindIndex(o=>o.text.Contains("十六张"));
        Check(index>=0,"sixteen-MCR-dropdown-present");sub.value=index;
        Check(Field<Toggle>("GuobiaoFlowersToggle").isOn,"flowers-enabled-by-default");
        Check(Field<TMP_InputField>("HepaiLimitInput").text=="8","eight-fan-default");
        Check(Field<Toggle>("InputHepaiLimitToggle").GetComponentInChildren<TMP_Text>(true).text=="修改起和番","win-threshold toggle caption visible");
        Check(!Field<Toggle>("DuplicateWallToggle").gameObject.activeInHierarchy,"standard-duplicate-hidden");
        Check(Field<Toggle>("TianDiRenHeToggle").gameObject.activeInHierarchy,"MCR-opening-win-setting-available");
        Field<TMP_InputField>("roomNameInput").text="十六张国标 Unity 独立客户端实测";
        Field<Toggle>("TouristLimitToggle").isOn=false;
        Field<Toggle>("SetRandomSeedToggle").isOn=true;
        Field<TMP_InputField>("randomSeedInput").text=new string('0',56)+"20261014";
        Field<Toggle>("gameTime1Button").isOn=true;
        Field<Toggle>("TacticalCallToggle").isOn=false;
        Field<Toggle>("tipsToggle").isOn=true;
        Field<TMP_Dropdown>("roundTimer").value=5;
        Field<TMP_Dropdown>("stepTimer").value=5;
        Save();return "MCR sixteen create panel prepared";
    }
    public static object Create() {
        LastRoom=null;LastGame=null;Call("CreateRoom");return "Create requested";
    }
    public static object Begin() {
        ConfigManager.gameUrl="ws://localhost:8081/game"; ConfigManager.chatUrl="ws://localhost:8083/chat"; ConfigManager.webApiUrl="http://localhost:3000"; NetworkManager.Instance.BeginNewConnection(); return Start("live-client");
    }
    public static object Bots() {NetworkManager.Instance.StartCoroutine(AddBots());return "Adding bots";}
    static IEnumerator AddBots() {
        for(int count=2;count<=3;count++) {
            string room=UserDataManager.Instance.RoomId;
            if(count==3)RoomNetworkManager.Instance.AddGuobiaoHeuristicBotToRoom(room); else RoomNetworkManager.Instance.AddSmartBotToRoom(room);
            double until=Time.realtimeSinceStartupAsDouble+10;
            while((LastRoom?["player_list"]?.Count()??0)<count && Time.realtimeSinceStartupAsDouble<until)yield return null;
            Check(LastRoom?["player_list"]?.Count()==count,"bot roster "+count);
        }
        // A second test client keeps the match alive while this client disconnects.
        // The normal lifecycle intentionally closes a game when every human leaves.
        yield return WaitFor(()=>LastRoom?["player_list"]?.Count()==4 && LastRoom?["ready_list"]?.Any(uid=>(int)uid>=10 && (int)uid!=UserDataManager.Instance.UserId)==true,"second local client joined and ready",60);
        RoomNetworkManager.Instance.SetBotSpeed(UserDataManager.Instance.RoomId,"instant");
        yield return new WaitForSecondsRealtime(.5f);
        RoomNetworkManager.Instance.StartGame(UserDataManager.Instance.RoomId,(int?)LastRoom?["roster_revision"]);
    }
    public static object Leave() {AutoPlay=false;RoomNetworkManager.Instance.LeaveRoom(UserDataManager.Instance.RoomId);return "Leaving";}
    public static object State() {
        Save(); return new {checks=Checks.Count,failures=Failures,starts=Starts,settlements=Settlements,ends=Ends,
            room=UserDataManager.Instance.RoomId,user=UserDataManager.Instance.UserId,
            tick=NormalGameStateManager.Instance.LastAskActionTick,actions=NormalGameStateManager.Instance.allowActionList,
            lastRoom=LastRoom,lastGame=LastGame};
    }
    static string Option(string name, string environment) {
        var args=Environment.GetCommandLineArgs(); int i=Array.IndexOf(args,name);
        return i>=0 && i+1<args.Length ? args[i+1] : Environment.GetEnvironmentVariable(environment);
    }
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Configure() {
        Root=Option("--verify-guobiao-sixteen", "OM_GB16_AUDIT_OUTPUT");
        Accounts=Option("--guobiao-sixteen-accounts", "OM_GB16_AUDIT_ACCOUNTS");
        Replay=Option("--guobiao-sixteen-replay", "OM_GB16_AUDIT_REPLAY");
        if(string.IsNullOrEmpty(Root))return;
        Root=Path.GetFullPath(Root);Directory.CreateDirectory(Root);
        if(string.IsNullOrEmpty(Accounts)||!File.Exists(Accounts))throw new InvalidOperationException("Explicit local test accounts file required");
        ConfigManager.gameUrl="ws://localhost:8081/game";ConfigManager.chatUrl="ws://localhost:8083/chat";
        ConfigManager.webApiUrl="http://localhost:3000";
    }
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Launch() {
        if(string.IsNullOrEmpty(Root))return;
        NetworkManager.Instance.StartCoroutine(Guard(Automated()));
    }
    static IEnumerator Guard(IEnumerator routine) {
        var stack=new Stack<IEnumerator>();stack.Push(routine);
        while(stack.Count>0) {
            object current;
            try {if(!stack.Peek().MoveNext()){stack.Pop();continue;}current=stack.Peek().Current;}
            catch(Exception e) {Failures.Add(e.ToString());Save();File.WriteAllText(Root+"/runtime-failed.txt",e.ToString());break;}
            if(current is IEnumerator nested){stack.Push(nested);continue;}
            yield return current;
        }
    }
    static IEnumerator WaitFor(Func<bool> condition,string label,float seconds=30) {
        double end=Time.realtimeSinceStartupAsDouble+seconds;
        while(!condition()&&Time.realtimeSinceStartupAsDouble<end)yield return null;
        Check(condition(),label);
        if(!condition())throw new InvalidOperationException(label);
    }
    static IEnumerator ReadyAfterResult(int round) {
        // The final game_end arrives before the winning animation finishes. Wait for
        // the real panel and confirm it before leaving, instead of using a fixed delay.
        var panel=EndResultPanel.Instance;
        yield return WaitFor(()=>panel.gameObject.activeInHierarchy,"winning panel visible round "+round);
        var button=(Button)typeof(EndResultPanel).GetField("EndButton",Private).GetValue(panel);
        yield return WaitFor(()=>button.interactable,"all winning fan entries revealed round "+round);
        var total=(GameObject)typeof(EndResultPanel).GetField("FanCountTotalPanel",Private).GetValue(panel);
        Check(total.activeInHierarchy,"winning total displayed round "+round);
        var labels=panel.GetComponentsInChildren<TMP_Text>(false);
        foreach(var label in labels)label.ForceMeshUpdate();
        var missing=labels.Where(t=>t.font!=null && !t.font.HasCharacters(t.GetParsedText(),out uint[] _,true,false)).Select(t=>t.GetParsedText()).ToArray();
        Check(missing.Length==0,"winning panel has every font glyph round "+round);
        File.WriteAllText(Root+"/settlement-"+round+"-text.json",JsonConvert.SerializeObject(new {
            labels=labels.Select(t=>t.GetParsedText()),missing,matchEnd=panel.IsMatchEndPending
        },Formatting.Indented));
        ScreenCapture.CaptureScreenshot(Root+"/settlement-"+round+".png");
        yield return new WaitForSecondsRealtime(.3f);
        button.onClick.Invoke();
        WinningPanels++;
        Save();
    }
    static IEnumerator Automated() {
        Start(Application.isEditor?"editor-live":"player-live");
        yield return WaitFor(()=>NetworkManager.Instance.IsWebSocketOpen,"local websocket connected");
        Login(0);
        yield return WaitFor(()=>Logins>0 && UserDataManager.Instance.UserId>0,"local test account logged in");
        yield return WaitFor(()=>WindowsManager.Instance.GetCurrentWindow()=="menu","login UI completed navigation");
        yield return new WaitForSecondsRealtime(.5f);
        OpenCreate();yield return new WaitForSecondsRealtime(1);
        yield return WaitFor(()=>UnityEngine.Object.FindObjectsByType<CreatePanel>(FindObjectsInactive.Exclude,FindObjectsSortMode.None).Any(),"create panel visible");
        PrepareCreate();yield return new WaitForSecondsRealtime(.5f);
        var drop=Field<TMP_Dropdown>("SubRuleDropdown");
        Check(drop.captionText.font==drop.itemText.font,"dropdown caption and item fonts match");
        Check(drop.captionText.font.HasCharacters("十六张"),"subrule static font has every glyph");
        ScreenCapture.CaptureScreenshot(Root+"/create-panel.png");
        Create();yield return WaitFor(()=>LastRoom!=null,"sixteen room created");
        Check((string)LastRoom["sub_rule"]=="guobiao/sixteen","room preserved subrule");
        Bots();yield return WaitFor(()=>Starts>0,"four player game started");
        yield return new WaitForSecondsRealtime(1.5f);
        var players=(JArray)LastGame["players_info"];
        var self=players.First(p=>(int)p["user_id"]==UserDataManager.Instance.UserId);
        int seat=(int)self["player_index"],expected=seat==0?17:16;
        Check(self["hand_tiles"].Count()==expected,"initial hand has sixteen or seventeen tiles");
        Check((string)LastGame["sub_rule"]=="guobiao/sixteen","live game preserved subrule");
        Check((string)LastGame["four_jokers_hints"]?["sub_rule"]=="guobiao/sixteen","sixteen authoritative wait snapshot");
        Check(players.Where(p=>(int)p["user_id"]!=UserDataManager.Instance.UserId).All(p=>p["hand_tiles"]==null||!p["hand_tiles"].Any()),"other concealed hands stay private");
        Check(GameCanvas.Instance.HandCardsContainer.GetComponentsInChildren<TileCard>(false).Length==expected,"all initial hand cards rendered");
        ScreenCapture.CaptureScreenshot(Root+"/initial-hand.png");
        SetAuto(true);
        yield return WaitFor(()=>NormalGameStateManager.Instance.LastAskActionTick>12,"real tile actions advanced",90);
        yield return ReconnectRoutine(0);
        yield return WaitFor(()=>Ends>0,"completed whole four-round match",400);
        yield return WaitFor(()=>WinningPanels==ExpectedWinningPanels,"all winning panels reviewed and confirmed");
        yield return WaitFor(()=>EndGamePanel.Instance.gameObject.activeInHierarchy,"final match ranking displayed");
        Check(Settlements==4,"exactly four settlements");
        Check(WinningPanels>0,"winning settlement exercised in live match");
        yield return new WaitForSecondsRealtime(1);
        ScreenCapture.CaptureScreenshot(Root+"/game-end.png");
        Leave();yield return WaitFor(()=>UserDataManager.Instance.RoomId==UserDataManager.ROOM_ID_NONE,"left room and cleared state");
        if(!string.IsNullOrEmpty(Replay)) {
            yield return new WaitForSecondsRealtime(1);
            var replay=GameRecordManager.Instance;
            replay.LoadRecord(File.ReadAllText(Replay));
            yield return new WaitForSecondsRealtime(1.5f);
            Check((string)replay.gameRecord.gameTitle["sub_rule"]=="guobiao/sixteen","replay restored sixteen subrule");
            for(int seatIndex=0;seatIndex<4;seatIndex++) {
                replay.SwitchRecordPerspectiveToOriginalPlayerIndex(seatIndex);
                yield return new WaitForSecondsRealtime(.3f);
                Check(replay.selectedPlayerIndex==seatIndex,"replay viewpoint "+seatIndex);
            }
            replay.SwitchRecordPerspectiveToOriginalPlayerIndex(0);
            ScreenCapture.CaptureScreenshot(Root+"/replay-seventeen-tiles.png");
            yield return new WaitForSecondsRealtime(.3f);
            foreach(var round in replay.gameRecord.gameRound.rounds.Values.OrderBy(r=>r.roundIndex)) {
                replay.GotoSelectRound(round.roundIndex);
                int target=Math.Max(0,round.actionTicks.Count/2);
                replay.GotoSelectNode(target);
                yield return new WaitForSecondsRealtime(.2f);
                Check(replay.currentRoundIndex==round.roundIndex && replay.currentNode==target,"replay seek round "+round.roundIndex);
            }
            ScreenCapture.CaptureScreenshot(Root+"/replay-mid-game.png");
        }
        Save();File.WriteAllText(Root+"/runtime-complete.json",JsonConvert.SerializeObject(new{passed=Failures.Count==0,checks=Checks.Count,starts=Starts,settlements=Settlements,ends=Ends,winningPanels=WinningPanels,expectedWinningPanels=ExpectedWinningPanels,failures=Failures},Formatting.Indented));
    }

}

#endif
