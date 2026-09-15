using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>
/// 自动操作策略轮子：把玩家的自动设置（AutoAction 面板）与当前询问（<see cref="TurnClock"/>）合成为
/// "该不该替玩家出手、出什么、延迟多久"，并托管唯一的自动操作协程。
///
/// 只依赖词表（<see cref="ActionWords"/>）分类，不认规则名；规则专有词在各自 XxxActionWords 里归类即可被识别。
/// </summary>
public sealed class AutoActionPolicy {
    public static AutoActionPolicy Current { get; private set; } = new AutoActionPolicy();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() {
        Current = new AutoActionPolicy();
    }

    private static GameSession Session => GameSession.Current;
    private static TableMirror Mirror => TableMirror.Current;
    private static TurnClock Clock => TurnClock.Current;
    private static List<string> AllowActions => Clock.AllowActionList;

    private Coroutine waitAutoActionCoroutine;
    private const int TimeoutCutsBeforeAutoMoqie = 3;
    private int consecutiveSelfTimeoutCuts;
    private bool timeoutAutoMoqieActive;

    /// <summary>
    /// 有各自独立自动设置、词表里归为 Other 的手牌动作；出现任一即不自动摸切。
    /// 自摸类（ActionWordKind.Tsumo）另由词表判定，见 ShouldStartAutoCut。
    /// </summary>
    private static readonly string[] HandActionsBlockingAutoCut = {
        "buzhang", "angang", "jiagang", "hu_flower", "initial_hu", "sea_bottom", "buhua"
    };

    // ---- 超时自动摸切 ----

    /// <summary>
    /// 只统计服务端明确标记的自家超时切牌。达到阈值后锁存自动摸切，
    /// 此后普通切牌回包不再清零，直到玩家主动关闭自动摸切。
    /// </summary>
    public void RegisterSelfTimeoutCut(string[] actions, int actionPlayer, bool isTimeoutAction) {
        if (Session.IsRealtimeSpectator || timeoutAutoMoqieActive || actionPlayer != Session.SelfIndex ||
            actions == null || !actions.Contains("cut")) {
            return;
        }

        if (!isTimeoutAction) {
            consecutiveSelfTimeoutCuts = 0;
            return;
        }

        consecutiveSelfTimeoutCuts++;
        Debug.Log($"[AutoAction] 连续超时切牌 {consecutiveSelfTimeoutCuts}/{TimeoutCutsBeforeAutoMoqie}");
        if (consecutiveSelfTimeoutCuts < TimeoutCutsBeforeAutoMoqie) {
            return;
        }

        timeoutAutoMoqieActive = true;
        AutoAction.Instance?.EnableAutoCutFromTimeout();
        Debug.Log("[AutoAction] 连续超时达到阈值，已开启自动摸切");
    }

    public void ResetTimeoutAutoMoqieTracking() {
        consecutiveSelfTimeoutCuts = 0;
        timeoutAutoMoqieActive = false;
    }

    // ---- 协程托管 ----

    /// <summary>取消未完成的自动操作协程，避免手动/自动 ChooseAction 后过期协程再次 ClearAction。</summary>
    public void Cancel(string reason) {
        if (waitAutoActionCoroutine == null) {
            return;
        }
        if (Session.Host != null) {
            Session.Host.StopCoroutine(waitAutoActionCoroutine);
        }
        waitAutoActionCoroutine = null;
        Debug.Log($"[AutoAction] 取消 WaitAutoAction | 原因={reason}");
    }

    public void StartDelayedAutoChoose(string actionType, float delaySeconds) {
        Cancel("新协程启动");
        waitAutoActionCoroutine = Session.Host.StartCoroutine(DelayedAutoChoose(actionType, delaySeconds));
    }

    public void StartWaitAutoCut() {
        Cancel("新协程启动");
        waitAutoActionCoroutine = Session.Host.StartCoroutine(WaitAutoCut());
    }

    private void ClearCoroutineRef() {
        waitAutoActionCoroutine = null;
    }

    // ---- 决议 ----

    /// <summary>鸣牌询问语境下的"和"：荣和与自摸类都算（后者在鸣牌询问中不会出现，取并集只为省一次分类）。</summary>
    private static bool IsHuAction(string action) {
        ActionWordKind kind = ActionWords.KindOf(action);
        return kind == ActionWordKind.Ron || kind == ActionWordKind.Tsumo;
    }

