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
        PrepareText(configTitle, 18f);
        PrepareText(configValue, 15f);
        configTitle.text = title;
        configValue.text = value;
    }

    private void PrepareText(TMP_Text text, float minFontSize)
    {
        if (text == null) return;
        text.enableWordWrapping = true;
        text.overflowMode = TextOverflowModes.Ellipsis;
        text.enableAutoSizing = true;
        text.fontSizeMin = minFontSize;
        text.fontSizeMax = Mathf.Max(text.fontSize, minFontSize);

        RectTransform selfRect = transform as RectTransform;
        RectTransform textRect = text.rectTransform;
        if (selfRect == null || textRect == null) return;
        float maxWidth = Mathf.Max(1f, selfRect.rect.width - 12f);
        if (textRect.rect.width > maxWidth) {
            textRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, maxWidth);
        }
    }
}
