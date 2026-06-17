using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 四川血战终局：reveal_hu → settle_hu* → chajiao* → cha_refund 顺序队列，避免 StartSequence 互相打断。
/// </summary>
public partial class RoundEndPresentation {
    private readonly Queue<IEnumerator> _sichuanEndgameQueue = new Queue<IEnumerator>();
    private Coroutine _sichuanEndgameRunner;
    private bool _sichuanEndgamePanelStarted;

    public void ResetSichuanEndgameQueue() {
        _sichuanEndgameQueue.Clear();
        _sichuanEndgamePanelStarted = false;
        if (_sichuanEndgameRunner != null) {
            StopCoroutine(_sichuanEndgameRunner);
            _sichuanEndgameRunner = null;
        }
    }

    public void EnqueueSichuanEndgameStep(IEnumerator step) {
        if (step == null) return;
        _sichuanEndgameQueue.Enqueue(step);
        if (_sichuanEndgameRunner == null) {
            _sichuanEndgameRunner = StartCoroutine(RunSichuanEndgameQueue());
        }
    }

    private IEnumerator RunSichuanEndgameQueue() {
        StopActiveSequence();
        HideSelfGameplayControl(false);
        gameObject.SetActive(true);
        _sichuanEndgamePanelStarted = false;
        while (_sichuanEndgameQueue.Count > 0) {
            yield return _sichuanEndgameQueue.Dequeue();
        }
        _sichuanEndgameRunner = null;
    }

    /// <summary>与国标和牌一致：先准备面板内容（alpha=0），再渐显。</summary>
    private void BeginSichuanEndgamePanel() {
        if (_sichuanEndgamePanelStarted && EndResultPanel.Instance != null) {
            EndResultPanel.Instance.gameObject.SetActive(false);
        }
        _sichuanEndgamePanelStarted = true;
        PreparePresentationRoot(true);
    }

    private IEnumerator CoFadeInSichuanEndgamePanel() {
        yield return PlayPresentationFade(true);
    }
}
