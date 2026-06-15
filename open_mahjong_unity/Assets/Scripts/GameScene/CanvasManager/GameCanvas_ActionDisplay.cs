// GameCanvas 操作文本

using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public partial class GameCanvas{
    // 显示操作文本
    public void ShowActionDisplay(string playerPosition, string actionType, string roomRule = null) {
        Transform displayPos = null;
        if (playerPosition == "self"){
            displayPos = SelfActionDisplayPos;
        } else if (playerPosition == "left"){
            displayPos = LeftActionDisplayPos;
        } else if (playerPosition == "right"){
            displayPos = RightActionDisplayPos;
        } else if (playerPosition == "top"){
            displayPos = TopActionDisplayPos;
        }
        if (displayPos == null) return;
        
        string displayText = GetActionDisplayText(actionType, roomRule);
        if (string.IsNullOrEmpty(displayText)) return;

        // 同一座位上若已有操作文本（含未走完渐隐协程的残留），先清掉再显示，避免叠字/卡死
        ClearActionDisplayAt(displayPos);

        // 实例化操作显示文本
        GameObject actionTextObj = Instantiate(ActionDisplayText, displayPos);
        
        // 设置文本内容
        TMP_Text actionText = actionTextObj.GetComponent<TMP_Text>();
        actionText.text = displayText;
        
        // 启动渐变消失协程
        StartCoroutine(FadeOutActionDisplay(actionTextObj,displayPos));
    }

    /// <summary>销毁指定座位锚点下所有操作文本实例（不依赖渐隐协程）。</summary>
    private void ClearActionDisplayAt(Transform displayPos) {
        if (displayPos == null) return;
        for (int i = displayPos.childCount - 1; i >= 0; i--) {
            Transform child = displayPos.GetChild(i);
            if (child != null) {
                Destroy(child.gameObject);
            }
        }
    }

    /// <summary>
    /// 清空四个座位的全部操作文本（补花/碰/吃/杠/和等）。
    /// 退出对局/牌谱/观战、初始化与跳转回放时调用：FadeOutActionDisplay 协程依附于本 GameCanvas，
    /// 在 gameObject 被 SetActive(false) 时会被中断而不销毁文本实例，导致「补花」等字残留卡死，
    /// 这里直接销毁实例兜底。
    /// </summary>
    public void ClearActionDisplay() {
        ClearActionDisplayAt(SelfActionDisplayPos);
        ClearActionDisplayAt(LeftActionDisplayPos);
        ClearActionDisplayAt(RightActionDisplayPos);
        ClearActionDisplayAt(TopActionDisplayPos);
    }

    private string GetHuSelfActionText(string roomRule = null) {
        string rule = !string.IsNullOrEmpty(roomRule) ? roomRule : NormalGameStateManager.Instance?.roomRule;
        string subRule = NormalGameStateManager.Instance?.subRule;
        if (rule == "guobiao" || (subRule != null && subRule.StartsWith("guobiao/"))) {
            return "和";
        }
        return "自摸";
    }

    private string GetActionDisplayText(string actionType, string roomRule) {
        if (actionType == "chi_left" || actionType == "chi_mid" || actionType == "chi_right"){
            return "吃";
        } else if (actionType == "peng"){
            return "碰";
        } else if (actionType == "angang" || actionType == "jiagang" || actionType == "gang"){
            return "杠";
        } else if (actionType == "hu_self" || actionType == "hu_first" || actionType == "hu_second" || actionType == "hu_third"){
            string rule = !string.IsNullOrEmpty(roomRule) ? roomRule : NormalGameStateManager.Instance.roomRule;
            if (actionType == "hu_self"){
                return GetHuSelfActionText(roomRule);
            }
            if (rule == "riichi") return "荣";
            return "和";
        } else if (actionType == "buhua"){
            return "补花";
        } else if (actionType == "riichi"){
            return "立直";
        }
        return string.Empty;
    }

    /// <summary>
    /// 观战用：显示和正常对局一致的操作按钮，但全部禁用（点击无效）。
    /// </summary>
    public void ShowSpectatorActionButtons(List<string> actionList) {
        ClearActionButton();
        if (actionList == null || actionList.Count == 0) {
            return;
        }

        SetActionButton(actionList);

        // 观战只展示按钮：移除点击回调（外观保持正常，点击无反应）
        foreach (Transform child in ActionButtonContainer) {
            Button btn = child.GetComponent<Button>();
            if (btn != null) {
                btn.interactable = true;
                btn.onClick.RemoveAllListeners();
            }
        }
    }

    // 渐变消失协程
    private IEnumerator FadeOutActionDisplay(GameObject actionTextObj,Transform displayPos) {
        if (actionTextObj == null) {
            Debug.LogWarning("actionTextObj is null");
            yield break;
        }
        
        TMP_Text actionText = actionTextObj.GetComponent<TMP_Text>();
        if (actionText == null) {
            Debug.LogWarning("actionText is null");
            yield break;
        }
        
        // 等待1秒
        yield return new WaitForSeconds(1f);
        
        // 渐变消失效果
        float fadeTime = 0.5f; // 渐变时间
        float elapsedTime = 0f;
        Color originalColor = actionText.color;
        
        while (elapsedTime < fadeTime) {
            if (actionTextObj == null) yield break;
            
            elapsedTime += Time.deltaTime;
            float alpha = Mathf.Lerp(1f, 0f, elapsedTime / fadeTime);
            actionText.color = new Color(originalColor.r, originalColor.g, originalColor.b, alpha);
            yield return null;
        }
        
        // 销毁对象
        if (actionTextObj != null) {
            Destroy(actionTextObj);
        }
    }
}