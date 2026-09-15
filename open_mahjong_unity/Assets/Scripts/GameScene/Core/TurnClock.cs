using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 回合时钟轮子：本家询问态（可选动作、询问 tick、被问的切牌）、倒计时、当前行动者指示与输入相位。
///
/// 无流程决策：族 GameState 决定"现在问本家什么"，本类负责把这一询问呈现出来（按钮/倒计时/光圈/输入相位），
/// 并在窗口关闭时收拾干净。自动操作的决议交给 <see cref="AutoActionPolicy"/>；
/// 族只在询问期间有效的临时状态（立直候选、禁切、强制切）经 <see cref="IGameState.OnAskWindowClosed"/> 通知族自行清理。
/// </summary>
public sealed class TurnClock {
    public static TurnClock Current { get; private set; } = new TurnClock();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() {
        Current = new TurnClock();
    }

    private static GameSession Session => GameSession.Current;
    private static TableMirror Mirror => TableMirror.Current;
    private static IGameState State => RuleRegistry.ActiveGameState;

    /// <summary>当前询问里本家可选的动作词。</summary>
    public List<string> AllowActionList = new List<string>();
    /// <summary>最近一次询问（手牌/鸣牌）的 action_tick，发送操作时回传给服务端用于丢弃过期提交。</summary>
    public int LastAskActionTick;
    /// <summary>当前鸣牌询问对应的切牌 id（仅来自 ask_other_action.cut_tile）。</summary>
    public int CurrentAskCutTileId;
    /// <summary>当前鸣牌询问是否来自他人加杠（抢杠和）。</summary>
    public bool IsQiangGangAsk;
    /// <summary>上一动作是加杠：下一次鸣牌询问即为抢杠询问。</summary>
    public bool PendingAskFromJiagang;
    /// <summary>当前行动者座位串。</summary>
    public string CurrentPlayer;
    /// <summary>上次 ask_hand_action 的 player_index；-1 表示本局尚未 ask，首次 ask 不收拢手牌。</summary>
    public int LastAskHandPlayerIndex = -1;
    /// <summary>当前是否在等待自己做出手牌/鸣牌操作（供回到主菜单时的红色提醒按钮判断）。</summary>
    public bool IsSelfActionRequired;
    /// <summary>本家剩余局时（开局同步）。</summary>
    public int SelfRemainingTime;

    // ---- 服务端随询问下发的切牌约束（每次 ask_hand_action 覆盖，窗口关闭时清空）----
    /// <summary>可立直切牌候选 {tile_id: [waiting_tile_id, ...]}（日麻）。</summary>
    public Dictionary<int, int[]> RiichiCandidateCuts = new Dictionary<int, int[]>();
    /// <summary>本巡禁切牌（食替：吃来源 + 两面搭子的筋）。</summary>
    public HashSet<int> ForbiddenCutTiles = new HashSet<int>();
    /// <summary>强制切牌：只能打这些摸入牌（长沙海底 / 开杠补张）。</summary>
    public HashSet<int> ForcedCutTiles = new HashSet<int>();

    /// <summary>用本次询问下发的约束覆盖（null 视为空）。</summary>
    public void SetCutConstraints(Dictionary<int, int[]> riichiCandidates, int[] forbidden, int[] forced) {
        RiichiCandidateCuts = riichiCandidates ?? new Dictionary<int, int[]>();
        ForbiddenCutTiles = forbidden != null ? new HashSet<int>(forbidden) : new HashSet<int>();
        ForcedCutTiles = forced != null ? new HashSet<int>(forced) : new HashSet<int>();
    }

    /// <summary>窗口关闭：清约束。超时只清强制切（候选/禁切留待下一次 ask 覆盖）。</summary>
    public void ClearCutConstraints(AskCloseReason reason) {
        ForcedCutTiles.Clear();
        if (reason != AskCloseReason.TimedOut) {
            RiichiCandidateCuts.Clear();
            ForbiddenCutTiles.Clear();
        }
    }

    // ---- 询问 ----

