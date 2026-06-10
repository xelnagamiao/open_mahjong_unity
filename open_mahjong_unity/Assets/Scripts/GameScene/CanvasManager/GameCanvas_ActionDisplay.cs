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
        
        string displayText = GetActionDisplayText(actionType, roomRule);
        if (string.IsNullOrEmpty(displayText)) return;

        // 实例化操作显示文本
        GameObject actionTextObj = Instantiate(ActionDisplayText, displayPos);
        
        // 设置文本内容
        TMP_Text actionText = actionTextObj.GetComponent<TMP_Text>();
        actionText.text = displayText;
        
        // 启动渐变消失协程
        StartCoroutine(FadeOutActionDisplay(actionTextObj,displayPos));
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