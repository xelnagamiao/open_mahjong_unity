using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public sealed class NewChineseGameState : TurnBasedGameState {
    public const string RuleId = "new_chinese", SubRule = "new_chinese/standard";
    public static NewChineseGameState Active => RuleRegistry.ActiveGameState as NewChineseGameState;
    public NewChineseInfo Info {get; private set;}

    private void Accept(GameInfo info) {
        if (info?.new_chinese == null) return;
        Info = info.new_chinese;
        RefreshSelfStatusIndicators();
    }

    protected override void OnRoundStarted(GameInfo info) {
        GameCanvas.Instance?.HideXueliuThrowThreeSelection();
        Accept(info);
        RefreshHints(info);
    }

    private void RefreshHints(GameInfo snapshot) {
        if (!Session.Tips || snapshot?.self_hand_tiles == null || Mirror.Self == null) return;
        var hand = snapshot.self_hand_tiles.ToList();
        if (Info?.self_has_draw_slot == true && hand.Count % 3 == 2) hand.RemoveAt(hand.Count-1);
        TipsBlock.Instance?.ShowTipsBlock(hand, Mirror.Self.combination_tiles ?? new List<string>());
    }

    protected override void OnAskHandAction(Response response) {
        Accept(response.game_info);
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
        if (words != null && (words.Contains("new_chinese_exchange") || words.Contains("rob_kan")) &&
            response.game_info?.self_hand_tiles != null) {
            GameCanvas.Instance?.HideNewChineseExchange();
            Manager.InitializeGame(true, "", response.game_info);
            return;
        }
        base.OnDoAction(response);
        if (words != null && (words.Contains("new_chinese_bao_change") || words.Contains("new_chinese_bao_reveal")))
            RefreshHints(response.game_info);
    }

    protected override bool TryApplyFamilyWord(TableAction action, string word) {
        if (word == "new_chinese_bao_change") {
            if (!action.Silent) EndLiujuPanel.Instance?.ShowLiujuPanel("换宝", null, 1f);
            return true;
        }
        return word == "new_chinese_bao_reveal" || word == "declare_kan" || word == "rob_kan";
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
