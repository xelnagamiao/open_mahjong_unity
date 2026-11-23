using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public sealed class NewChineseGameState : TurnBasedGameState {
    public const string RuleId = "new_chinese", SubRule = "new_chinese/standard";
    public static NewChineseGameState Active => RuleRegistry.ActiveGameState as NewChineseGameState;
    public NewChineseInfo Info {get; private set;}
    private int exchangeTick = -1;
    private Coroutine baoPresentation;
    private readonly Queue<(string suffix, Response response)> afterBao = new Queue<(string, Response)>();

    public override bool HandleMessage(string suffix, Response response) {
        if (suffix == "game_start") ClearBaoPresentation();
        if (baoPresentation == null) return Dispatch(suffix, response);
        afterBao.Enqueue((suffix, response));
        return true;
    }

    private bool Dispatch(string suffix, Response response) {
        var info = response.game_info?.new_chinese;
        if (info != null && response.received_monotonic is double received) {
            double now = System.Diagnostics.Stopwatch.GetTimestamp() / (double)System.Diagnostics.Stopwatch.Frequency;
            int delay = (int)Math.Max(0, (now - received) * 1000);
            if (info.bao_remaining_ms is int remaining) info.bao_remaining_ms = Math.Max(0, remaining - delay);
            info.exchange_remaining_ms = Math.Max(0, info.exchange_remaining_ms - delay);
        }
        int tick = response.game_info?.action_tick ?? 0;
        if (info != null && suffix != "game_start" && tick > 0) {
            bool exchange = info.phase == "waiting_exchange" || info.phase == "showing_exchange_choices" || info.phase == "receiving_exchange";
            if (exchange && tick <= exchangeTick) return true;
            exchangeTick = Math.Max(tick, exchangeTick);
        }
        return base.HandleMessage(suffix, response);
    }

    private void ShowBaoCaption(bool initial, float remainingSeconds) {
        Clock.Clear("NewChineseBao");
        var panel = EndLiujuPanel.Instance;
        if (panel == null || remainingSeconds <= 0f) return;
        panel.ShowTableCaption(initial ? "翻宝" : "换宝", remainingSeconds);
        if (panel.IsShowingTableCaption)
            baoPresentation = Manager.StartCoroutine(WaitForBaoCaption(panel));
    }

    private IEnumerator WaitForBaoCaption(EndLiujuPanel panel) {
        while (panel != null && panel.IsShowingTableCaption) yield return null;
        baoPresentation = null;
        while (baoPresentation == null && afterBao.Count > 0) {
            var message = afterBao.Dequeue();
            Dispatch(message.suffix, message.response);
        }
    }

    private void ClearBaoPresentation() {
        if (baoPresentation != null) {
            if (Manager != null) Manager.StopCoroutine(baoPresentation);
            if (EndLiujuPanel.Instance?.IsShowingTableCaption == true)
                EndLiujuPanel.Instance.ClearEndLiujuPanel();
        }
        baoPresentation = null;
        afterBao.Clear();
    }

    public override bool IsSelfLocked => (Info?.declared != null && Session.SelfIndex >= 0 &&
        Session.SelfIndex < Info.declared.Length) ? Info.declared[Session.SelfIndex] : SeatHasTag("self", tag => tag == "declared_ready");

    public override bool CanCutTile(int tileId) {
        var actions = Clock.AllowActionList;
        if (actions.Contains("riichi_cut") && !actions.Contains("cut"))
            return RiichiCutSelectionController.Instance?.IsActive == true && Clock.RiichiCandidateCuts.ContainsKey(tileId);
        return base.CanCutTile(tileId);
    }

    protected override void OnActionPlayed(TableAction action) {
        if (!action.Silent && action.IsRiichiHorizontal && action.HasWord("cut"))
            SoundManager.Instance.PlayActionSound(action.Seat, "riichi");
    }

    private void Accept(GameInfo info) {
        if (info?.new_chinese == null) return;
        Info = info.new_chinese;
        RefreshSelfStatusIndicators();
    }

    protected override void OnRoundStarted(GameInfo info) {
        GameCanvas.Instance?.HideXueliuThrowThreeSelection();
        GameCanvas.Instance?.ClearXueliuExchangePresentation();
        Accept(info);
        exchangeTick = info?.action_tick ?? -1;
        if (Info?.phase == "waiting_exchange") {
            if (Info.exchange_outgoing != null && !Session.IsRealtimeSpectator) {
                var hand = Info.exchange_outgoing;
                GameCanvas.Instance?.RestoreSbrWaiting(hand,
                    hand.Length == 3 ? "已选换牌，等待其他玩家…" : "已选不换，等待其他玩家…", anyThreeOptional: true);
            } else exchangeTick--;
        }
        if (Info?.phase == "showing_exchange_choices")
            GameCanvas.Instance?.ShowNewChineseExchangeChoices(Info, Mirror.IndexToPosition);
        if (Info?.phase == "receiving_exchange" && !Session.IsRealtimeSpectator)
            GameCanvas.Instance?.ShowSbrReceived(Info.exchange_incoming, 0, Info.exchange_remaining_ms * .001f);
        if (Info?.phase == "showing_bao")
            ShowBaoCaption(Info.bao_initial, (Info.bao_remaining_ms ?? 1000) * .001f);
        RefreshHints(info);
    }

    private void RefreshHints(GameInfo snapshot) {
        if (!Session.Tips || snapshot?.self_hand_tiles == null || Mirror.Self == null) return;
        var hand = snapshot.self_hand_tiles.ToList();
        if (Info?.self_has_draw_slot == true && hand.Count % 3 == 2) hand.RemoveAt(hand.Count-1);
        TipsBlock.Instance?.ShowTipsBlock(hand, Mirror.Self.combination_tiles ?? new List<string>());
    }

    protected override void OnAskHandAction(Response response) {
        if (Info?.phase == "receiving_exchange") GameCanvas.Instance?.FinishNewChineseReceipt();
        Accept(response.game_info);
        if (Info?.phase == "showing_exchange_choices" || Info?.phase == "receiving_exchange") return;
        GameCanvas.Instance?.FinishNewChineseExchangeChoices();
        var ask = response.ask_hand_action_info;
        if (ask == null) return;
        if (Info?.phase == "waiting_exchange") {
            Clock.LastAskActionTick = ask.action_tick;
            bool canSubmit = (ask.action_list ?? Array.Empty<string>()).Contains("exchange_three");
            if (canSubmit && !Session.IsRealtimeSpectator)
                GameCanvas.Instance?.ShowXueliuThrowThreeSelection(Math.Max(1,ask.remaining_time+(ask.step_remaining ?? Session.RoomStepTime)), true, true);
            return;
        }
        GameCanvas.Instance?.HideNewChineseExchange();
        base.OnAskHandAction(response);
        RefreshHints(response.game_info);
    }

    protected override void OnAskClaim(Response response) {
        Accept(response.game_info);
        Clock.PendingAskFromJiagang = Info?.phase == "waiting_action_qianggang";
        base.OnAskClaim(response);
        RefreshHints(response.game_info);
    }

    protected override void OnDoAction(Response response) {
        Accept(response.game_info);
        var words = response.do_action_info?.action_list;
        if (words != null && words.Contains("new_chinese_exchange_choices")) {
            GameCanvas.Instance?.HideNewChineseExchange();
            GameCanvas.Instance?.ShowNewChineseExchangeChoices(Info, Mirror.IndexToPosition);
            return;
        }
        if (words != null && (words.Contains("new_chinese_exchange") || words.Contains("rob_kan")) &&
            response.game_info?.self_hand_tiles != null) {
            GameCanvas.Instance?.FinishNewChineseExchangeChoices();
            GameCanvas.Instance?.HideNewChineseExchange();
            Manager.InitializeGame(true, "", response.game_info);
            return;
        }
        base.OnDoAction(response);
        if (words != null && (words.Contains("new_chinese_bao_change") || words.Contains("new_chinese_bao_reveal")))
            RefreshHints(response.game_info);
    }

    protected override bool TryApplyFamilyWord(TableAction action, string word) {
        if (word == "new_chinese_bao_reveal" || word == "new_chinese_bao_change") {
            if (!action.Silent)
                ShowBaoCaption(word == "new_chinese_bao_reveal", (Info?.bao_remaining_ms ?? 1000) * .001f);
            return true;
        }
        return word == "declare_kan" || word == "rob_kan";
    }

    protected override void OnShowResult(Response response) {
        Accept(response.game_info);
        base.OnShowResult(response);
    }

    public override void RefreshSelfStatusIndicators() {
        if (GameCanvas.Instance != null)
            GameCanvas.Instance.ShowNewChineseStatus(Info, Mirror.IndexToPosition, Session.SelfIndex);
    }

    public override void OnPlayerTagsRefreshed() {
        base.OnPlayerTagsRefreshed();
        RefreshSelfStatusIndicators();
    }

    public override void OnSessionReset() {
        ClearBaoPresentation();
        GameCanvas.Instance?.ClearXueliuExchangePresentation();
        GameCanvas.Instance?.HideNewChineseExchange();
        GameCanvas.Instance?.ShowNewChineseStatus(null, Mirror.IndexToPosition, -1);
        Info = null;
    }
}

