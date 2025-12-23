using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

public class TooltipTrigger : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    // 要显示的提示文本
    public string tooltipText = "这是提示内容";
    
    // 悬浮窗预制体 (你需要提前创建好一个带有Text的Panel预制体)
    public GameObject tooltipPrefab;
    
    // 偏移量，让悬浮窗显示在鼠标右下方，不遮挡视线
    public Vector2 offset = new Vector2(20, -20);

    private GameObject currentTooltip; // 当前显示的悬浮窗实例

    // 当鼠标进入
    public void OnPointerEnter(PointerEventData eventData)
    {
        if (tooltipPrefab != null)
        {
            // 实例化悬浮窗
            currentTooltip = Instantiate(tooltipPrefab, transform.parent); // 保持在同一层级下
            currentTooltip.transform.SetAsLastSibling(); // 置于顶层
            
            // 获取悬浮窗上的Text组件并赋值
            Text textComponent = currentTooltip.GetComponentInChildren<Text>();
            if (textComponent != null && !string.IsNullOrEmpty(tooltipText))
            {
                textComponent.text = tooltipText;
            }

            // 设置位置
            UpdateTooltipPosition(eventData.position);
        }
    }

    void Update()
    {
        if (currentTooltip != null && Input.mousePresent)
        {
            UpdateTooltipPosition(Input.mousePosition);
        }
    }

    private void UpdateTooltipPosition(Vector2 mousePosition)
    {
        // 转换坐标并加上偏移
        RectTransform tooltipRect = currentTooltip.GetComponent<RectTransform>();
        tooltipRect.position = mousePosition + offset;
    }

    // 当鼠标退出
    public void OnPointerExit(PointerEventData eventData)
    {
        if (currentTooltip != null)
        {
            Destroy(currentTooltip); // 销毁悬浮窗
            currentTooltip = null;
        }
    }
}