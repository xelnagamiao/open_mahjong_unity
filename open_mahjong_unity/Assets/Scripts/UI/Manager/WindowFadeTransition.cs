using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public static class WindowFadeTransition {
    public static float DurationSeconds {
        get {
            WindowsManager wm = WindowsManager.Instance;
            return wm != null ? wm.WindowFadeDuration : 0.2f;
        }
    }

    public static void Normalize(GameObject go) {
        if (go == null) return;
        CanvasGroup cg = go.GetComponent<CanvasGroup>();
        if (cg == null) return;
        cg.alpha = 1f;
        cg.interactable = true;
        cg.blocksRaycasts = true;
    }

    /// <summary>
    /// 打断淡入淡出后立刻收到目标态：隐藏节点关掉并复位 CanvasGroup，显示节点打开并复位。
    /// StopCoroutine 不会走到 FinishAfterFade，必须由调用方 Snap。
    /// </summary>
    public static void Snap(GameObject hide, GameObject show) {
        Snap(
            hide != null ? new[] { hide } : null,
            show != null ? new[] { show } : null);
    }

    public static void Snap(GameObject hide, GameObject[] show) {
        Snap(hide != null ? new[] { hide } : null, show);
    }

    public static void Snap(GameObject[] hide, GameObject show) {
        Snap(hide, show != null ? new[] { show } : null);
    }

    public static void Snap(GameObject[] hide, GameObject[] show) {
        var hideSet = new HashSet<GameObject>();
        if (hide != null) {
            for (int i = 0; i < hide.Length; i++) {
                GameObject go = hide[i];
                if (go == null || !hideSet.Add(go)) continue;
                go.SetActive(false);
                Normalize(go);
            }
        }
        if (show != null) {
            for (int i = 0; i < show.Length; i++) {
                GameObject go = show[i];
                if (go == null || hideSet.Contains(go)) continue;
                go.SetActive(true);
                Normalize(go);
            }
        }
    }

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

    /// <summary>
    /// 与 Header 一级窗口相同：旧面板渐隐、新面板渐显同时进行。不改场景层级顺序。
    /// </summary>
    public static IEnumerator CrossFade(GameObject hide, GameObject show, float durationSeconds, System.Action afterPrepare = null) {
        yield return CrossFade(
            hide != null ? new[] { hide } : null,
            show != null ? new[] { show } : null,
            durationSeconds,
            afterPrepare);
    }

    public static IEnumerator CrossFade(GameObject[] hide, GameObject[] show, float durationSeconds, System.Action afterPrepare = null) {
        var fadeOut = new List<(GameObject go, CanvasGroup cg)>();
        var fadeIn = new List<(GameObject go, CanvasGroup cg)>();
        var hideSet = new HashSet<GameObject>();
        if (hide != null) {
            for (int i = 0; i < hide.Length; i++) {
                GameObject go = hide[i];
                if (go == null || !go.activeSelf) continue;
                if (!hideSet.Add(go)) continue;
                fadeOut.Add((go, EnsureCanvasGroup(go)));
            }
        }
        if (show != null) {
            for (int i = 0; i < show.Length; i++) {
                GameObject go = show[i];
                if (go == null || hideSet.Contains(go)) continue;
                fadeIn.Add((go, EnsureCanvasGroup(go)));
            }
        }
        if (fadeOut.Count == 0 && fadeIn.Count == 0) {
            afterPrepare?.Invoke();
            yield break;
        }
        PrepareFadeOut(fadeOut);
        PrepareFadeIn(fadeIn);
        afterPrepare?.Invoke();
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
            cg.alpha = 0f; // 先透明再激活，避免白底页闪一帧
            cg.interactable = true;
            cg.blocksRaycasts = false;
            go.SetActive(true);
            UnifyChildCanvasGroupAlphas(go, cg);
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
