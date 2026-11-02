using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

public sealed class DuocaiGameState : TurnBasedGameState {
    public const string RuleId = "duocai", SubRule = "duocai/guobiao", RuleVersion = "duocai-v1";
    public static DuocaiGameState Active => RuleRegistry.ActiveGameState as DuocaiGameState;
    public DuocaiInfo Info { get; private set; }
    public readonly DuocaiHintsCache Hints = new DuocaiHintsCache();
    private readonly DuocaiAskClock askClock = new DuocaiAskClock();
    private Dictionary<int, int[]> endHands;
    private GameInfo latestInfo;
    private DuocaiChoice[] choices = Array.Empty<DuocaiChoice>();
    private readonly HashSet<int> riichiSeats = new HashSet<int>();
    private int generation, clockHand;
    private string clockGame;
    private bool settlementAccepted, pendingSettlement;
    private Response pendingReady;
    private UnityEngine.Coroutine settlementCoroutine;
    private Game3DManager settlementHost;
    private bool transitionHeld;
    private readonly Queue<(string suffix,Response response)> transitionMessages=new Queue<(string,Response)>();

    public override bool HandleMessage(string suffix,Response response) {
        if(transitionMessages.Count>0||(transitionHeld&&(suffix=="game_start"||suffix=="game_end"))) {
            transitionMessages.Enqueue((suffix,response));return true;
        }
        return base.HandleMessage(suffix,response);
    }

