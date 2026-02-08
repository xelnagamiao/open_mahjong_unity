using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

public class HoverEventTrigger : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler {
    private float hoverOffset = 50f;  // 上浮距离

    [Header("UI Components")]
    [SerializeField] private Image tileImage;    // 牌面图片组件
    [SerializeField] private Button tileButton;  // 按钮组件

    private RectTransform imageRect;
    private RectTransform buttonRect;

    private void Awake() {
        imageRect = tileImage.GetComponent<RectTransform>();
        buttonRect = tileButton.GetComponent<RectTransform>();
    }

    public void OnPointerEnter(PointerEventData eventData) {
        // Image和Button一起上浮
        imageRect.localPosition += Vector3.up * hoverOffset;
        buttonRect.localPosition += Vector3.up * hoverOffset;
    }

    public void OnPointerExit(PointerEventData eventData) {
        // Image和Button一起下降
        imageRect.localPosition -= Vector3.up * hoverOffset;
        buttonRect.localPosition -= Vector3.up * hoverOffset;
    }
} 