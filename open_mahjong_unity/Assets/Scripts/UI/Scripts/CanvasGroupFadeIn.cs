using System.Collections;
using UnityEngine;

/// <summary>
/// 淡入动画
/// </summary>

[RequireComponent(typeof(CanvasGroup))]
public class CanvasGroupFadeIn : MonoBehaviour {
    [SerializeField] private float duration = 0.2f;

    private CanvasGroup canvasGroup;
    private Coroutine fadeCoroutine;

    private void Awake() {
        canvasGroup = GetComponent<CanvasGroup>();
    }

    public void PlayFadeIn() {
        if (canvasGroup == null) canvasGroup = GetComponent<CanvasGroup>();
        if (fadeCoroutine != null) StopCoroutine(fadeCoroutine);
        fadeCoroutine = StartCoroutine(FadeInRoutine());
    }

    private IEnumerator FadeInRoutine() {
        float targetAlpha = canvasGroup.alpha <= 0f ? 1f : canvasGroup.alpha;
        canvasGroup.alpha = 0f;

        if (duration <= 0f) {
            canvasGroup.alpha = targetAlpha;
            fadeCoroutine = null;
            yield break;
        }

        float elapsed = 0f;
        while (elapsed < duration) {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            canvasGroup.alpha = Mathf.Lerp(0f, targetAlpha, t);
            yield return null;
        }
        canvasGroup.alpha = targetAlpha;
        fadeCoroutine = null;
    }
}