    private void Accept(GameInfo info) {
        if (info == null) return;
        latestInfo = info;
        Info = info.duocai_info ?? Info;
        Hints.Accept(info.duocai_hints);
        DuocaiTableIndicators.Refresh(Info, info.players_info);
    }
    protected override void OnRoundStarted(GameInfo info) {
        if (clockHand != info.duocai_info?.hand_number || clockGame != Session.GamestateId) {
            askClock.Reset(); clockHand = info.duocai_info?.hand_number ?? 0; clockGame = Session.GamestateId;
        }
        riichiSeats.Clear();
        ResetSettlement(); Info = null; endHands = null; Hints.Reset(); Accept(info);
        foreach(var player in info.players_info??Array.Empty<PlayerInfo>())if(player.duocai_riichi>0&&Mirror.TryGetSeat(player.player_index,out var seat)) {
            riichiSeats.Add(player.player_index);Game3DManager.Instance?.PlaceRiichiTenbouAt(seat);
        }
        if (Session.Tips && Mirror.Self != null && TipsBlock.Instance != null)
            TipsBlock.Instance.ShowTipsBlock(Mirror.SelfHandTiles, Mirror.Self.combination_tiles ?? new List<string>());
    }
    protected override void OnAskHandAction(Response response) {
        Accept(response.game_info);
        var info = response.ask_hand_action_info;
        if (info == null || askClock.IsClosed(info.action_tick)) return;
        choices = info.duocai_choices ?? Array.Empty<DuocaiChoice>();
        info.action_list = ChoiceWords(info.action_list);
        info.riichi_candidate_cuts = (info.riichi_cut_tiles ?? Array.Empty<int>()).ToDictionary(v=>v,v=>Array.Empty<int>());
        base.OnAskHandAction(response);
        PresentClock(response, info.action_tick, info.remaining_time, info.step_remaining ?? Session.RoomStepTime);
    }
    protected override void OnAskClaim(Response response) {
        Accept(response.game_info);
        var info = response.ask_other_action_info;
        if (info == null || askClock.IsClosed(info.action_tick)) return;
        Clock.PendingAskFromJiagang = false;
        choices = info.duocai_choices ?? Array.Empty<DuocaiChoice>();
        info.action_list = ChoiceWords(info.action_list);
        base.OnAskClaim(response);
        PresentClock(response, info.action_tick, info.remaining_time, info.step_remaining ?? Session.RoomStepTime);
    }
    private void PresentClock(Response response, int tick, int bank, int step) {
        if (!Clock.IsSelfActionRequired) return;
        if (Info?.clock_active == false || Clock.AllowActionList.Count == 0) { Clock.Clear("duocaiAnswered"); return; }
        double now = DuocaiAskClock.Now;
        askClock.Project(tick, Info?.clock_remaining_ms ?? (bank + (double)step) * 1000,
            Info?.clock_step_remaining_ms ?? step * 1000d, response.received_monotonic ?? now, now, out double remaining, out double stepLeft);
        GameCanvas.Instance?.LoadingRemianTime(remaining, stepLeft);
    }
    public override void OnAskWindowClosed(AskCloseReason reason) {
        if (reason == AskCloseReason.Acted || reason == AskCloseReason.TimedOut) askClock.Close();
        RiichiCutSelectionController.Instance?.ExitRiichiCutMode();
        base.OnAskWindowClosed(reason);
    }
    protected override void OnDoAction(Response response) {
        latestAction=response.do_action_info;
        Accept(response.game_info); base.OnDoAction(response);
        if(Session.Tips&&Mirror.Self!=null&&Mirror.SelfHandTiles.Count%3==1)
            TipsBlock.Instance?.ShowTipsBlock(Mirror.SelfHandTiles,Mirror.Self.combination_tiles??new List<string>());
    }
    private static string[] ChoiceWords(string[] words) => (words ?? Array.Empty<string>()).Select(w=>new[]{"chi_left","peng","gang","angang","jiagang"}.Contains(w)?"duocai_group:"+w:w).Distinct().ToArray();
    public IReadOnlyList<ActionCandidate> Candidates(string group) => choices.Where(c=>c.action==group.Substring("duocai_group:".Length))
        .Select(c=>new ActionCandidate("duocai_choice:"+c.id,c.take,caption:c.meld_index>=0?$"扩杠 · 第{c.meld_index+1}组":null)).ToArray();
    public override bool TryChooseAction(string action) {
        if(!action.StartsWith("duocai_choice:"))return false;
        var choice=choices.FirstOrDefault(c=>c.id==action.Substring("duocai_choice:".Length));
        if(choice==null||!Clock.IsSelfActionRequired)return true;
        AutoActionPolicy.Current.Cancel("duocaiChoice");
        Manager.SwitchCurrentPlayer("self","ClearAction",0);
        GameStateNetworkManager.Instance.SendAction(choice.action,0,choiceId:choice.id);
        return true;
    }
    public override bool IsSelfLocked => latestInfo?.players_info?.FirstOrDefault(p=>p.player_index==Session.SelfIndex)?.duocai_riichi>0;
    protected override bool TryApplyFamilyWord(TableAction action,string word) {
        if(word=="riichi") {
            var player=latestInfo?.players_info?.FirstOrDefault(p=>p.player_index==action.PlayerIndex);
            if(player!=null){Mirror.Info(action.Seat).score=player.score;Mirror.Info(action.Seat).tag_list=player.tag_list??Array.Empty<string>();Presenter.ApplyScores(new Dictionary<int,int>{{player.player_index,player.score}});}
            if(riichiSeats.Add(action.PlayerIndex))Game3DManager.Instance?.PlayRiichiTenbouFlight(action.Seat);
            return true;
        }
        if(word=="declare_kan")return true;
        if(!new[]{"chi_left","peng","gang","angang","jiagang","rob_kan"}.Contains(word))return false;
        var info=latestInfo?.players_info?.FirstOrDefault(p=>p.player_index==action.PlayerIndex);
        var target=Mirror.Info(action.Seat);if(info==null||target==null)return true;
        var removed=new List<int>();
        if(action.Seat=="self") {
            var remaining=new List<int>(info.hand_tiles??Array.Empty<int>());
            foreach(int tile in Mirror.SelfHandTiles)if(!remaining.Remove(tile))removed.Add(tile);
            Mirror.SelfHandTiles=new List<int>(info.hand_tiles??Array.Empty<int>());
            GameCanvas.Instance?.ChangeHandCards("RemoveCombinationCard",0,removed.ToArray(),null);
        } else removed.AddRange(Enumerable.Repeat(0,Math.Max(0,target.hand_tiles_count-info.hand_tiles_count)));
        target.hand_tiles_count=info.hand_tiles_count;
        target.combination_tiles=(info.combination_tiles??Array.Empty<string>()).ToList();
        target.combination_masks=(info.combination_mask??Array.Empty<int[]>()).ToList();
        if(word=="rob_kan") {
            Game3DManager.Instance?.RemoveHongqueKongHandTile(action.Seat,latestAction?.cut_tile??0);
            return true;
        }
        int index=latestAction?.duocai_meld_index??target.combination_masks.Count-1;
        string discarder=new[]{"chi_left","peng","gang"}.Contains(word)?Mirror.SeatOf(latestAction?.cut_from_player??-1):null;
        int claimed=latestAction?.cut_tile??0;
        if(discarder!=null)Mirror.RemoveClaimedDiscard(discarder,claimed);
        if(index>=0&&index<target.combination_masks.Count)Game3DManager.Instance?.PlayFreeMeld(action.Seat,target.combination_masks[index],index,removed.ToArray(),discarder,claimed,
            word=="jiagang"?target.combination_masks.Select(mask=>(int[])mask.Clone()).ToArray():null);
        return true;
    }
    private DoActionInfo latestAction;
    public override void OnPlayerTagsRefreshed() {
        base.OnPlayerTagsRefreshed();
        if (Session.Tips) TipsContainer.Instance?.RefreshTenpaiTipsIfCached();
    }
    public WaitTileHint DescribeWaiting(WaitHintQuery q) => Hints.Describe(q);
    protected override void OnShowResult(Response response) {
        if (settlementAccepted || response.show_result_info == null) return;
        settlementAccepted = true; Accept(response.game_info);
        Info = response.show_result_info.duocai_info ?? Info;
        endHands = response.show_result_info.duocai_end_hands;
        pendingSettlement = transitionHeld = true; Presenter.CloseTableForSettlement();
        settlementHost = Game3DManager.Instance;
        if (settlementHost != null) settlementCoroutine = settlementHost.StartCoroutine(SettlementSync.PreserveContext(WaitForTable(response, generation)));
        else {CompleteSettlement(response, generation);ReleaseTransition();}
    }
    private IEnumerator WaitForTable(Response response, int expected) {
        yield return null;
        int idle = 0;
        while (Active == this && generation == expected && settlementHost != null) {
            bool pending = settlementHost.HasPendingRecordTableAnimations || GameCanvas.Instance?.IsChangeHandCardProcessing == true;
            idle = pending ? 0 : idle + 1;
            if (idle >= 2) break;
            yield return null;
        }
        if (Active != this || generation != expected) yield break;
        CompleteSettlement(response, expected);
        if(response.show_result_info.hu_class=="liuju")
            yield return new UnityEngine.WaitForSeconds(RoundEndTiming.RoundEndPresentationFadeSeconds+RoundEndPresentation.DrawCaptionHoldSeconds);
        else {
            while(Active==this&&generation==expected&&EndResultPanel.Instance!=null&&!EndResultPanel.Instance.HasPresentedTotal)yield return null;
            double until=UnityEngine.Time.realtimeSinceStartupAsDouble+RoundEndTiming.HuConfirmCountdownSeconds;
            while(Active==this&&generation==expected&&EndResultPanel.Instance!=null&&!EndResultPanel.Instance.HasConfirmedResult
                &&UnityEngine.Time.realtimeSinceStartupAsDouble<until)yield return null;
        }
        if(Active==this&&generation==expected)ReleaseTransition();
    }
    private void CompleteSettlement(Response response, int expected) {
        if (expected != generation) return;
        pendingSettlement = false;
        var ready = pendingReady; pendingReady = null;
        foreach(var player in latestInfo?.players_info??Array.Empty<PlayerInfo>()) {
            var target=Mirror.Info(Mirror.SeatOf(player.player_index));
            var masks=player.combination_mask??Array.Empty<int[]>();
            if(target==null)continue;
            if(target.combination_masks!=null&&target.combination_masks.Count==masks.Length
                &&target.combination_masks.Select((mask,i)=>mask.SequenceEqual(masks[i])).All(equal=>equal))continue;
            target.combination_tiles=(player.combination_tiles??Array.Empty<string>()).ToList();
            target.combination_masks=masks.Select(mask=>(int[])mask.Clone()).ToList();
            Game3DManager.Instance?.RebuildPlayerMelds(Mirror.SeatOf(player.player_index));
        }
        base.OnShowResult(response);
        if (ready != null) base.OnReadyStatus(ready);
    }
    private void ReleaseTransition() {
        settlementCoroutine=null;settlementHost=null;transitionHeld=false;
        var messages=transitionMessages.ToArray();transitionMessages.Clear();
        foreach(var message in messages)HandleMessage(message.suffix,message.response);
    }
    protected override SettlementEnvelope BuildEnvelope(ShowResultInfo info) {
        var env = base.BuildEnvelope(info);
        int expected = generation;
        if (!env.IsLiuju) env.AfterHandReveal = () => {
            if (Active == this && generation == expected)
                Game3DManager.Instance?.RevealSichuanLiujuAllHands(endHands?.Where(p => p.Key != env.WinnerIndex).ToDictionary(p => p.Key, p => p.Value));
        };
        return env;
    }
    protected override void OnTableClosedForSettlement(SettlementEnvelope env) {
        Game3DManager.Instance?.ClearAllRiichiTenbous();
        if (env.IsLiuju) Game3DManager.Instance?.RevealSichuanLiujuAllHands(endHands);
    }
    protected override void OnSettlementPresented(SettlementEnvelope env, ShowResultInfo info) {
        if (env.IsLiuju) Presenter.ApplyScores(env.ScoresAfter);
    }
    protected override void AppendScoreboard(SettlementEnvelope env) {
        if (SettlementSync.IsRestoring) return;
        if (Info?.start_scores?.Length == 4) {
            int rows = 0; bool replaced = false;
            foreach (var seat in Mirror.IndexToPosition) {
                var player = Mirror.Info(seat.Value); if (player == null) continue;
                player.score = Info.start_scores[seat.Key];
                if (Info.hand_number > 0 && player.round_number_history?.Count > 0 && player.score_history?.Count >= Info.hand_number
                    && player.round_number_history.Last() == Mirror.CurrentRound) {
                    replaced = true; rows = Math.Max(rows, player.score_history.Count);
                    player.round_number_history.RemoveAt(player.round_number_history.Count - 1);
                    player.score_history.RemoveAt(player.score_history.Count - 1);
                }
            }
            if (replaced && Mirror.RoundSettlementHistory.Count == rows) Mirror.RoundSettlementHistory.RemoveAt(rows - 1);
        }
        base.AppendScoreboard(env);
    }
    protected override void OnReadyStatus(Response response) {
        if (response.ready_status_info == null) return;
        Clock.LastAskActionTick = response.ready_status_info.action_tick;
        Info = response.ready_status_info.duocai_info ?? Info;
        DuocaiTableIndicators.Refresh(Info);
        if (pendingSettlement) pendingReady = response; else base.OnReadyStatus(response);
    }
    private void ResetSettlement() {
        generation++;
        if (settlementCoroutine != null && settlementHost != null) settlementHost.StopCoroutine(settlementCoroutine);
        settlementCoroutine = null; settlementHost = null; settlementAccepted = pendingSettlement = false;
        pendingReady = null;transitionHeld=false;transitionMessages.Clear();RoundEndPresentation.Instance?.StopActiveSequence();
    }
    public override void OnSessionReset() {
        ResetSettlement(); askClock.Reset(); clockHand = 0; clockGame = null;
        Info = null; latestInfo=null; latestAction=null; choices=Array.Empty<DuocaiChoice>(); riichiSeats.Clear(); endHands = null; Hints.Reset(); DuocaiTableIndicators.Refresh(null);
    }
}
