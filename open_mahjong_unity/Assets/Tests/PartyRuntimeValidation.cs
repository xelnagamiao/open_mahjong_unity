#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.IO;
using System.Collections;
using System.Linq;
using Newtonsoft.Json;
using TMPro;
using UnityEngine;

/// <summary>Opt-in checks for the real MainScene party UI, against loopback services only.</summary>
public sealed class PartyRuntimeValidation : MonoBehaviour {
    static string output;
    static int qaUser;
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Configure() {
        output = null; qaUser = 0;
        var args = Environment.GetCommandLineArgs(); int i = Array.IndexOf(args, "--verify-party");
        if (i < 0 || i + 1 >= args.Length) return;
        output = Path.GetFullPath(args[i + 1]); Directory.CreateDirectory(output);
        ConfigManager.gameUrl = "ws://localhost:8081/game";
        ConfigManager.chatUrl = "ws://localhost:8083/chat";
        ConfigManager.webApiUrl = "http://localhost:3000";
        int endpointIndex = Array.IndexOf(args, "--party-qa-endpoint");
        if (endpointIndex >= 0 && endpointIndex + 2 < args.Length) {
            var endpoint = new Uri(args[endpointIndex + 1]);
            if (endpoint.Scheme != "ws" || !endpoint.IsLoopback) throw new InvalidOperationException("Party QA must use loopback");
            qaUser = int.Parse(args[endpointIndex + 2]);
            ConfigManager.gameUrl = endpoint.ToString().TrimEnd('/') + "/" + qaUser;
            ConfigManager.chatUrl = "ws://127.0.0.1:1/chat";
        }
        Application.runInBackground = true;
    }
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap() { if (output != null) new GameObject("PartyRuntimeValidation").AddComponent<PartyRuntimeValidation>(); }
    IEnumerator Start() {
        float deadline = Time.realtimeSinceStartup + 30;
        while ((!NetworkManager.Instance || !NetworkManager.Instance.IsWebSocketOpen) && Time.realtimeSinceStartup < deadline) yield return null;
        if (qaUser == 0) yield break;
        NetworkManager.Instance.Login("组队测试" + qaUser, "qa-only");
        while (UserDataManager.Instance.UserId == 0 && Time.realtimeSinceStartup < deadline) yield return null;
        if (UserDataManager.Instance.UserId == 0) yield break;
        WindowsManager.Instance.SwitchWindow("match");
        yield return new WaitForSecondsRealtime(1);
        var views = UnityEngine.Object.FindObjectsByType<MatchLobbyView>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        var dock = UnityEngine.Object.FindFirstObjectByType<PartyLobbyView>(FindObjectsInactive.Include);
        foreach (var view in views) view.SelectRule(3);
        yield return null;
        if (dock.bar.activeInHierarchy) throw new InvalidOperationException("Party panel leaked into another matching rule");
        Check("other-rule-hidden");
        yield return new WaitForSecondsRealtime(.15f);
        WindowsManager.Instance.SwitchWindow("menu");
        yield return null;
        if (dock.bar.activeInHierarchy) throw new InvalidOperationException("Party panel leaked outside matching");
        Check("outside-match-hidden");
        yield return new WaitForSecondsRealtime(.15f);
        WindowsManager.Instance.SwitchWindow("match");
        foreach (var view in views) view.SelectRule(7);
        yield return new WaitForSecondsRealtime(1);
        if (!dock.bar.activeInHierarchy || !dock.leaderCrowns[0].activeInHierarchy)
            throw new InvalidOperationException("Solo captain panel or crown is missing");
        Check("initial-lobby");
    }
    public static string Check(string phase) {
        var manager = PartyNetworkManager.Instance;
        if (!manager || !manager.friendPicker || !manager.incomingPanel) throw new InvalidOperationException("Missing party UI binding");
        var dock = UnityEngine.Object.FindFirstObjectByType<PartyLobbyView>(FindObjectsInactive.Include);
        if (!dock || dock.memberButtons.Length < 5 || !dock.invite || !dock.heading) throw new InvalidOperationException("Missing extensible party dock");
        if (dock.bar.transform.Find("Action") || dock.bar.transform.Find("Detail") || dock.bar.transform.Find("Accent"))
            throw new InvalidOperationException("Retired party actions or layered decoration remain");
        if (!dock.matchLobby || !dock.transform.IsChildOf(dock.matchLobby.transform)
            || dock.leaderCrowns == null || dock.leaderCrowns.Length != dock.memberButtons.Length)
            throw new InvalidOperationException("Party panel must belong to matching and bind its captain crowns");
        foreach (var root in new[] { manager.friendPicker.gameObject, manager.incomingPanel.gameObject, dock.gameObject })
            if (root.GetComponentsInChildren<TMP_Text>(true).Any(t => (t.fontStyle & FontStyles.Bold) != 0 || t.fontWeight != FontWeight.Regular))
                throw new InvalidOperationException("Chinese UI must use normal font weight");
        string json = JsonConvert.SerializeObject(new { phase, party = manager.Party, dockVisible = dock.bar.activeInHierarchy,
            picker = manager.friendPicker.gameObject.activeInHierarchy, incoming = manager.incomingPanel.gameObject.activeInHierarchy,
            rule = RankedRules.QueueRule(PartyNetworkManager.Queue), members = manager.MemberCount,
            captainCrowns = dock.leaderCrowns.Count(c => c && c.activeInHierarchy), activeRule = dock.matchLobby.ActiveRule });
        if (output != null) {
            File.WriteAllText(Path.Combine(output, Path.GetFileName(phase) + ".json"), json);
            ScreenCapture.CaptureScreenshot(Path.Combine(output, Path.GetFileName(phase) + ".png"));
        }
        return json;
    }
}
#endif
