using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>面板弹出：从 1.2 倍缩放 + 不透明 1，在指定时长内恢复到进入动画前的 localScale 与 CanvasGroup.alpha。</summary>
public static class UIPanelPopInReveal {
    private const float DefaultScaleOvershoot = 1.2f;

    private static readonly Dictionary<int, (Vector3 scale, float alpha)> s_restByPanelId = new Dictionary<int, (Vector3, float)>();

    public static Coroutine PlayShow(MonoBehaviour runner, GameObject panel, float durationSeconds = 1f, float startScaleMultiplier = DefaultScaleOvershoot) {
        if (panel == null || runner == null) return null;
        return runner.StartCoroutine(ShowRoutine(panel, durationSeconds, startScaleMultiplier));
    }

    public static void StopAndHide(MonoBehaviour runner, GameObject panel, ref Coroutine runningCoroutine) {
        if (runningCoroutine != null) {
            runner.StopCoroutine(runningCoroutine);
            runningCoroutine = null;
        }
        if (panel == null) return;
        int id = panel.GetInstanceID();
        RectTransform rt = panel.GetComponent<RectTransform>();
        CanvasGroup cg = panel.GetComponent<CanvasGroup>();
        if (s_restByPanelId.TryGetValue(id, out (Vector3 scale, float alpha) rest)) {
            if (rt != null) rt.localScale = rest.scale;
            if (cg != null) cg.alpha = rest.alpha;
        }
        panel.SetActive(false);
    }

    private static IEnumerator ShowRoutine(GameObject panel, float duration, float startScaleMultiplier) {
        panel.SetActive(true);
        RectTransform rt = panel.GetComponent<RectTransform>();
        if (rt == null) yield break;

        CanvasGroup cg = panel.GetComponent<CanvasGroup>();
        if (cg == null) cg = panel.AddComponent<CanvasGroup>();

        Vector3 endScale = rt.localScale;
        float endAlpha = cg.alpha;
        int id = panel.GetInstanceID();
        s_restByPanelId[id] = (endScale, endAlpha);

        Vector3 startScale = endScale * startScaleMultiplier;
        rt.localScale = startScale;
        cg.alpha = 1f;

        if (duration <= 0f) {
            rt.localScale = endScale;
            cg.alpha = endAlpha;
            yield break;
        }

        float elapsed = 0f;
        const float startAlpha = 1f;
        while (elapsed < duration) {
            elapsed += Time.unscaledDeltaTime;
            float k = Mathf.Clamp01(elapsed / duration);
            float s = k * k * (3f - 2f * k);
            rt.localScale = Vector3.Lerp(startScale, endScale, s);
            cg.alpha = Mathf.Lerp(startAlpha, endAlpha, s);
            yield return null;
        }
        rt.localScale = endScale;
        cg.alpha = endAlpha;
    }
}
