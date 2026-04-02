using System;
using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;
using TMPro;

/// <summary>
/// 单条提示的显示、动画与生命周期：从左侧缩放弹入 → 空闲倒计时 → 超时向上滑出渐隐 / 点击渐隐消失。
/// 悬停时暂停倒计时并微放大，离开恢复。销毁时通知 TipStackController 补位。
/// </summary>
[RequireComponent(typeof(CanvasGroup))]
public class TipItem : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerClickHandler {
    [Header("UI 组件")]
    [SerializeField] private TextMeshProUGUI messageText;

    [Header("弹入")]
    [SerializeField] private float popInDuration = 0.15f;
    [SerializeField] private float popInStartScale = 0.3f;

    [Header("超时滑出")]
    [SerializeField] private float slideOutDuration = 0.3f;
    [SerializeField] private float slideOutOffsetY = 40f; // 向上滑出距离

    [Header("点击消失")]
    [SerializeField] private float clickDismissDuration = 0.2f;

    [Header("悬停反馈")]
    [SerializeField] private float hoverScale = 1.03f;
    [SerializeField] private float hoverScaleDuration = 0.1f;

    private RectTransform _rt;
    private CanvasGroup _cg;
    private float _lifetime;
    private float _elapsed;
    private bool _hovered;
    private bool _dismissing;
    private Coroutine _hoverTween;

    public event Action<TipItem> OnDismissed; // TipStackController 订阅

    /// <summary>设置提示内容并显示。</summary>
    public void ShowMessage(string type, bool success, string message) {
        messageText.text = message;
    }

    public void Init(float lifetime) {
        _rt = GetComponent<RectTransform>();
        _cg = GetComponent<CanvasGroup>();
        _lifetime = lifetime;
        _elapsed = 0f;
        _dismissing = false;
        StartCoroutine(LifecycleRoutine());
    }

    private IEnumerator LifecycleRoutine() {
        yield return PopInRoutine();
        while (_elapsed < _lifetime) {
            if (!_hovered) _elapsed += Time.unscaledDeltaTime;
            yield return null;
        }
        yield return SlideOutRoutine();
        NotifyAndDestroy();
    }

    private IEnumerator PopInRoutine() {
        _rt.localScale = Vector3.one * popInStartScale;
        _cg.alpha = 0f;

        for (float t = 0f; t < popInDuration; t += Time.unscaledDeltaTime) {
            float s = Smooth01(t / popInDuration);
            _rt.localScale = Vector3.one * Mathf.Lerp(popInStartScale, 1f, s);
            _cg.alpha = s;
            yield return null;
        }
        _rt.localScale = Vector3.one;
        _cg.alpha = 1f;
    }

    private IEnumerator SlideOutRoutine() {
        _dismissing = true;
        _cg.blocksRaycasts = false;
        Vector2 startPos = _rt.anchoredPosition;
        Vector2 endPos = startPos + new Vector2(0f, slideOutOffsetY);

        for (float t = 0f; t < slideOutDuration; t += Time.unscaledDeltaTime) {
            float s = Smooth01(t / slideOutDuration);
            _rt.anchoredPosition = Vector2.Lerp(startPos, endPos, s);
            _cg.alpha = 1f - s;
            yield return null;
        }
        _cg.alpha = 0f;
    }

    private IEnumerator ClickDismissRoutine() {
        _dismissing = true;
        _cg.blocksRaycasts = false;
        float startAlpha = _cg.alpha;

        for (float t = 0f; t < clickDismissDuration; t += Time.unscaledDeltaTime) {
            float s = Smooth01(t / clickDismissDuration);
            _cg.alpha = Mathf.Lerp(startAlpha, 0f, s);
            _rt.localScale = Vector3.one * Mathf.Lerp(1f, 0.85f, s);
            yield return null;
        }
        _cg.alpha = 0f;
        NotifyAndDestroy();
    }

    /// <summary>平滑移动到目标 anchoredPosition（由 TipStackController 调用补位）。</summary>
    public Coroutine AnimateToPosition(Vector2 target, float duration) {
        return StartCoroutine(MoveRoutine(target, duration));
    }

    private IEnumerator MoveRoutine(Vector2 target, float duration) {
        Vector2 start = _rt.anchoredPosition;
        for (float t = 0f; t < duration; t += Time.unscaledDeltaTime) {
            _rt.anchoredPosition = Vector2.Lerp(start, target, Smooth01(t / duration));
            yield return null;
        }
        _rt.anchoredPosition = target;
    }

    // -- 指针事件 --

    public void OnPointerEnter(PointerEventData eventData) {
        if (_dismissing) return;
        _hovered = true;
        if (_hoverTween != null) StopCoroutine(_hoverTween);
        _hoverTween = StartCoroutine(ScaleTo(hoverScale, hoverScaleDuration));
    }

    public void OnPointerExit(PointerEventData eventData) {
        if (_dismissing) return;
        _hovered = false;
        if (_hoverTween != null) StopCoroutine(_hoverTween);
        _hoverTween = StartCoroutine(ScaleTo(1f, hoverScaleDuration));
    }

    public void OnPointerClick(PointerEventData eventData) {
        if (_dismissing) return;
        StopAllCoroutines();
        StartCoroutine(ClickDismissRoutine());
    }

    private IEnumerator ScaleTo(float target, float dur) {
        float start = _rt.localScale.x;
        for (float t = 0f; t < dur; t += Time.unscaledDeltaTime) {
            float v = Mathf.Lerp(start, target, Smooth01(t / dur));
            _rt.localScale = Vector3.one * v;
            yield return null;
        }
        _rt.localScale = Vector3.one * target;
        _hoverTween = null;
    }

    private void NotifyAndDestroy() {
        OnDismissed?.Invoke(this);
        Destroy(gameObject);
    }

    private static float Smooth01(float t) {
        t = Mathf.Clamp01(t);
        return t * t * (3f - 2f * t);
    }
}
