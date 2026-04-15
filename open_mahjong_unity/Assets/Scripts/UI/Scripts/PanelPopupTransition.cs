using System;
using System.Collections;
using UnityEngine;

/// <summary>
/// 挂载到需要弹出/收起动画的面板上。
/// Show: 从 120% 缩放 + 全透明 → 100% 缩放 + 不透明。
/// Hide: 从 100% 缩放 + 不透明 → 120% 缩放 + 全透明 → SetActive(false)。
/// </summary>
[RequireComponent(typeof(CanvasGroup))]
public class PanelPopupTransition : MonoBehaviour {
    [SerializeField] private float showDuration = 0.2f;
    [SerializeField] private float hideDuration = 0.15f;
    [SerializeField] private float overshootScale = 1.2f;

    private CanvasGroup _cg;
    private RectTransform _rt;
    private Coroutine _routine;

    private void Awake() {
        _cg = GetComponent<CanvasGroup>();
        _rt = GetComponent<RectTransform>();
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
        gameObject.SetActive(true);
        _cg.alpha = 0f;
        _rt.localScale = Vector3.one * overshootScale;
        if (_routine != null) StopCoroutine(_routine);
        _routine = StartCoroutine(RunShow(onComplete));
    }

    /// <summary>
    /// 收起隐藏面板。可选 onComplete 回调。
    /// </summary>
    public void Hide(Action onComplete = null) {
        if (!gameObject.activeSelf) {
            onComplete?.Invoke();
            return;
        }
        EnsureComponents();
        if (_routine != null) StopCoroutine(_routine);
        _routine = StartCoroutine(RunHide(onComplete));
    }

    private IEnumerator RunShow(Action onComplete) {
        if (showDuration <= 0f) {
            _cg.alpha = 1f;
            _rt.localScale = Vector3.one;
        } else {
            for (float t = 0f; t < showDuration; t += Time.unscaledDeltaTime) {
                float k = Mathf.Clamp01(t / showDuration);
                float s = k * k * (3f - 2f * k);
                _cg.alpha = s;
                _rt.localScale = Vector3.Lerp(Vector3.one * overshootScale, Vector3.one, s);
                yield return null;
            }
            _cg.alpha = 1f;
            _rt.localScale = Vector3.one;
        }
        _routine = null;
        onComplete?.Invoke();
    }

    private IEnumerator RunHide(Action onComplete) {
        if (hideDuration <= 0f) {
            _cg.alpha = 0f;
            _rt.localScale = Vector3.one * overshootScale;
        } else {
            for (float t = 0f; t < hideDuration; t += Time.unscaledDeltaTime) {
                float k = Mathf.Clamp01(t / hideDuration);
                float s = k * k * (3f - 2f * k);
                _cg.alpha = 1f - s;
                _rt.localScale = Vector3.Lerp(Vector3.one, Vector3.one * overshootScale, s);
                yield return null;
            }
            _cg.alpha = 0f;
            _rt.localScale = Vector3.one * overshootScale;
        }

        gameObject.SetActive(false);
        _cg.alpha = 1f;
        _rt.localScale = Vector3.one;
        _routine = null;
        onComplete?.Invoke();
    }
}
