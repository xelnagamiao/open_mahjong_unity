using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public static class WindowFadeTransition {
    /// <summary>单面板淡入：与主窗口切换相同的 Prepare + Fade 流程。</summary>
    public static IEnumerator FadeOverlayIn(GameObject panel, float durationSeconds) {
        var fadeIn = new List<(GameObject go, CanvasGroup cg)> { (panel, EnsureCanvasGroup(panel)) };
        PrepareFadeIn(fadeIn);
        var fadeOut = new List<(GameObject go, CanvasGroup cg)>();
        yield return Fade(fadeOut, fadeIn, durationSeconds);
    }

    /// <summary>单面板淡出：结束后 SetActive(false) 并复位根 CanvasGroup.alpha。</summary>
    public static IEnumerator FadeOverlayOut(GameObject panel, float durationSeconds) {
        var fadeOut = new List<(GameObject go, CanvasGroup cg)> { (panel, EnsureCanvasGroup(panel)) };
        PrepareFadeOut(fadeOut);
        var fadeIn = new List<(GameObject go, CanvasGroup cg)>();
        yield return Fade(fadeOut, fadeIn, durationSeconds);
    }

    private static CanvasGroup EnsureCanvasGroup(GameObject go) {
        CanvasGroup cg = go.GetComponent<CanvasGroup>();
        if (cg == null) cg = go.AddComponent<CanvasGroup>();
        return cg;
    }
    public static void PrepareFadeIn(List<(GameObject go, CanvasGroup cg)> fadeIn) {
        for (int i = 0; i < fadeIn.Count; i++) {
            (GameObject go, CanvasGroup cg) = fadeIn[i];
            go.SetActive(true); // 先激活再改 alpha，避免没渲染
            UnifyChildCanvasGroupAlphas(go, cg); // 子级 CanvasGroup alpha 统一为 1
            cg.alpha = 0f; // 从透明开始
            cg.interactable = true; // 保持 Button 等按 Normal 状态渲染
            cg.blocksRaycasts = false; // 过渡中禁止点击
        }
    }

    public static void PrepareFadeOut(List<(GameObject go, CanvasGroup cg)> fadeOut) {
        for (int i = 0; i < fadeOut.Count; i++) {
            (GameObject go, CanvasGroup cg) = fadeOut[i];
            UnifyChildCanvasGroupAlphas(go, cg); // 避免子级 alpha 叠乘导致不同步
            cg.alpha = 1f; // 从不透明开始
            cg.interactable = true; // 保持 Button 等按 Normal 状态渲染
            cg.blocksRaycasts = false; // 过渡中禁止点击
        }
    }

    public static IEnumerator Fade(List<(GameObject go, CanvasGroup cg)> fadeOut, List<(GameObject go, CanvasGroup cg)> fadeIn, float duration) {
        if (duration <= 0f) {
            FinishInstant(fadeOut, fadeIn); // 不做动画，直接切换
            yield break;
        }
        for (float t = 0f; t < duration; t += Time.unscaledDeltaTime) {
            float k = Mathf.Clamp01(t / duration); // 归一化时间
            float s = k * k * (3f - 2f * k); // smoothstep
            for (int i = 0; i < fadeOut.Count; i++)
                fadeOut[i].cg.alpha = 1f - s; // 渐隐
            for (int i = 0; i < fadeIn.Count; i++)
                fadeIn[i].cg.alpha = s; // 渐显
            yield return null;
        }
        FinishAfterFade(fadeOut, fadeIn); // 收尾并恢复可点击
    }

    private static void FinishInstant(List<(GameObject go, CanvasGroup cg)> fadeOut, List<(GameObject go, CanvasGroup cg)> fadeIn) {
        for (int i = 0; i < fadeOut.Count; i++) {
            (GameObject go, CanvasGroup cg) = fadeOut[i];
            cg.alpha = 0f; // 置为透明
            go.SetActive(false); // 关闭面板
            cg.alpha = 1f; // 复位，便于下次再次显示
            cg.interactable = true; // 恢复交互
            cg.blocksRaycasts = true; // 恢复射线
        }
        for (int i = 0; i < fadeIn.Count; i++) {
            (GameObject go, CanvasGroup cg) = fadeIn[i];
            cg.alpha = 1f; // 置为不透明
            cg.interactable = true; // 恢复交互
            cg.blocksRaycasts = true; // 恢复射线
        }
    }

    private static void FinishAfterFade(List<(GameObject go, CanvasGroup cg)> fadeOut, List<(GameObject go, CanvasGroup cg)> fadeIn) {
        for (int i = 0; i < fadeOut.Count; i++) {
            (GameObject go, CanvasGroup cg) = fadeOut[i];
            cg.alpha = 0f; // 最终透明
            go.SetActive(false); // 关闭面板
            cg.alpha = 1f; // 复位，便于下次再次显示
            cg.interactable = true; // 恢复交互
            cg.blocksRaycasts = true; // 恢复射线
        }
        for (int i = 0; i < fadeIn.Count; i++) {
            (GameObject go, CanvasGroup cg) = fadeIn[i];
            cg.alpha = 1f; // 最终不透明
            cg.interactable = true; // 恢复交互
            cg.blocksRaycasts = true; // 恢复射线
        }
    }

    private static void UnifyChildCanvasGroupAlphas(GameObject rootGo, CanvasGroup rootCg) {
        CanvasGroup[] groups = rootGo.GetComponentsInChildren<CanvasGroup>(true); // 包含未激活
        foreach (CanvasGroup cg in groups) {
            if (cg == rootCg) continue;
            cg.alpha = 1f; // 子级 alpha 统一为 1
        }
    }
}
