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

public sealed class GuangdongDoublesClientValidation : MonoBehaviour {
#if UNITY_EDITOR
    public static void Build() {
        var args = Environment.GetCommandLineArgs();
        int at = Array.IndexOf(args, "--gd-output");
        if (at < 0 || at + 1 >= args.Length) throw new ArgumentException("Missing player path");
        string path = Path.GetFullPath(args[at + 1]);
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        if (Array.IndexOf(args, "--bake-support-table") >= 0) {
            UnityEditor.SceneManagement.EditorSceneManager.OpenScene("Assets/Scenes/MainScene.unity");
            if (!UnityEditor.EditorApplication.ExecuteMenuItem("Tools/Mahjong/Main Scene/Sync Mahjong Rule Support Table"))
                throw new InvalidOperationException("Failed to bake the rule support table");
        }
        var result = UnityEditor.BuildPipeline.BuildPlayer(new UnityEditor.BuildPlayerOptions {
            scenes = new[] { "Assets/Scenes/MainScene.unity" }, locationPathName = path,
            target = UnityEditor.BuildTarget.StandaloneWindows64, options = UnityEditor.BuildOptions.Development
        });
        var report = result.summary;
        if (report.result == UnityEditor.Build.Reporting.BuildResult.Succeeded && Array.IndexOf(args, "--gd-adapter-tests") >= 0)
            File.WriteAllText(Path.Combine(Path.GetDirectoryName(path), "adapter-tests.json"), GuangdongClientValidation.Run());
        File.WriteAllText(Path.Combine(Path.GetDirectoryName(path), "build-result.json"),
            JsonConvert.SerializeObject(new { result = report.result.ToString(), report.totalErrors, report.totalWarnings, report.totalSize }));
        UnityEditor.EditorApplication.Exit(report.result == UnityEditor.Build.Reporting.BuildResult.Succeeded ? 0 : 1);
    }
#endif
    private static string output, endpoint;
    private static int user;
    private NativeWebSocket.WebSocket socket;
    private readonly List<string> failures = new();
    private readonly List<string> checks = new();
    private int clicks, claims, midWins, finished, reconnects;
    private float deadline;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Configure() {
        string[] args = Environment.GetCommandLineArgs();
        int at = Array.IndexOf(args, "--verify-guangdong-doubles");
        if (at < 0 || at + 3 >= args.Length) return;
        output = Path.GetFullPath(args[at + 1]);
        endpoint = args[at + 2];
        user = int.Parse(args[at + 3]);
        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri) || !uri.IsLoopback) throw new ArgumentException("Loopback required");
        ConfigManager.gameUrl = endpoint + "/" + user;
        ConfigManager.BuildForSteam = false;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Launch() {
        if (output == null) return;
        var chat = Resources.FindObjectsOfTypeAll<ChatManager>().FirstOrDefault(c => c.gameObject.scene.IsValid());
        if (chat != null) chat.enabled = false;
        new GameObject(nameof(GuangdongDoublesClientValidation)).AddComponent<GuangdongDoublesClientValidation>();
    }

    private void Save(string status = "running") {
        Directory.CreateDirectory(output);
        File.WriteAllText(Path.Combine(output, $"player-{user}.json"),
            JsonConvert.SerializeObject(new { status, clicks, claims, midWins, finished, reconnects, checks, failures }, Formatting.Indented));
    }

    private void Check(bool valid, string label) {
        if (valid) checks.Add(label); else failures.Add(label);
        Save();
    }

    private IEnumerator Start() {
        Application.runInBackground = true;
        Application.targetFrameRate = 24;
        QualitySettings.vSyncCount = 0;
        deadline = Time.realtimeSinceStartup + 840;
        Application.logMessageReceived += (msg, trace, kind) => {
            if (kind == LogType.Error || kind == LogType.Exception || kind == LogType.Assert) { failures.Add(msg + "\n" + trace); Save(); }
        };
        Check(PlayerInfoRuleCatalog.HasFanStats(GuangdongMilRules.Doubles) &&
            PlayerInfoRuleCatalog.FanNames(GuangdongMilRules.Doubles).ContainsKey("鸡和"), "番种统计目录有鸡和");
        var tiles = new List<int> { 11,12,13,14,15,16,22,23,24,36,37,38,41,41 };
        var context = new JObject { ["doubles"]=true, ["chicken_available"]=true, ["discarded_ghosts"]=2 };
        var row = RegionalLocalCalculator.Describe("guangdong",tiles,new List<string>(),41,context);
        Check((bool)row["ron"] && (int)row["ron_score"]==8 && (int)row["self_draw_score"]==16, "C# 队伍系数与鸡和点数");
        context["chicken_available"]=false;
        row = RegionalLocalCalculator.Describe("guangdong",tiles,new List<string>(),41,context);
        Check(!(bool)row["ron"] && !(bool)row["self_draw"], "C# 第二次鸡和受限");
        var history = new WaitHintQuery { HandWithWin=tiles, Melds=new List<string>(), HepaiTile=41,
            Record=new RecordTipsContext { SubRule=GuangdongMilRules.Doubles, GuangdongTeamGhosts=2, GuangdongChickenAvailable=false } };
        Check(GuangdongBaseHints.Describe(history).Label == "未满足", "牌谱按座位检查队友的鸡和资格");
        while (NetworkManager.Instance == null || !NetworkManager.Instance.IsWebSocketOpen) yield return null;
        UserDataManager.Instance.SetUserInfo("广东双打测试" + user, "qa-only", user, false);
        Resources.FindObjectsOfTypeAll<UserContainer>().First(c=>c.gameObject.scene.IsValid()).ShowUserSettings(new UserSettings());
        WindowsManager.Instance.SwitchWindow("menu");
        Bind();
        _ = socket.SendText("{\"type\":\"qa/ready\"}");
    }

    private void Bind() {
        if (socket != null) socket.OnMessage -= Receive;
        socket = NetworkManager.Instance.GetWebSocket();
        socket.OnMessage += Receive;
    }

    private void Update() {
        if (deadline <= 0 || Time.realtimeSinceStartup < deadline) return;
        deadline = 0; failures.Add("客户端超时"); Save("failed"); Application.Quit(2);
    }

    private void Receive(byte[] data) {
        var message = JObject.Parse(System.Text.Encoding.UTF8.GetString(data));
        var type = (string)(message["qa"] ?? message["type"]);
        if (type == "qa/action") StartCoroutine(Act(message));
        if (type == "qa/inspect") StartCoroutine(Inspect(message));
        if (type == "qa/reconnect") StartCoroutine(Reconnect());
        if (type == "qa/replay") StartCoroutine(Replay(message));
        if (type == "qa/done") { Save(failures.Count == 0 ? "completed" : "failed"); Application.Quit(failures.Count == 0 ? 0 : 1); }
        if (type != null && type.EndsWith("/show_result")) {
            var info = message["show_result_info"];
            if ((string)info["blood_battle_step"] == "mid_win") {
                midWins++;
                Check(info["revealed_hands"]==null && info["hu_fan"]==null && info["hu_score"]==null, "副中不公开底牌、分数和番种");
            } else if ((string)info["next_status"] != "round_continue") finished++;
        }
    }

    private IEnumerator Act(JObject msg) {
        yield return new WaitForSecondsRealtime(.25f);
        var manager = NormalGameStateManager.Instance;
        var action = (string)msg["action"];
        int tick = (int)msg["tick"];
        float askDeadline = Time.realtimeSinceStartup + 10;
        while (manager.LastAskActionTick != tick && Time.realtimeSinceStartup < askDeadline) yield return null;
        Check(manager.LastAskActionTick == tick, "操作窗口 " + tick);
        if (manager.LastAskActionTick != tick) yield break;
        if (action != "cut") {
            Check(manager.allowActionList.Contains(action), "权威按钮 " + action);
            GameStateNetworkManager.Instance.SendAction(action, manager.currentAskCutTileId); claims++;
        } else {
            int tile=(int)msg["tile"];
            bool drawn=(bool)msg["drawn"];
            TileCard card=null;
            float until=Time.realtimeSinceStartup+8;
            while(card==null && Time.realtimeSinceStartup<until) {
                card=FindObjectsByType<TileCard>(FindObjectsInactive.Exclude,FindObjectsSortMode.None)
                    .FirstOrDefault(c=>c.tileId==tile && c.currentGetTile==drawn && c.IsSelectableForCut());
                if(card==null)yield return null;
            }
            if (card == null) {
                var shown = FindObjectsByType<TileCard>(FindObjectsInactive.Exclude, FindObjectsSortMode.None)
                    .Where(c => c.IsSelectableForCut()).Select(c => c.tileId + (c.currentGetTile ? "*" : ""));
                failures.Add("可选实体牌 " + string.Join(",", shown));
            }
            Check(card!=null,"可点击的实体手牌 " + tile);
            if(card!=null){typeof(TileCard).GetMethod("OnTileClick", BindingFlags.Instance|BindingFlags.NonPublic).Invoke(card,null);clicks++;}
        }
        Save();
    }

    private IEnumerator Reconnect() {
        NetworkManager.Instance.BeginNewConnection();
        yield return null;
        while(!NetworkManager.Instance.IsWebSocketOpen)yield return null;
        Bind();reconnects++;_ = socket.SendText("{\"type\":\"qa/reconnected\"}");
    }

    private void CheckTeamScores(int[] scores, string stage) {
        var panel = FindFirstObjectByType<SichuanRoundScorePanel>();
        string[] teams = { "BlueTeam", "RedTeam" };
        for (int i = 0; i < teams.Length; i++) {
            var cell = panel?.transform.Find(teams[i] + "/Score")?.GetComponent<TMPro.TMP_Text>();
            string expected = scores[i] > 0 ? "+" + scores[i] : scores[i].ToString();
            Check(cell != null && cell.gameObject.activeInHierarchy && cell.text == expected,
                stage + " · " + teams[i] + " " + expected + " / " + cell?.text);
        }
    }

    private IEnumerator Inspect(JObject message) {
        yield return new WaitForSecondsRealtime(.8f);
        var mirror=TableMirror.Current;
        Check(RuleRegistry.ActiveGameState is GuangdongMilGameState && GameSession.Current.SubRule==GuangdongMilRules.Doubles,"广东双打状态机");
        var scores=message["scores"].ToObject<int[]>();
        float until = Time.realtimeSinceStartup + 8;
        while (mirror.IndexToPosition.Any(p => mirror.Info(p.Value).score != scores[p.Key]) && Time.realtimeSinceStartup < until) yield return null;
        foreach(var pair in mirror.IndexToPosition)Check(mirror.Info(pair.Value).score==scores[pair.Key],"座位分数 "+pair.Key);
        string label=(string)message["label"];
        CheckTeamScores(message["team_scores"].ToObject<int[]>(), label);
        if(label=="chicken")Check(mirror.IndexToPosition.Values.Any(pos=>(mirror.Info(pos).tag_list??Array.Empty<string>()).Contains("gd_chicken")), "牌桌公开鸡和");
        if(user==11000001) ClientValidationScreenshot.Capture(output, label, Check);
        _=socket.SendText(new JObject{["type"]="qa/inspected",["label"]=label,["token"]=message["token"]}.ToString(Formatting.None));
    }

    private IEnumerator Replay(JObject message) {
        NormalGameStateManager.Instance.ResetForExit();
        RecordPanel.OpenRecord(message["detail"].ToObject<RecordDetail>(),true);
        yield return new WaitForSecondsRealtime(1.0f);
        var manager=GameRecordManager.Instance;
        var previous = new int[2];
        foreach(var round in message["rounds"]) {
            int i=(int)round["index"];
            manager.GotoSelectRound(i,false);
            yield return null;
            int end=manager.gameRecord.gameRound.rounds[i].actionTicks.Count;
            manager.GotoAction(end);yield return null;
            var scores=round["scores"].ToObject<int[]>();
            foreach(var p in manager.recordPlayer_to_info.Values)
                Check(p.score==scores[p.playerIndex],"牌谱第"+i+"副座位"+p.playerIndex+"结算");
            var teamScores = round["team_scores"].ToObject<int[]>();
            CheckTeamScores(teamScores, "回放第" + i + "副结算");
            if (i == 1 || i == (int)message["rounds"].Last["index"]) {
                manager.GotoAction(0); yield return null;
                CheckTeamScores(previous, "回退第" + i + "副开局");
                manager.GotoAction(end); yield return null;
                CheckTeamScores(teamScores, "再看第" + i + "副结算");
            }
            previous = teamScores;
        }
        ClientValidationScreenshot.Capture(output, "replay-final", Check);
        _=socket.SendText("{\"type\":\"qa/replayed\"}");
    }
}
#endif