    private static string GetFirstHuAction(IEnumerable<string> actions) {
        return actions.FirstOrDefault(IsHuAction);
    }

    /// <summary>
    /// 自动过牌的动作名：取当前询问里词表归为 Pass 的词（回合制为 "pass"；虹雀为 hongque_pass，
    /// 发标准 "pass" 会走回合制通道导致服务端收不到回应，亮牌窗口挂起卡死）。
    /// </summary>
    private static string ResolveAutoPassAction() {
        return ActionWords.First(AllowActions, ActionWordKind.Pass) ?? "pass";
    }

    /// <summary>
    /// 鸣牌询问：应用「不吃/不碰/不明杠」及「不点和」逐项过滤后，仍可供选择的操作（不含 pass）。
    /// 仅用于判定是否「全部跳过」可自动 pass；不做 UI 过滤。
    /// </summary>
    private static List<string> BuildRemainingActionsAfterMeldFilter(List<string> source) {
        List<string> remaining = new List<string>(source);
        remaining.RemoveAll(a => ActionWords.Is(a, ActionWordKind.Pass));
        if (AutoAction.Instance.IsPassChi) {
            remaining.RemoveAll(a => ActionWords.Is(a, ActionWordKind.Chi));
        }
        if (AutoAction.Instance.IsPassPeng) {
            remaining.RemoveAll(a => ActionWords.Is(a, ActionWordKind.Peng));
        }
        if (AutoAction.Instance.IsPassMingGang) {
            remaining.RemoveAll(a => ActionWords.Is(a, ActionWordKind.MingGang));
        }
        if (ShouldFilterRonForAutoPass(source)) {
            remaining.RemoveAll(IsHuAction);
        }
        return remaining;
    }

    /// <summary>
    /// 「不点和」是否应将荣和从剩余可操作项中剔除（纳入自动 pass 队列）。
    /// 例外：该牌既能鸣牌（吃/碰/明杠且对应过滤未开）又能点和时，不剔除，等待玩家抉择。
    /// </summary>
    private static bool ShouldFilterRonForAutoPass(List<string> allowActions) {
        if (!AutoAction.Instance.IsNoRon) {
            return false;
        }
        if (!allowActions.Any(IsHuAction)) {
            return false;
        }
        return !HasUnblockedMeldOption(allowActions);
    }

    private static bool HasUnblockedMeldOption(List<string> allowActions) {
        if (!AutoAction.Instance.IsPassPeng && ActionWords.Any(allowActions, ActionWordKind.Peng)) {
            return true;
        }
        if (!AutoAction.Instance.IsPassChi && ActionWords.Any(allowActions, ActionWordKind.Chi)) {
            return true;
        }
        if (!AutoAction.Instance.IsPassMingGang && ActionWords.Any(allowActions, ActionWordKind.MingGang)) {
            return true;
        }
        return false;
    }

    /// <summary>
    /// 鸣牌询问是否应自动 pass：仅当服务器给出的全部可操作项（不含 pass）均被筛除时为 true。
    /// 例：仅点和且开「不点和」→ 自动 pass；可碰可点和且未开「不碰」→ 保留等待玩家。
    /// </summary>
    private static bool ShouldAutoPassClaimAsk(List<string> allowActions) {
        bool hasOfferedAction = allowActions.Any(a => !ActionWords.Is(a, ActionWordKind.Pass));
        if (!hasOfferedAction) {
            return false;
        }
        return BuildRemainingActionsAfterMeldFilter(allowActions).Count == 0;
    }

    /// <summary>
    /// 鸣牌询问是否可在显示按钮前全量自动处理。
    /// 优先级：牌张跳过 → 自动和 → 鸣牌过滤后无剩余项则 pass。
    /// pass 立即发网（delay=0）；自动和保留短延迟。半自动返回 false，UI 显示服务端全集按钮。
    /// </summary>
    public bool TryResolveClaim(out string actionType, out float delaySeconds) {
        actionType = null;
        delaySeconds = 0f;
        if (Session.IsRealtimeSpectator) {
            return false;
        }

        // 1. 牌张设置命中：不询问任何操作（含荣和）
        if (AutoAction.Instance.ShouldAutoPassForCurrentDiscard()) {
            actionType = ResolveAutoPassAction();
            return true;
        }

        // 2. 自动和牌（受不点和/不抢杠约束；"和"由词表判定，规则专有词在各自 XxxActionWords 里归类）
        string huAction = GetFirstHuAction(AllowActions);
        if (!string.IsNullOrEmpty(huAction)) {
            bool shouldAutoWin = Clock.IsQiangGangAsk
                ? AutoAction.Instance.ShouldAutoWinRobKong()
                : AutoAction.Instance.ShouldAutoWinRon();
            if (shouldAutoWin) {
                actionType = huAction;
                delaySeconds = 0.3f;
                return true;
            }
        }

        // 3. 全部可操作项被筛光 → 自动 pass
        if (ShouldAutoPassClaimAsk(AllowActions)) {
            actionType = ResolveAutoPassAction();
            return true;
        }

        return false;
    }

