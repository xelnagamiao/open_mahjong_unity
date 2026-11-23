using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

public class HoverEventTrigger : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler {
    private float hoverOffset = 50f;  // 上浮距离

    [Header("UI Components")]
    [SerializeField] private Image tileImage;    // 牌面图片组件
    [SerializeField] private Image faceBackground; // 手牌背景，悬停时与花纹一起上浮
    [SerializeField] private Button tileButton;  // 按钮组件

    private RectTransform imageRect;
    private RectTransform buttonRect;
    private RectTransform backgroundRect;
    private bool isHoverLifted;
    private bool isArmedLifted;
    public bool SbrRaised { get; private set; }
    private bool sbrPresentation;
    private float sbrLiftFraction;
    private float visualLift;

    private void Awake() {
        imageRect = tileImage.GetComponent<RectTransform>();
        buttonRect = tileButton.GetComponent<RectTransform>();
        if (faceBackground != null) {
            backgroundRect = faceBackground.rectTransform;
        }
    }

    private bool IsHoverLiftDisabled() {
        return GameSettings.Current.IsHandCutConfirmEnabled;
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

    public void SetSbrLift(bool presenting, bool raised) {
        sbrPresentation = presenting;
        SbrRaised = raised;
        sbrLiftFraction = raised ? 1f : 0f;
        RefreshVisualLift();
    }

    internal void SetReceiptLift(float fraction) {
        sbrLiftFraction = Mathf.Clamp01(fraction);
        sbrPresentation = SbrRaised = sbrLiftFraction > 0f;
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
        float target = sbrPresentation ? imageRect.rect.height * .46f * sbrLiftFraction
                                       : (wantLift ? hoverOffset : 0f);
        if (Mathf.Approximately(target, visualLift)) return;
        Vector3 delta = Vector3.up * (target - visualLift);
        imageRect.localPosition += delta;
        buttonRect.localPosition += delta;
        if (backgroundRect != null) {
            backgroundRect.localPosition += delta;
        }
        GetComponent<WashizuHandVisual>()?.SyncLayout();
        visualLift = target;
    }
}
