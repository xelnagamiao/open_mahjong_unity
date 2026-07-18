using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public partial class NormalGameStateManager {
    private Coroutine waitAutoActionCoroutine;

    private static readonly HashSet<string> HuActionNames = new HashSet<string> {
        "hu", "hu_first", "hu_second", "hu_third"
    };

    private static readonly string[] HandActionsBlockingAutoCut = {
        "buzhang", "angang", "jiagang", "hu_self", "initial_hu", "sea_bottom", "buhua"
    };

    /// <summary>取消未完成的自动操作协程，避免手动/自动 ChooseAction 后过期协程再次 ClearAction。</summary>
    public void CancelWaitAutoAction(string reason) {
        if (waitAutoActionCoroutine == null) {
            return;
        }
        StopCoroutine(waitAutoActionCoroutine);
        waitAutoActionCoroutine = null;
        Debug.Log($"[AutoAction] 取消 WaitAutoAction | 原因={reason}");
    }

    private void StartDelayedAutoChoose(string actionType, float delaySeconds) {
        CancelWaitAutoAction("新协程启动");
        waitAutoActionCoroutine = StartCoroutine(DelayedAutoChoose(actionType, delaySeconds));
    }

    private void StartWaitAutoCut() {
        CancelWaitAutoAction("新协程启动");
        waitAutoActionCoroutine = StartCoroutine(WaitAutoCut());
    }

    private void ClearWaitAutoActionCoroutineRef() {
        waitAutoActionCoroutine = null;
    }

    private static bool IsHuAction(string action) {
        return HuActionNames.Contains(action);
    }

    private static string GetFirstHuAction(IEnumerable<string> actions) {
        return actions.FirstOrDefault(IsHuAction);
    }

    /// <summary>
    /// 鸣牌询问：应用「不吃/不碰/不明杠」及「不点和」逐项过滤后，仍可供选择的操作（不含 pass）。
    /// 仅用于判定是否「全部跳过」可自动 pass；不做 UI 过滤。
    /// </summary>
    private static List<string> BuildRemainingActionsAfterMeldFilter(List<string> source) {
        List<string> remaining = new List<string>(source);
        remaining.RemoveAll(a => a == "pass");
        if (AutoAction.Instance.IsPassChi) {
            remaining.RemoveAll(a => a == "chi_left" || a == "chi_mid" || a == "chi_right");
        }
        if (AutoAction.Instance.IsPassPeng) {
            remaining.RemoveAll(a => a == "peng");
        }
        if (AutoAction.Instance.IsPassMingGang) {
            remaining.RemoveAll(a => a == "gang");
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
        if (!AutoAction.Instance.IsPassPeng && allowActions.Contains("peng")) {
            return true;
        }
        if (!AutoAction.Instance.IsPassChi &&
            (allowActions.Contains("chi_left") || allowActions.Contains("chi_mid") || allowActions.Contains("chi_right"))) {
            return true;
        }
        if (!AutoAction.Instance.IsPassMingGang && allowActions.Contains("gang")) {
            return true;
        }
        return false;
    }

    /// <summary>
    /// 鸣牌询问是否应自动 pass：仅当服务器给出的全部可操作项（不含 pass）均被筛除时为 true。
    /// 例：仅点和且开「不点和」→ 自动 pass；可碰可点和且未开「不碰」→ 保留等待玩家。
    /// </summary>
    private static bool ShouldAutoPassMingPaiAsk(List<string> allowActions) {
        bool hasOfferedAction = allowActions.Any(a => a != "pass");
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
    private bool TryResolveFullAutoMingPai(out string actionType, out float delaySeconds) {
        actionType = null;
        delaySeconds = 0f;
        if (IsRealtimeSpectator) {
            return false;
        }

        // 1. 牌张设置命中：不询问任何操作（含荣和）
        if (AutoAction.Instance.ShouldAutoPassForCurrentDiscard()) {
            actionType = "pass";
            return true;
        }

        // 2. 自动和牌（受不点和/不抢杠约束）
        string huAction = GetFirstHuAction(allowActionList);
        if (!string.IsNullOrEmpty(huAction)) {
            bool shouldAutoWin = IsQiangGangAsk
                ? AutoAction.Instance.ShouldAutoWinRobKong()
                : AutoAction.Instance.ShouldAutoWinRon();
            if (shouldAutoWin) {
                actionType = huAction;
                delaySeconds = 0.3f;
                return true;
            }
        }

        // 3. 全部可操作项被筛光 → 自动 pass
        if (ShouldAutoPassMingPaiAsk(allowActionList)) {
            actionType = "pass";
            return true;
        }

        return false;
    }

    /// <summary>
    /// 手牌询问是否可在显示按钮前立刻自动处理（自摸/起手胡/补花）。
    /// 自动出牌仍需手牌 UI，不走此路径。
    /// </summary>
    private bool TryResolveImmediateAutoHand(out string actionType, out float delaySeconds) {
        actionType = null;
        delaySeconds = 0.3f;
        if (IsRealtimeSpectator) {
            return false;
        }

        if (allowActionList.Contains("initial_hu") && AutoAction.Instance.IsAutoHepai) {
            actionType = "initial_hu";
            return true;
        }

        if (allowActionList.Contains("hu_self")
            && AutoAction.Instance.ShouldAutoWinTsumo()
            && !AutoAction.Instance.ShouldAutoPassForCurrentDraw()) {
            actionType = "hu_self";
            return true;
        }

        if (allowActionList.Contains("buhua") && AutoAction.Instance.IsAutoBuhua) {
            actionType = "buhua";
            return true;
        }

        return false;
    }

    /// <summary>是否应在已显示按钮后挂起自动摸切协程。</summary>
    private bool ShouldStartAutoCut() {
        if (IsRealtimeSpectator || !AutoAction.Instance.IsAutoCut) {
            return false;
        }
        if (HandActionsBlockingAutoCut.Any(allowActionList.Contains)) {
            return false;
        }
        if (lastDealTileType == "deal_gang_tile") {
            return false;
        }
        return true;
    }

    /// <summary>自家当前摸入牌 id：优先 lastDealTileId，否则回退 2D 摸牌区标记。</summary>
    public int GetCurrentDrawTileId() {
        if (lastDealTileId > 0) {
            return lastDealTileId;
        }
        TileCard drawTile = GameCanvas.Instance.GetDrawTile();
        if (drawTile != null && drawTile.tileId > 0) {
            return drawTile.tileId;
        }
        return 0;
    }

    /// <summary>延迟发送已决议的自动操作；期间不创建操作按钮。</summary>
    private IEnumerator DelayedAutoChoose(string actionType, float delaySeconds) {
        try {
            if (IsRealtimeSpectator) {
                yield break;
            }
            if (delaySeconds > 0f) {
                yield return new WaitForSeconds(delaySeconds);
            }
            else {
                yield return null;
            }
            GameCanvas.Instance.ChooseAction(actionType, 0);
        }
        finally {
            ClearWaitAutoActionCoroutineRef();
        }
    }

    /// <summary>已显示按钮后的自动摸切（立直锁切等）。</summary>
    private IEnumerator WaitAutoCut() {
        try {
            if (IsRealtimeSpectator || !AutoAction.Instance.IsAutoCut) {
                yield break;
            }
            yield return new WaitForSeconds(0.5f);
            if (!GameCanvas.Instance.TriggerMoqieHandCardClick()) {
                Debug.LogWarning("自动出牌失败：手牌容器中没有可出的牌");
            }
        }
        finally {
            ClearWaitAutoActionCoroutineRef();
        }
    }
}