    /// <summary>
    /// 手牌询问是否可在显示按钮前立刻自动处理（自摸/起手胡/补花）。
    /// 自动出牌仍需手牌 UI，不走此路径。
    /// </summary>
    public bool TryResolveHand(string dealTileType, out string actionType, out float delaySeconds) {
        actionType = null;
        delaySeconds = 0.3f;
        if (Session.IsRealtimeSpectator) {
            return false;
        }

        // 杠后补牌保留完整的手动决策窗口：不自动和牌、不自动补花。
        // 自动摸切由 ShouldStartAutoCut 中同一条件拦截。
        if (dealTileType == "deal_gang_tile") {
            return false;
        }

        if (AllowActions.Contains("initial_hu") && AutoAction.Instance.IsAutoHepai) {
            actionType = "initial_hu";
            return true;
        }

        string tsumoAction = ActionWords.First(AllowActions, ActionWordKind.Tsumo);
        if (tsumoAction != null
            && AutoAction.Instance.ShouldAutoWinTsumo()
            && !AutoAction.Instance.ShouldAutoPassForCurrentDraw()) {
            actionType = tsumoAction;
            return true;
        }

        if (AllowActions.Contains("hu_flower")
            && AutoAction.Instance.ShouldAutoWinTsumo()
            && !AutoAction.Instance.ShouldAutoPassForCurrentDraw()) {
            actionType = "hu_flower";
            return true;
        }

        if (AllowActions.Contains("buhua") && AutoAction.Instance.IsAutoBuhua) {
            actionType = "buhua";
            return true;
        }

        return false;
    }

    /// <summary>是否应在已显示按钮后挂起自动摸切协程。</summary>
    public bool ShouldStartAutoCut(string dealTileType) {
        if (Session.IsRealtimeSpectator || !AutoAction.Instance.IsAutoCut) {
            return false;
        }
        if (HandActionsBlockingAutoCut.Any(AllowActions.Contains)
            || ActionWords.Any(AllowActions, ActionWordKind.Tsumo)) {
            return false;
        }
        if (dealTileType == "deal_gang_tile") {
            return false;
        }
        return true;
    }

    /// <summary>自家当前摸入牌 id：优先镜像的 LastDealTileId，否则回退 2D 摸牌区标记。</summary>
    public int GetCurrentDrawTileId() {
        if (Mirror.LastDealTileId > 0) {
            return Mirror.LastDealTileId;
        }
        TileCard drawTile = GameCanvas.Instance.GetDrawTile();
        if (drawTile != null && drawTile.tileId > 0) {
            return drawTile.tileId;
        }
        return 0;
    }

    // ---- 协程体 ----

    /// <summary>延迟发送已决议的自动操作；期间不创建操作按钮。</summary>
    private IEnumerator DelayedAutoChoose(string actionType, float delaySeconds) {
        try {
            if (Session.IsRealtimeSpectator) {
                yield break;
            }
            if (delaySeconds > 0f) {
                yield return new WaitForSeconds(delaySeconds);
            } else {
                yield return null;
            }
            GameCanvas.Instance.ChooseAction(actionType, 0);
        } finally {
            ClearCoroutineRef();
        }
    }

    /// <summary>已显示按钮后的自动摸切（立直锁切等）。</summary>
    private IEnumerator WaitAutoCut() {
        try {
            if (Session.IsRealtimeSpectator || !AutoAction.Instance.IsAutoCut) {
                yield break;
            }
            yield return new WaitForSeconds(0.5f);
            if (!GameCanvas.Instance.TriggerMoqieHandCardClick()) {
                Debug.LogWarning("自动出牌失败：手牌容器中没有可出的牌");
            }
        } finally {
            ClearCoroutineRef();
        }
    }
}