    /// <summary>
    /// 手牌询问（切牌/补花/自摸/暗杠/加杠……）。seat 为被问者；本家时弹按钮开倒计时并交自动策略，
    /// 他家时只清本家输入并更新行动者指示。
    /// </summary>
    public void BeginHandAsk(string seat, int remainingTime, int askHandPlayerIndex,
                             string dealTileType = null, int stepTimeOverride = -1) {
        int displayStepTime = stepTimeOverride >= 0 ? stepTimeOverride : Session.RoomStepTime;
        // 仅行动者换人时收拢：首次 ask、同玩家连补花后的再次 ask 均不收拢，保留摸牌区以区分手切/摸切
        bool shouldConsolidateHands = LastAskHandPlayerIndex >= 0 && askHandPlayerIndex != LastAskHandPlayerIndex;
        if (shouldConsolidateHands) {
            // 3D：跳过当前行动者，避免刚摸牌的家被收拢进主列（2D 侧轮到自己本就不 ReSetHandCards）
            Game3DManager.Instance.CheckAndRearrangeAllPlayersHandCards(seat);
        }
        if (seat == "self") {
            // 清空操作按钮 *有时候补花轮自己不补花，但是别人也不补，就出现两次按钮
            GameCanvas.Instance.ClearActionButton();
            // 锁手 / 禁切：每次询问立刻刷新自家手牌的可点状态与变灰显示
            AutoAction.Instance.SetAutoCutLocked(State != null && State.IsSelfLocked);
            GameCanvas.Instance.RefreshHandTileSelectability();

            AutoActionPolicy auto = AutoActionPolicy.Current;
            // 全量自动（自摸/起手胡/补花）：不出按钮，仅延迟发网，避免闪按钮泄密
            if (auto.TryResolveHand(dealTileType, out string autoHandAction, out float autoHandDelay)) {
                auto.StartDelayedAutoChoose(autoHandAction, autoHandDelay);
            } else {
                GameCanvas.Instance.SetActionButton(AllowActionList);
                GameCanvas.Instance.LoadingRemianTime(remainingTime, displayStepTime);
                if (auto.ShouldStartAutoCut(dealTileType)) {
                    auto.StartWaitAutoCut();
                }
            }
            // 询问操作时隐藏提示块（实时观战保持与切牌后一致的听牌提示）
            if (!Session.IsRealtimeSpectator) {
                TipsBlock.Instance.HideTipsBlock();
                TipsContainer.Instance.HideTips();
            }
            IsSelfActionRequired = true;
            GameSceneMouseInputController.Instance.SetActionInputPhase(GameSceneMouseInputController.InputPhaseAskHand);
        } else {
            if (shouldConsolidateHands) {
                GameCanvas.Instance.ChangeHandCards("ReSetHandCards", 0, null, null);
            }
            Clear("askOther"); // 重置本家命令
            IsSelfActionRequired = false;
        }
        LastAskHandPlayerIndex = askHandPlayerIndex;
        ApplyCurrentPlayerIndicator(seat);
    }

    /// <summary>鸣牌询问（吃/碰/杠/和/过），被问者一定是本家。</summary>
    public void BeginClaimAsk(int remainingTime, bool isTacticalRecheck = false, int stepTimeOverride = -1) {
        int displayStepTime = stepTimeOverride >= 0 ? stepTimeOverride : Session.RoomStepTime;
        GameCanvas.Instance.ClearActionButton();
        AutoActionPolicy auto = AutoActionPolicy.Current;
        // 全量自动（牌张跳过 / 自动和 / 筛光后 pass）：不出按钮，避免闪按钮泄密
        // pass 立即发网；自动和保留短延迟。半自动仍显示服务端全集按钮，不做 UI 过滤
        if (auto.TryResolveClaim(out string autoAction, out float autoDelay)) {
            auto.StartDelayedAutoChoose(autoAction, autoDelay);
        } else {
            GameCanvas.Instance.SetActionButton(AllowActionList);
            // 吃碰杠询问：给可操作牌（最新弃牌/加杠牌）底部显示光圈。
            if (AllowActionList.Count > 0) {
                Game3DManager.Instance?.ShowClaimGlow(CurrentAskCutTileId);
            }
            // 战术鸣牌打断窗口：remaining_time 即为 grace 秒数，不再叠加步时（避免显示 5+5）
            GameCanvas.Instance.LoadingRemianTime(remainingTime, isTacticalRecheck ? 0 : displayStepTime);
        }
        IsSelfActionRequired = true;
        GameSceneMouseInputController.Instance.SetActionInputPhase(GameSceneMouseInputController.InputPhaseAskOther);
    }

