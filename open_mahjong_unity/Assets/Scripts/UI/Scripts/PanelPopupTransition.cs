using System;
using System.Collections;
using UnityEngine;

/// <summary>
/// 挂载到需要弹出/收起动画的面板上。
/// Show: 从 1.15 倍缩放 + 全透明 → 100% 缩放 + 不透明。
/// Hide: 从 100% 缩放 + 不透明 → 1.05 倍缩放 + 全透明 → SetActive(false)。
/// </summary>
[RequireComponent(typeof(CanvasGroup))]
public class PanelPopupTransition : MonoBehaviour {
    [SerializeField] private float showDuration = 0.2f;
    [SerializeField] private float hideDuration = 0.15f;
    [SerializeField] private bool animateScale = true;
    public bool AnimateScale { get => animateScale; set => animateScale = value; }
    private const float OvershootScale = 1.20f;
    private const float HideEndScale = 1.15f;

    private CanvasGroup _cg;
    private RectTransform _rt;
    private Coroutine _routine;
    private bool _shown;
    private int _transitionVersion;
    public bool IsClosing { get; private set; }
    public bool IsOpen => gameObject.activeInHierarchy && !IsClosing;

    private void Awake() {
        _cg = GetComponent<CanvasGroup>();
        _rt = GetComponent<RectTransform>();
        // Overlay 上的密码框场景默认激活；登录页不会走 RoomListPanel.Start。
        if (!_shown && gameObject.name.StartsWith("PasswordInputPanel")) {
            gameObject.SetActive(false);
        }
    }

    private void EnsureComponents() {
        if (_cg == null) _cg = GetComponent<CanvasGroup>();
        if (_rt == null) _rt = GetComponent<RectTransform>();
    }

    /// <summary>
    /// 弹出显示面板。可选 onComplete 回调。
    /// </summary>
    public void Show(Action onComplete = null) {
        EnsureComponents();
        bool reversing = IsClosing && gameObject.activeInHierarchy;
        float fromAlpha = reversing ? _cg.alpha : 0f;
        Vector3 fromScale = animateScale ? (reversing ? _rt.localScale : Vector3.one * OvershootScale) : Vector3.one;
        ++_transitionVersion;
        IsClosing = false;
        _shown = true;
        gameObject.SetActive(true);
        _cg.alpha = fromAlpha;
        _cg.blocksRaycasts = true;
        _rt.localScale = fromScale;
        if (_routine != null) StopCoroutine(_routine);
        _routine = StartCoroutine(RunShow(onComplete, fromAlpha, fromScale));
    }

    /// <summary>
    /// 收起隐藏面板。可选 onComplete 回调。
    /// </summary>
    public void Hide(Action onComplete = null) {
        if (!gameObject.activeInHierarchy) {
            HideImmediate(); onComplete?.Invoke(); return;
        }
        if (IsClosing) return;
        EnsureComponents();
        IsClosing = true;
        _cg.blocksRaycasts = false;
        int version = ++_transitionVersion;
        if (_routine != null) StopCoroutine(_routine);
        _routine = StartCoroutine(RunHide(onComplete, version, _cg.alpha, animateScale ? _rt.localScale : Vector3.one));
    }

    /// <summary>
    /// 立即隐藏面板（跳过渐出动画）。用于匹配成功等需要立刻让出界面的场景。
    /// </summary>
    public void HideImmediate() {
        ++_transitionVersion; IsClosing = false;
        if (_routine != null) {
            StopCoroutine(_routine);
            _routine = null;
        }
        EnsureComponents();
        _cg.alpha = 1f; _cg.blocksRaycasts = true;
        _rt.localScale = Vector3.one;
        gameObject.SetActive(false);
    }

    private IEnumerator RunShow(Action onComplete, float fromAlpha, Vector3 fromScale) {
        if (showDuration <= 0f) {
            _cg.alpha = 1f;
            _rt.localScale = Vector3.one;
        } else {
            for (float t = 0f; t < showDuration; t += Time.unscaledDeltaTime) {
                float k = Mathf.Clamp01(t / showDuration);
                float s = k * k * (3f - 2f * k);
                _cg.alpha = Mathf.Lerp(fromAlpha, 1f, s);
                _rt.localScale = Vector3.Lerp(fromScale, Vector3.one, s);
                yield return null;
            }
            _cg.alpha = 1f;
            _rt.localScale = Vector3.one;
        }
        _routine = null;
        onComplete?.Invoke();
    }

    private IEnumerator RunHide(Action onComplete, int version, float fromAlpha, Vector3 fromScale) {
        Vector3 endScale = animateScale ? Vector3.one * HideEndScale : Vector3.one;
        if (hideDuration <= 0f) {
            _cg.alpha = 0f;
            _rt.localScale = endScale;
        } else {
            for (float t = 0f; t < hideDuration; t += Time.unscaledDeltaTime) {
                float k = Mathf.Clamp01(t / hideDuration);
                float s = k * k * (3f - 2f * k);
                _cg.alpha = Mathf.Lerp(fromAlpha, 0f, s);
                _rt.localScale = Vector3.Lerp(fromScale, endScale, s);
                yield return null;
            }
            _cg.alpha = 0f;
            _rt.localScale = endScale;
        }

        _routine = null;
        onComplete?.Invoke();
        yield return null;
        if (this == null || version != _transitionVersion) {
            yield break;
        }
        IsClosing = false;
        gameObject.SetActive(false);
        _cg.alpha = 1f; _cg.blocksRaycasts = true;
        _rt.localScale = Vector3.one;
    }
}
