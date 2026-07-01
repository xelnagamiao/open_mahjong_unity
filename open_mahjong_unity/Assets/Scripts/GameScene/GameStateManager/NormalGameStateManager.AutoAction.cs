using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public partial class NormalGameStateManager {
    private Coroutine waitAutoActionCoroutine;

    /// <summary>取消未完成的自动操作协程，避免手动/自动 ChooseAction 后过期协程再次 ClearAction。</summary>
    public void CancelWaitAutoAction(string reason) {
        if (waitAutoActionCoroutine == null) {
            return;
        }
        StopCoroutine(waitAutoActionCoroutine);
        waitAutoActionCoroutine = null;
        Debug.Log($"[AutoAction] 取消 WaitAutoAction | 原因={reason}");
    }

    private void StartWaitAutoAction(string action) {
        CancelWaitAutoAction("新协程启动");
        waitAutoActionCoroutine = StartCoroutine(WaitAutoAction(action));
    }

    private void ClearWaitAutoActionCoroutineRef() {
        waitAutoActionCoroutine = null;
    }

    // 等待自动操作
    private IEnumerator WaitAutoAction(string action){
        if (IsRealtimeSpectator) {
            yield break;
        }

        try {
            // 鸣牌操作自动执行
            if (action == "AutoMingPaiAction"){
                List<string> allowHupaiAction = new List<string>{"hu", "hu_first", "hu_second", "hu_third"};
                string actualHupaiAction = allowActionList.FirstOrDefault(a => allowHupaiAction.Contains(a));

                // 牌张设置：勾选牌自动过（含荣和）
                if (AutoAction.Instance.ShouldAutoPassForCurrentDiscard()){
                    yield return new WaitForSeconds(0.2f);
                    GameCanvas.Instance.ChooseAction("pass", 0);
                    yield break;
                }
                // 自动和牌：抢杠与普通荣和分别受「不抢杠」「不点和」约束，互不覆盖
                if (!string.IsNullOrEmpty(actualHupaiAction)){
                    bool isQiangGangAsk = NormalGameStateManager.Instance != null && NormalGameStateManager.Instance.IsQiangGangAsk;
                    bool shouldAutoWin = isQiangGangAsk
                        ? AutoAction.Instance.ShouldAutoWinRobKong()
                        : AutoAction.Instance.ShouldAutoWinRon();
                    if (shouldAutoWin){
                        yield return new WaitForSeconds(0.2f);
                        GameCanvas.Instance.ChooseAction(actualHupaiAction, 0);
                        yield break;
                    }
                }

                if (AutoAction.Instance.IsAutoPass){
                    yield return new WaitForSeconds(0.2f);
                    GameCanvas.Instance.ChooseAction("pass", 0);
                    yield break;
                }

                List<string> remaining = new List<string>(allowActionList);
                if (AutoAction.Instance.IsPassChi)
                    remaining.RemoveAll(a => a == "chi_left" || a == "chi_mid" || a == "chi_right");
                if (AutoAction.Instance.IsPassPeng)
                    remaining.RemoveAll(a => a == "peng");
                if (AutoAction.Instance.IsPassMingGang)
                    remaining.RemoveAll(a => a == "gang");
                remaining.RemoveAll(a => a == "pass");
                if (remaining.Count == 0){
                    yield return new WaitForSeconds(0.2f);
                    GameCanvas.Instance.ChooseAction("pass", 0);
                    yield break;
                }

                yield return null;
            }

            // 手牌操作自动执行
            else if (action == "AutoHandAction"){
                // 如果允许操作列表有hu_self
                if (allowActionList.Contains("hu_self")){
                    // 自动胡牌开启且未勾选「不自摸」时执行自动自摸
                    if (AutoAction.Instance.ShouldAutoWinTsumo()){
                        yield return new WaitForSeconds(0.2f);
                        GameCanvas.Instance.ChooseAction("hu_self", 0);
                        yield break;
                    }
                }

                // 如果允许操作列表有buhua
                if (allowActionList.Contains("buhua")){
                    // 如果开启自动补花，则执行自动补花
                    if (AutoAction.Instance.IsAutoBuhua){
                        yield return new WaitForSeconds(0.3f);
                        GameCanvas.Instance.ChooseAction("buhua", 0);
                        yield break;
                    }
                }

                List<string> allowActionWithoutCut = new List<string>{"angang","jiagang","hu_self","buhua"};
                // 如果允许操作列表有除去cut的其他操作 则转到玩家操作
                if (allowActionWithoutCut.Any(allowActionList.Contains)){
                    yield return null;
                }
                // 如果上次摸牌类型是杠牌，不执行自动出牌
                else if (lastDealTileType == "deal_gang_tile"){
                    yield return null;
                }
                // 如果没有，则执行自动出牌
                else{
                    if (AutoAction.Instance.IsAutoCut){
                        float autoCutDelay = AutoAction.Instance.IsAutoCutLocked ? 0.3f : 0.5f;
                        yield return new WaitForSeconds(autoCutDelay);
                        if (!GameCanvas.Instance.TriggerMoqieHandCardClick()) {
                            Debug.LogWarning("自动出牌失败：手牌容器中没有可出的牌");
                        }
                    }
                    else{
                        yield return null;
                    }
                }
            }

            else{
                Debug.LogWarning($"未知操作: {action}");
            }
        }
        finally {
            ClearWaitAutoActionCoroutineRef();
        }
    }
}
