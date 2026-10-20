#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using TMPro;
using UnityEngine;

public static class QingqueUnlimitedValidation {
    private static readonly BindingFlags Fields = BindingFlags.Instance | BindingFlags.NonPublic;
    private static T Field<T>(object owner, string name) => (T)owner.GetType().GetField(name, Fields).GetValue(owner);
    private static void Invoke(object owner, string name, params object[] args) => owner.GetType().GetMethod(name, Fields).Invoke(owner, args);
    private static readonly List<string> Checks = new List<string>();
    private static void Check(bool ok, string message) {
        if (!ok) throw new InvalidOperationException(message);
        Checks.Add(message);
    }

    public static IEnumerator Run(string outputDirectory) {
        if (!Application.isPlaying) throw new InvalidOperationException("Play Mode required");
        Checks.Clear();
        WindowsManager.Instance.SwitchWindow("room");
        yield return new WaitForSecondsRealtime(.5f);
        RoomWindowsManager.Instance.SwitchRoomWindow("createRoom");
        var create = Resources.FindObjectsOfTypeAll<CreatePanel>().Single(c => c.name == "CreateRoomPanel" && c.gameObject.scene.IsValid());
        // Activate the real saved hierarchy; no new UI is created by the validation.
        for (Transform parent = create.transform; parent; parent = parent.parent) parent.gameObject.SetActive(true);
        yield return null;
        var chooseRule = Field<TMP_Dropdown>(create, "chooseRule");
        int qingque = CreateRoomRuleTextConfigCatalog.Rules.ToList().FindIndex(r => r.Rule == "qingque");
        chooseRule.value = qingque;
        yield return null;
        var unlimited = Field<UnityEngine.UI.Toggle>(create, "unlimitedRoundsToggle");
        var full = Field<UnityEngine.UI.Toggle>(create, "gameTime4Button");
        Check(unlimited && unlimited.gameObject.activeInHierarchy, "Qingque offers the baked unlimited option");
        Check(unlimited.group == full.group, "Unlimited and fixed lengths are mutually exclusive");
        Check(unlimited.transform.GetSiblingIndex() == full.transform.GetSiblingIndex() + 1, "Unlimited is directly right of the full-game option");
        unlimited.isOn = true;
        Canvas.ForceUpdateCanvases();
        Check(!full.isOn && create.CaptureRoomCreationInput().UnlimitedRounds, "Selecting unlimited captures the setting and deselects sixteen hands");
        // An unauthenticated editor run can have a blank default name.
        var input = create.CaptureRoomCreationInput(); input.RoomName = "无固定时限验证";
        var request = QingqueRoomBuilder.Build(input);
        Check(request.IsValid && ((Qingque_Create_RoomConfig)request.Configuration).UnlimitedRounds, "Room builder sends the selected setting");
        var cornersA = new Vector3[4]; var cornersB = new Vector3[4];
        ((RectTransform)full.transform).GetWorldCorners(cornersA);
        ((RectTransform)unlimited.transform).GetWorldCorners(cornersB);
        Check(cornersB.Min(c => c.x) >= cornersA.Max(c => c.x) - 1, "Fifth choice does not overlap the sixteen-hand choice");
        Check(unlimited.GetComponentInChildren<TMP_Text>().preferredWidth <= ((RectTransform)unlimited.transform).rect.width, "Unlimited label fits its segment");
        ScreenCapture.CaptureScreenshot(Path.Combine(outputDirectory, "create-room.png"));
        yield return new WaitForSecondsRealtime(.3f);
        chooseRule.value = 0;
        yield return null;
        Check(!unlimited.gameObject.activeSelf && !create.CaptureRoomCreationInput().UnlimitedRounds, "Other rules cannot inherit unlimited mode");
        chooseRule.value = qingque;
        yield return null;
        Check(!unlimited.isOn && full.isOn, "Switching back restores the standard sixteen-hand default");

        GameHost.Current.SwitchWindow("game");
        yield return new WaitForSecondsRealtime(.5f);
        GameSceneUIManager.Instance.ClearTemporaryPanels();
        yield return null;
        var panel = UnlimitedRoundPanel.Instance;
        var menu = ExitButtonManager.Instance;
        menu.ShowForRoomGame();
        int self = UserDataManager.Instance.UserId;
        var a = new UnlimitedRoundPlayer { user_id = self, username = "小明" };
        var b = new UnlimitedRoundPlayer { user_id = self + 12345, username = "另一玩家" };
        var state = new UnlimitedRoundInfo { enabled = true, withdrawn_players = new List<UnlimitedRoundPlayer>() };
        panel.Initialize(new GameInfo { room_rule = "qingque", room_type = "custom", unlimited_rounds = true, unlimited_info = state }, true);
        var button = Field<UnityEngine.UI.Button>(menu, "unlimitedEndButton");
        var label = Field<TMP_Text>(menu, "unlimitedEndLabel");
        var notice = Field<GameObject>(panel, "noticeRoot");
        var message = Field<TMP_Text>(panel, "messageText");
        var background = Field<UnityEngine.UI.Image>(panel, "background");
        Check(button.gameObject.activeInHierarchy && label.text == "结束游戏", "Unlimited game exposes end-game button");
        Check(!Field<UnityEngine.UI.Button>(menu, "voteEndButton").gameObject.activeSelf, "Unlimited game uses end-after-hand control");
        int children = panel.transform.childCount;
        state.withdrawn_players.Add(a);
        panel.ApplyState(state);
        Check(notice.activeInHierarchy && background.color.r > background.color.b, "Withdrawal displays a red banner");
        Check(label.text == "回归游戏" && message.text == "小明玩家已放弃游戏，游戏将在本局游戏后结束", "Withdrawal shows player name and enables return");
        Check(!message.richText && !background.raycastTarget, "Player names are plain text and notice does not block the table");
        ScreenCapture.CaptureScreenshot(Path.Combine(outputDirectory, "withdrawn.png"));
        yield return new WaitForSecondsRealtime(.3f);
        state.withdrawn_players.Clear();
        state.returned_player = a; state.countdown = 5;
        panel.ApplyState(state);
        Check(notice.activeSelf && background.color.b > background.color.r && label.text == "结束游戏", "Returning immediately cancels local withdrawal and turns the banner blue");
        Check(message.text == "小明玩家已回归游戏", "Return banner identifies the returning player");
        ScreenCapture.CaptureScreenshot(Path.Combine(outputDirectory, "returned.png"));
        yield return new WaitForSecondsRealtime(5.1f);
        Check(!notice.activeSelf, "Return banner disappears automatically after five seconds");
        state.withdrawn_players.Add(b);
        state.countdown = .1f;
        panel.ApplyState(state);
        yield return new WaitForSecondsRealtime(.2f);
        Check(notice.activeSelf && background.color.r > background.color.b && message.text.Contains("另一玩家"), "After return notice expires, another player's pending request remains red");
        Check(label.text == "结束游戏", "Other players' requests do not change this player's button to return");
        for (int i = 0; i < 5; i++) panel.ApplyState(state);
        Check(children == panel.transform.childCount && notice.transform.childCount == 2, "Repeated state updates reuse all baked objects");
        state.returned_player = null;
        state.countdown = 0;
        state.revision = 2;
        panel.ApplyState(state);
        panel.ApplyState(new UnlimitedRoundInfo { enabled = true, revision = 1, withdrawn_players = new List<UnlimitedRoundPlayer>() });
        Check(notice.activeSelf && message.text.Contains("另一玩家"), "A delayed older broadcast cannot erase a newer withdrawal");
        menu.ShowForRealtimeSpectator();
        panel.Initialize(new GameInfo { room_rule = "qingque", room_type = "custom", unlimited_rounds = true, unlimited_info = state }, false);
        Check(!button.gameObject.activeSelf, "Spectators cannot end or rejoin the game");
        panel.ResetState();
        Check(!notice.activeSelf && !button.gameObject.activeSelf, "Session teardown clears the banner and control");
        Directory.CreateDirectory(outputDirectory);
        File.WriteAllText(Path.Combine(outputDirectory, "client-validation.json"), JsonConvert.SerializeObject(new { passed = Checks.Count, Checks, Screen.width, Screen.height }, Formatting.Indented));
        Debug.Log("Qingque unlimited validation passed: " + Checks.Count);
    }
}
#endif
