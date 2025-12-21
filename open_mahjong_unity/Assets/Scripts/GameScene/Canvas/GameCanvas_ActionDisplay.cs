// GameCanvas 操作文本

using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public partial class GameCanvas{
    // 显示操作文本
    public void ShowActionDisplay(string playerPosition, string actionType)
    {
        Transform displayPos = null;
        if (playerPosition == "self"){
            displayPos = SelfActionDisplayPos;
        }
        else if (playerPosition == "left"){
            displayPos = LeftActionDisplayPos;
        }
        else if (playerPosition == "right"){
            displayPos = RightActionDisplayPos;
        }
        else if (playerPosition == "top"){
            displayPos = TopActionDisplayPos;
        }
        
        // 实例化操作显示文本
        GameObject actionTextObj = Instantiate(ActionDisplayText, displayPos);
        
        // 设置文本内容
        TMP_Text actionText = actionTextObj.GetComponent<TMP_Text>();
        if (actionType == "chi_left" || actionType == "chi_mid" || actionType == "chi_right"){
            actionText.text = "吃";
        }
        else if (actionType == "peng"){
            actionText.text = "碰";
        }
        else if (actionType == "angang" || actionType == "jiagang" || actionType == "gang"){
            actionText.text = "杠";
        }
        else if (actionType == "hu_self" || actionType == "hu_first" || actionType == "hu_second" || actionType == "hu_third"){
            actionText.text = "胡";
        }
        else if (actionType == "buhua"){
            actionText.text = "补花";
        }
        
        // 启动渐变消失协程
        StartCoroutine(FadeOutActionDisplay(actionTextObj,displayPos));
    }

    // 渐变消失协程
    private IEnumerator FadeOutActionDisplay(GameObject actionTextObj,Transform displayPos)
    {
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
        
        while (elapsedTime < fadeTime)
        {
            if (actionTextObj == null) yield break;
            
            elapsedTime += Time.deltaTime;
            float alpha = Mathf.Lerp(1f, 0f, elapsedTime / fadeTime);
            actionText.color = new Color(originalColor.r, originalColor.g, originalColor.b, alpha);
            yield return null;
        }
        
        // 销毁对象
        if (actionTextObj != null)
        {
            Destroy(actionTextObj);
        }
    }
}