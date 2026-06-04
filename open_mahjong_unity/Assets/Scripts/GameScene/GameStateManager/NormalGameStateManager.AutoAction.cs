using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public partial class NormalGameStateManager {
    // 等待自动操作
    private IEnumerator WaitAutoAction(string action){
        if (IsRealtimeSpectator) {
            yield break;
        }

        // 鸣牌操作自动执行
        if (action == "AutoMingPaiAction"){
            // 和牌不属于不吃碰杠
            List<string> allowHupaiAction = new List<string>{"hu_first","hu_second","hu_third"};

            // 从允许操作列表中找到实际存在的和牌操作
            string actualHupaiAction = allowActionList.FirstOrDefault(a => allowHupaiAction.Contains(a));

            // 如果开启自动胡牌，则判断
            if (AutoAction.Instance.IsAutoHepai){
                // 如果允许操作列表中有和牌，则执行自动胡牌
                if (!string.IsNullOrEmpty(actualHupaiAction)){
                    yield return new WaitForSeconds(0.2f);
                    GameCanvas.Instance.ChooseAction(actualHupaiAction, 0);
                    yield return null;
                }
            }

            // 如果和牌列表为空（没有可用的和牌操作）
            if (string.IsNullOrEmpty(actualHupaiAction)){
                // 如果开启自动过牌，则直接pass
                if (AutoAction.Instance.IsAutoPass){
                    yield return new WaitForSeconds(0.2f); // (如果玩家后悔了，希望玩家手速够快)
                    GameCanvas.Instance.ChooseAction("pass", 0);
                    yield return null;
                }
                else{
                    // 按不吃/不碰/不杠筛选：不吃排除 chi_*，不碰排除 peng，不杠排除 gang
                    // 如果筛选后剩余操作列表为空（或只剩 pass），则自动pass
                    List<string> remaining = new List<string>(allowActionList);
                    if (AutoAction.Instance.IsAutoPassChi)
                        remaining.RemoveAll(a => a == "chi_left" || a == "chi_mid" || a == "chi_right");
                    if (AutoAction.Instance.IsAutoPassPeng)
                        remaining.RemoveAll(a => a == "peng");
                    if (AutoAction.Instance.IsAutoPassGang)
                        remaining.RemoveAll(a => a == "gang");
                    remaining.RemoveAll(a => a == "pass");
                    if (remaining.Count == 0){
                        yield return new WaitForSeconds(0.2f);
                        GameCanvas.Instance.ChooseAction("pass", 0);
                        yield return null;
                    }
                }
            }

            yield return null;
        }

        // 手牌操作自动执行
        else if (action == "AutoHandAction"){
            // 如果允许操作列表有hu_self
            if (allowActionList.Contains("hu_self")){
                // 如果开启自动胡牌，则执行自动胡牌
                if (AutoAction.Instance.IsAutoHepai){
                    yield return new WaitForSeconds(0.2f);
                    GameCanvas.Instance.ChooseAction("hu_self", 0);
                    yield return null;
                }
                // 如果没有开启，转到玩家操作
                else{
                    yield return null;
                }
            }
            
            // 如果允许操作列表有buhua
            if (allowActionList.Contains("buhua")){
                // 如果开启自动补花，则执行自动补花
                if (AutoAction.Instance.IsAutoBuhua){
                    yield return new WaitForSeconds(0.3f);
                    GameCanvas.Instance.ChooseAction("buhua", 0);
                    yield return null;
                }
                // 如果没有开启，转到玩家操作
                else{
                    yield return null;
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
}
