using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// 通用按压透明度控制：
/// - 左键按下时应用 holdAlpha
/// - 左键松开时恢复 normalAlpha
/// 可复用于任意 UI 面板。
/// </summary>
[RequireComponent(typeof(CanvasGroup))]
public class PressHoldAlphaController : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerExitHandler {
    [SerializeField, Range(0f, 1f)] private float normalAlpha = 1f;
    [SerializeField, Range(0f, 1f)] private float holdAlpha = 0f;

    private CanvasGroup panelCanvasGroup;

    private void Awake() {
        panelCanvasGroup = GetComponent<CanvasGroup>();
        panelCanvasGroup.alpha = normalAlpha;
    }

    public void OnPointerDown(PointerEventData eventData) {
        if (eventData.button != PointerEventData.InputButton.Left) return;
        panelCanvasGroup.alpha = holdAlpha;
    }

    public void OnPointerUp(PointerEventData eventData) {
        if (eventData.button != PointerEventData.InputButton.Left) return;
        panelCanvasGroup.alpha = normalAlpha;
    }

    public void OnPointerExit(PointerEventData eventData) {
        // 防止按下后移出区域导致不恢复
        panelCanvasGroup.alpha = normalAlpha;
    }

    private void OnDisable() {
        if (panelCanvasGroup != null) {
            panelCanvasGroup.alpha = normalAlpha;
        }
    }

    private void OnValidate() {
        normalAlpha = Mathf.Clamp01(normalAlpha);
        holdAlpha = Mathf.Clamp01(holdAlpha);
        if (panelCanvasGroup == null) {
            panelCanvasGroup = GetComponent<CanvasGroup>();
        }
        if (panelCanvasGroup != null && !Application.isPlaying) {
            panelCanvasGroup.alpha = normalAlpha;
        }
    }
}
