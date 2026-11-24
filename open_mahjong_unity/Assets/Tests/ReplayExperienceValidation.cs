#if UNITY_EDITOR
using System;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public static class ReplayExperienceValidation {
    public static string Run(string detailJson) {
        if (!Application.isPlaying) throw new InvalidOperationException("Real MainScene Play Mode required");
        int checks = 0;
        void Check(bool ok, string message) { if (!ok) throw new Exception(message); checks++; }
        var detail = JsonConvert.DeserializeObject<RecordDetail>(detailJson);
        detail.selected_table_id = "open";
        GameRecordManager manager = null;
        void Open() {
            Check(RecordPanel.OpenRecord(detail, true), "open archive");
            manager = GameRecordManager.Instance;
            manager.GetComponentsInChildren<TMP_Dropdown>(true).Single(t => t.name == "ComparePhase").value = 0;
        }
        void Call(string method, params object[] args) => typeof(GameRecordManager).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(manager, args);
        Button Toolbar(string field) => (Button)typeof(GameRecordManager).GetField(field, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(manager);
        Open();
        for (int seat = 0; seat < 4; seat++) {
            if (seat > 0) Open();
            manager.SwitchRecordPerspectiveToOriginalPlayerIndex(seat);
            var actions = (JArray)manager.MatchSession.Table("open")["round_meta"]["round_index_1"]["action_meta"];
            int source = -1, target = -1;
            for (int i = 0; i < actions.Count; i++) {
                if ((int?)actions[i]["actor_original_seat"] != seat || !(actions[i]["wall_ref"] is JObject origin)) continue;
                var found = MatchRecordSession.Locate(manager.MatchSession.Table("closed"), origin);
                if (!found.Exact) continue;
                source = i + 1; target = found.Node; break;
            }
            Check(source > 0, "real draw for selected seat " + seat);
            manager.JumpToSharePosition(1, source);
            Call("ShowRoundInfo");
            var popup = UnityEngine.Object.FindAnyObjectByType<RecordInfoPopup>(FindObjectsInactive.Include);
            Check(popup.gameObject.activeSelf, "status opened");
            string before = manager.ReplayStatusText;
            manager.SwitchMatchTable("closed");
            Check(manager.currentNode == target && manager.selectedPlayerOriginalIndex == seat, "selected perspective retained across aligned switch " + seat);
            Check(popup.gameObject.activeSelf && manager.ReplayStatusText != before && manager.ReplayStatusText.Contains("闭室"), "open status follows switch");
            manager.JumpToSharePosition(1, target + 1);
            Check(manager.ReplayStatusText.Contains((target + 1) + "/"), "status follows seek");
            var toggle = manager.GetComponentsInChildren<Toggle>(true).Single(t => t.name == "CompareWall");
            toggle.isOn = false;
            Check(manager.currentNode == 0, "independent progress was not overwritten by comparison");
        }
        Open();
        var gameButton = Toolbar("showGameInfoButton");
        var statusButton = Toolbar("showRoundInfoButton");
        gameButton.onClick.Invoke();
        var info = UnityEngine.Object.FindAnyObjectByType<RecordInfoPopup>(FindObjectsInactive.Include);
        Canvas.ForceUpdateCanvases();
        TMP_Text[] Labels() => info.GetComponentsInChildren<TMP_Text>(false);
        var scroll = info.GetComponent<ScrollRect>();
        var text = scroll.content.GetComponent<TMP_Text>();
        string body = text.text.Replace("\r", "");
        Check(gameButton.GetComponentsInChildren<TMP_Text>(false).Any(t => t.text == "对局信息")
            && body.StartsWith("【对局信息】\n规则: "), "original match info format and renamed toolbar");
        Check(Labels().Length == 1 && !info.GetComponentsInChildren<Button>(false).Any(), "one original text, no added headings, cards or buttons");
        int cursor = 0;
        for (int t = 0; t < 2; t++) {
            string room = t == 0 ? "开室" : "闭室";
            int heading = body.IndexOf(room + "\n", cursor, StringComparison.Ordinal);
            Check(heading >= cursor && heading > 0 && body[heading - 1] == '\n', "room heading " + room); cursor = heading + room.Length + 1;
            var title = manager.MatchSession.Table(t == 0 ? "open" : "closed")["payload"]["game_title"];
            for (int seat = 0; seat < 4; seat++) {
                string line = body.Substring(cursor).Split('\n')[0];
                Check(line.StartsWith("东南西北"[seat].ToString()) && line.Contains(title[$"p{seat}_name"].ToString())
                    && line.Contains(title[$"p{seat}_uid"].ToString()), $"{room} seat {seat} includes original ID and UID");
                cursor += line.Length + 1;
            }
        }
        Check(!new[]{"全场结果", "个人 IMP", "规则设置", "原对局：", "牌山与校验信息", "比赛 ID"}.Any(body.Contains), "no additional match summary or section headings");
        var activeTitle = manager.MatchSession.Table(manager.MatchSession.ActiveTable)["payload"]["game_title"];
        Check(body.Contains((string)activeTitle["start_time"]) && body.Contains((string)activeTitle["end_time"]), "original record dates remain visible");
        Check(scroll.vertical, "original info scroll available");
        scroll.verticalNormalizedPosition = 0;
        Canvas.ForceUpdateCanvases();
        if (scroll.content.rect.height > scroll.viewport.rect.height) {
            var bottom = scroll.viewport.InverseTransformPoint(scroll.content.TransformPoint(new Vector2(0,scroll.content.rect.yMin)));
            Check(Mathf.Abs(bottom.y-scroll.viewport.rect.yMin)<2, "can scroll to last info line");
        }
        scroll.verticalNormalizedPosition = 1;
        Check(text.fontWeight == FontWeight.Regular && (text.fontStyle & FontStyles.Bold) == 0, "regular Chinese typography");
        gameButton.onClick.Invoke();
        Check(!info.gameObject.activeSelf, "same match info toolbar control dismisses the popup");
        gameButton.onClick.Invoke(); statusButton.onClick.Invoke();
        Check(info.gameObject.activeSelf && info.GetComponentsInChildren<TMP_Text>(false).Any(t => t.text == "阅览状态"), "info buttons switch directly between documents");
        Check(!info.GetComponentsInChildren<Button>(false).Any(), "no extra close button in replay status");
        statusButton.onClick.Invoke();
        Check(!info.gameObject.activeSelf, "same status toolbar control dismisses the popup");
        statusButton.onClick.Invoke();
        manager.JumpToSharePosition(2, 0);
        var scorePanel = GameCanvas.Instance.GetComponentsInChildren<SichuanRoundScorePanel>(true).Single();
        var state = (SichuanRoundScoreState)typeof(SichuanRoundScorePanel).GetField("state", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(scorePanel);
        var firstBoard = detail.record["settlements"].Single(board => (int)board["number"] == 1)["imp_scores"];
        Check(state.Unit == "IMP" && state.Blue == (long)firstBoard[0] && state.Red == (long)firstBoard[1],
            "HUD at board two contains board one IMP only");
        void CheckReadability(SichuanRoundScorePanel panel, string blue, string red) {
            void TextSize(string path, string expected, float minimum) {
                var label = panel.transform.Find(path).GetComponent<TMP_Text>();
                label.ForceMeshUpdate();
                var visible = label.textInfo.characterInfo.Take(label.textInfo.characterCount).Where(c => c.isVisible).ToArray();
                float renderedSize = visible.Length == 0 ? 0 : visible.Min(c => c.pointSize);
                Check(label.gameObject.activeInHierarchy && label.text.Contains(expected) && renderedSize >= minimum,
                    $"{path} rendered at {renderedSize}, expected >= {minimum} for {expected}");
            }
            TextSize("BlueTeam/Score", blue, 27); TextSize("RedTeam/Score", red, 27);
            TextSize("BlueTeam/Unit", "IMP", 10); TextSize("RedTeam/Unit", "IMP", 10);
            Check(panel.GetComponentsInChildren<TMP_Text>(false).All(t => t.name == "TeamName" || t.name == "Score" || t.name == "Unit"),
                "team scores have no supplementary caption");
        }
        CheckReadability(scorePanel, firstBoard[0].ToString(), firstBoard[1].ToString());
        var live = UnityEngine.Object.Instantiate(scorePanel, scorePanel.transform.parent);
        try {
            live.Initialize(new GameInfo { duplicate_match = new DuplicateMatchInfo { id = "font-check", sync_mode = true } }, "sichuan/doubles_duplicate");
            live.UpdateDuplicate(new DuplicateMatchInfo { id = "font-check", scoring = "imp", imp_scores = new[] {12, 9}, sync_mode = true });
            Canvas.ForceUpdateCanvases();
            CheckReadability(live, "12", "9");
        } finally { live.gameObject.SetActive(false); UnityEngine.Object.Destroy(live.gameObject); }
        manager.JumpToSharePosition(1, 0);
        Check(state.Blue == 0 && state.Red == 0, "HUD rewinds with replay");
        manager.ToggleRecordAutoPlay(); Call("RefreshReplayPresentation", true);
        Check(manager.ReplayStatusText.Contains("播放中") && info.gameObject.activeSelf, "playback status follows play");
        manager.ToggleRecordAutoPlay(); Call("RefreshReplayPresentation", true);
        Check(manager.ReplayStatusText.Contains("已暂停"), "playback status follows pause");
        Call("ShowGameInfo");
        return checks + " replay experience checks passed";
    }
}
#endif
