using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;

public class TooltipTrigger : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    // 要显示的提示文本
    public string tooltipText = "这是提示内容";

    // 悬浮窗使用项目的 TMP 文本预制体。
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
            CloseTooltip();
            var parent=NotificationManager.Instance ? NotificationManager.Instance.TooltipRoot : GetComponentInParent<Canvas>()?.rootCanvas.transform as RectTransform;
            if(!parent)return;
            currentTooltip = Instantiate(tooltipPrefab, parent);
            currentTooltip.transform.SetAsLastSibling(); // 置于顶层
            foreach(var graphic in currentTooltip.GetComponentsInChildren<Graphic>(true))graphic.raycastTarget=false;

            // 获取悬浮窗上的Text组件并赋值
            TMP_Text textComponent = currentTooltip.GetComponentInChildren<TMP_Text>();
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
        RectTransform tooltipRect = currentTooltip.GetComponent<RectTransform>();
        var parent=tooltipRect.parent as RectTransform;
        var canvas=parent.GetComponentInParent<Canvas>().rootCanvas;
        Camera camera=canvas.renderMode==RenderMode.ScreenSpaceOverlay?null:canvas.worldCamera;
        if(!RectTransformUtility.ScreenPointToLocalPointInRectangle(parent,mousePosition,camera,out var local))return;
        tooltipRect.anchorMin=tooltipRect.anchorMax=Vector2.one*.5f;
        tooltipRect.pivot=new Vector2(0,1);
        var position=local+offset/Mathf.Max(.001f,CanvasReferenceLayout.PixelsPerUnit(parent));
        position.x=Mathf.Clamp(position.x,parent.rect.xMin,Mathf.Max(parent.rect.xMin,parent.rect.xMax-tooltipRect.rect.width));
        position.y=Mathf.Clamp(position.y,Mathf.Min(parent.rect.yMax,parent.rect.yMin+tooltipRect.rect.height),parent.rect.yMax);
        tooltipRect.anchoredPosition=position;
    }

    // 当鼠标退出
    public void OnPointerExit(PointerEventData eventData)
    {
        CloseTooltip();
    }
    void OnDisable() { CloseTooltip(); }
    private void CloseTooltip()
    {
        if (currentTooltip != null)
        {
            Destroy(currentTooltip); // 销毁悬浮窗
            currentTooltip = null;
        }
    }
}