public partial class GameCanvas {
    public void ShowNewChineseStatus(NewChineseInfo info, IReadOnlyDictionary<int,string> positions, int viewer) {
        bool declared = viewer >= 0 && viewer < (info?.declared?.Length ?? 0) && info.declared[viewer];
        SetSelfBaoIndicator(declared && info.bao > 0, declared ? info.bao : 0, false, false);
        int level = info?.levels != null && viewer >= 0 && viewer < info.levels.Length ? info.levels[viewer] : info?.pass_level ?? 0;
        string[] pass = {"", "过和", "过和 · 仅自摸", "过和 · 仅摸宝", "过和 · 等换宝"};
        SetSelfStatusIndicator(StatusSlotLangyongWave, level > 0, pass[Mathf.Clamp(level, 0, 4)]);
        if (positions == null) return;
        foreach (var seat in positions) {
            var panel = GetPlayerPanelByPosition(seat.Value);
            int count = seat.Key >= 0 && seat.Key < (info?.chains?.Length ?? 0) ? info.chains[seat.Key] : 0;
            panel?.SetPlayerVariable(GamePlayerPanel.VariableLangyong, count > 0 ? $"连杠：{count}" : null);
            bool ready = seat.Key >= 0 && seat.Key < (info?.declared?.Length ?? 0) && info.declared[seat.Key];
            panel?.SetPlayerVariable(GamePlayerPanel.VariableDeclaredReady, ready ? "报听" : null);
        }
    }
}
