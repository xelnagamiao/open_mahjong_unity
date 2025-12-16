// TextPreferredSizeOffset.cs
// 仅适用于 TextMeshPro - Text (TMP)
using UnityEngine;
using TMPro;
using UnityEngine.UI;

[RequireComponent(typeof(TMP_Text))]
[RequireComponent(typeof(LayoutElement))]
public class TextPreferredSizeOffset : MonoBehaviour
{
    [Tooltip("在文本 Preferred Width 基础上增加的像素值（可为负数）")]
    public float horizontalOffset = 0f;

    [Tooltip("在文本 Preferred Height 基础上增加的像素值（可为负数）")]
    public float verticalOffset = 0f;

    private TMP_Text tmpText;
    private LayoutElement layoutElement;

    void Awake()
    {
        tmpText = GetComponent<TMP_Text>();
        layoutElement = GetComponent<LayoutElement>();

        // 确保 LayoutElement 不忽略布局
        layoutElement.ignoreLayout = false;

        UpdateSize();
    }

    void OnEnable()
    {
        // 监听 Canvas 渲染前更新尺寸（确保每次文本变化都生效）
        Canvas.willRenderCanvases += UpdateSize;
    }

    void OnDisable()
    {
        Canvas.willRenderCanvases -= UpdateSize;
    }

    void UpdateSize()
    {
        if (tmpText == null || layoutElement == null) return;

        // 获取 TMP 文本的精确 Preferred 尺寸
        var preferredSize = tmpText.GetPreferredValues();

        // 应用偏移
        layoutElement.preferredWidth = preferredSize.x + horizontalOffset;
        layoutElement.preferredHeight = preferredSize.y + verticalOffset;
    }
}