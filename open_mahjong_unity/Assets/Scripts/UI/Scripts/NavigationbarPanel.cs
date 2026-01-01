using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

[System.Serializable]
public class NavigationItem
{
    public Button navigationButton;
    public RectTransform contentElement;
}

public class NavigationbarPanel : MonoBehaviour
{
    [Header("滚动视图")]
    [SerializeField] private ScrollRect scrollRect;

    [Header("导航项列表")]
    [SerializeField] private List<NavigationItem> navigationItems = new List<NavigationItem>();

    private void Awake()
    {
        for (int i = 0; i < navigationItems.Count; i++)
        {
            int index = i;
            navigationItems[i].navigationButton.onClick.AddListener(() => ScrollToContent(index));
        }
    }

    private void ScrollToContent(int index)
    {
        if (index >= navigationItems.Count) return;

        RectTransform targetElement = navigationItems[index].contentElement;
        RectTransform content = scrollRect.content;
        RectTransform viewport = scrollRect.viewport;
        
        float contentHeight = content.rect.height;
        float viewportHeight = viewport.rect.height;
        float scrollableHeight = contentHeight - viewportHeight;
        
        if (scrollableHeight <= 0) return;
        
        // 计算元素顶部位置（相对于内容顶部）
        // anchoredPosition.y 通常是负值（向下为正），所以取负号得到顶部位置
        float elementTop = -targetElement.anchoredPosition.y;
        
        // 计算元素高度的一半，用于调整到上边缘
        float elementHalfHeight = targetElement.rect.height * 0.5f;
        
        // 调整位置，使元素上边缘对齐到视口顶部
        // 元素中心位置 - 元素高度的一半 = 元素上边缘位置
        float elementTopEdge = elementTop - elementHalfHeight;
        
        // 转换为归一化位置（0-1之间，1表示顶部，0表示底部）
        float normalizedPosition = elementTopEdge / scrollableHeight;
        
        scrollRect.verticalNormalizedPosition = Mathf.Clamp01(1f - normalizedPosition);
    }
}
