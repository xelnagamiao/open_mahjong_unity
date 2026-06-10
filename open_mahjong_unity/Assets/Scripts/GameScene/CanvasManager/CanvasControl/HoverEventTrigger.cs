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
    private bool isHoverLifted;
    private bool isArmedLifted;
    private bool isVisuallyLifted;

    private void Awake() {
        imageRect = tileImage.GetComponent<RectTransform>();
        buttonRect = tileButton.GetComponent<RectTransform>();
    }

    private bool IsHoverLiftDisabled() {
        return ConfigManager.Instance != null && ConfigManager.Instance.IsHandCutConfirmEnabled;
    }

    public void OnPointerEnter(PointerEventData eventData) {
        if (HandCardDragController.IsDragging || HandCardDragController.SuppressPointerHover) {
            return;
        }
        SetHoverLift(true);
    }

    public void OnPointerExit(PointerEventData eventData) {
        SetHoverLift(false);
    }

    public void ForceResetHover() {
        SetHoverLift(false);
    }

    public void SetArmedLift(bool armed) {
        if (isArmedLifted == armed) {
            return;
        }
        isArmedLifted = armed;
        RefreshVisualLift();
    }

    private void SetHoverLift(bool hover) {
        if (isHoverLifted == hover) {
            return;
        }
        isHoverLifted = hover;
        RefreshVisualLift();
    }

    private void RefreshVisualLift() {
        bool wantLift = isArmedLifted || (isHoverLifted && !IsHoverLiftDisabled());
        ApplyVisualLift(wantLift);
    }

    private void ApplyVisualLift(bool lift) {
        if (isVisuallyLifted == lift) {
            return;
        }
        Vector3 delta = Vector3.up * hoverOffset * (lift ? 1f : -1f);
        imageRect.localPosition += delta;
        buttonRect.localPosition += delta;
        isVisuallyLifted = lift;
    }
}