    // ---- 关闭 ----

    /// <summary>某家已执行动作：本家时关闭询问窗口并在需要时显示听牌提示。</summary>
    public void ShowActing(string seat) {
        if (seat == "self") {
            AutoActionPolicy.Current.Cancel("doAction");
        }
        GameSceneMouseInputController.Instance.SetActionInputPhase(GameSceneMouseInputController.InputPhaseNone);
        Debug.Log($"doAction行动者: {seat}");
        if (seat != "self") return;

        ClearQiangGangAskState();
        GameCanvas.Instance.StopTimeRunning();
        AllowActionList.Clear();
        GameCanvas.Instance.ClearActionButton();
        // 切牌后清切牌约束并退出族的询问期临时状态（立直选牌……；超时被迫切牌时同样会走到这里）
        ClearCutConstraints(AskCloseReason.Acted);
        State?.OnAskWindowClosed(AskCloseReason.Acted);
        // 立刻恢复手牌正常颜色，避免用户看到禁切灰色滞留到下一轮询问
        GameCanvas.Instance.RefreshHandTileSelectability();
        // 在自己执行操作以后计算听牌提示，如果有提示就显示右侧提示块
        if (Session.Tips) {
            TipsBlock.Instance.ShowTipsBlock(Mirror.SelfHandTiles, Mirror.Self.combination_tiles);
        }
        IsSelfActionRequired = false;
    }

    /// <summary>清空本家询问：停计时、清按钮、清可选动作，并通知族清理询问期状态。</summary>
    public void Clear(string reason = null) {
        AutoActionPolicy.Current.Cancel(reason ?? "ClearAction");
        ClearQiangGangAskState();
        GameSceneMouseInputController.Instance.ClearStaleHandInput("ClearAction");
        GameCanvas.Instance.StopTimeRunning();
        GameCanvas.Instance.ClearActionButton();
        AllowActionList.Clear();
        ClearCutConstraints(AskCloseReason.Cleared);
        State?.OnAskWindowClosed(AskCloseReason.Cleared);
        IsSelfActionRequired = false;
        GameSceneMouseInputController.Instance.SetActionInputPhase(GameSceneMouseInputController.InputPhaseNone);
    }

    /// <summary>步时耗尽：清按钮与输入，服务端随后会代为出牌/过。</summary>
    public void Timeout() {
        AutoActionPolicy.Current.Cancel("TimeOut");
        ClearQiangGangAskState();
        GameSceneMouseInputController.Instance.ClearStaleHandInput("TimeOut");
        GameCanvas.Instance.ClearActionButton();
        ClearCutConstraints(AskCloseReason.TimedOut);
        State?.OnAskWindowClosed(AskCloseReason.TimedOut);
        IsSelfActionRequired = false;
        GameSceneMouseInputController.Instance.SetActionInputPhase(GameSceneMouseInputController.InputPhaseNone);
    }

    public void ApplyCurrentPlayerIndicator(string seat) {
        BoardCanvas.Instance.ShowCurrentPlayer(seat, Mirror.RemainTiles);
        CurrentPlayer = seat;
    }

    public void ClearQiangGangAskState() {
        IsQiangGangAsk = false;
        PendingAskFromJiagang = false;
        CurrentAskCutTileId = 0;
    }

    /// <summary>退出对局/牌谱/观战。</summary>
    public void ResetForExit() {
        AutoActionPolicy.Current.Cancel("ResetForExit");
        IsSelfActionRequired = false;
        AllowActionList.Clear();
        CurrentAskCutTileId = 0;
        CurrentPlayer = null;
        LastAskHandPlayerIndex = -1;
        IsQiangGangAsk = false;
        PendingAskFromJiagang = false;
        RiichiCandidateCuts.Clear();
        ForbiddenCutTiles.Clear();
        ForcedCutTiles.Clear();
    }
}
