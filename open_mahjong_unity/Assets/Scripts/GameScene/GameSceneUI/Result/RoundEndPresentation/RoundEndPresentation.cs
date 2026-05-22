using System;
using System.Collections;
using UnityEngine;

/// <summary>
/// 局终展示根控制器：统一处理自身操作区、倒牌、根节点渐显，以及子面板协程启动时机。
/// </summary>
public partial class RoundEndPresentation : MonoBehaviour {
    public static RoundEndPresentation Instance { get; private set; }

    [SerializeField] private GameObject selfGameplayControlRoot;
    [SerializeField] private CanvasGroup presentationCanvasGroup;
    [SerializeField] private float fadeInSeconds = RoundEndTiming.RoundEndPresentationFadeSeconds;
    [SerializeField] private float handRevealHoldSeconds = RoundEndTiming.RoundEndHandRevealSeconds;

    private Coroutine activeRoundEndCoroutine;

    public float FadeInSeconds => fadeInSeconds;
    public float HandRevealHoldSeconds => handRevealHoldSeconds;

    private void Awake() {
        if (Instance != null && Instance != this) {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        if (presentationCanvasGroup == null) {
            presentationCanvasGroup = GetComponent<CanvasGroup>();
        }
    }

    /// <summary>隐藏自身操作区。</summary>
    public void HideSelfGameplayControl(bool revealSelfHand = true) {
        selfGameplayControlRoot.SetActive(false);
        if (revealSelfHand) {
            Game3DManager.Instance.RefreshSelfFaceHandFromTileList();
        }
    }

    /// <summary>显示自身操作区并重新同步手牌。</summary>
    public void ShowSelfGameplayControlAndResyncHand3D() {
        selfGameplayControlRoot.SetActive(true);
        Game3DManager.Instance.RefreshSelfBlankHandFromSelfTileList();
    }

    /// <summary>停止当前局终流程。</summary>
    public void StopActiveSequence() {
        if (activeRoundEndCoroutine != null) {
            StopCoroutine(activeRoundEndCoroutine);
            activeRoundEndCoroutine = null;
        }
    }

    private void StartSequence(IEnumerator routine) {
        StopActiveSequence();
        activeRoundEndCoroutine = StartCoroutine(routine);
    }

    private void PreparePresentationRoot(bool playPresentationEffects) {
        gameObject.SetActive(true);
        if (presentationCanvasGroup == null) {
            return;
        }
        presentationCanvasGroup.alpha = playPresentationEffects ? 0f : 1f;
    }

    private IEnumerator PlayPresentationFade(bool playPresentationEffects) {
        if (!playPresentationEffects || presentationCanvasGroup == null || fadeInSeconds <= 0f) {
            if (presentationCanvasGroup != null) {
                presentationCanvasGroup.alpha = 1f;
            }
            yield break;
        }

        float t = 0f;
        while (t < fadeInSeconds) {
            t += Time.deltaTime;
            presentationCanvasGroup.alpha = Mathf.Clamp01(t / fadeInSeconds);
            yield return null;
        }
        presentationCanvasGroup.alpha = 1f;
    }

    private IEnumerator PlayAfterFade(Action playAction, bool playPresentationEffects) {
        yield return PlayPresentationFade(playPresentationEffects);
        playAction();
        activeRoundEndCoroutine = null;
    }
}
