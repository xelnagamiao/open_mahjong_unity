using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TMPro;
using UnityEngine.EventSystems;

public class TipsWindows : MonoBehaviour
{
    [Header("UI 组件")]
    [SerializeField] private TextMeshProUGUI messageText;

    /// <summary>
    /// 显示提示消息
    /// </summary>
    /// <param name="type">消息类型</param>
    /// <param name="success">是否成功（true=绿色，false=红色）</param>
    /// <param name="message">消息内容</param>
    public void ShowMessage(string type, bool success, string message)
    {
        if (messageText == null)
        {
            Debug.LogError("TipsWindows: messageText 未设置！请在 Inspector 中拖拽 TextMeshProUGUI 组件。");
            return;
        }

        messageText.text = message;

        if (success){
            messageText.color = Color.green; // 成功显示绿色
        }
        else{
            messageText.color = Color.red; // 失败显示红色
        }
    }
}
