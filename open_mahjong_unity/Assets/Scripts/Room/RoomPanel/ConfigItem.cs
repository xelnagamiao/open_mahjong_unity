using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TMPro;

public class ConfigItem : MonoBehaviour
{
    [SerializeField] private TMP_Text configTitle; // 配置名
    [SerializeField] private TMP_Text configValue; // 配置值

    /// <summary>
    /// 设置配置项的标题和值
    /// </summary>
    /// <param name="title">配置项标题</param>
    /// <param name="value">配置项值</param>
    public void SetConfig(string title, string value)
    {
        configTitle.text = title;
        configValue.text = value;
    }
}
