using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TMPro;
using UnityEngine.EventSystems;

public class TipsPrefab : MonoBehaviour
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
        messageText.text = message;
    }
}
