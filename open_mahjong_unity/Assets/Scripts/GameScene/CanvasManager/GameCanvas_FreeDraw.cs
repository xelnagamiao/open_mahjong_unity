using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public partial class GameCanvas {
    private Coroutine _freeDrawAnimation;
    private Dictionary<RectTransform, Vector2> _freeDrawTargets;
    private const float FreeDrawDuration = 0.16f;

    private bool IsFreeDrawChange(string changeType) {
        return FreeGameState.Active != null && !IsHandRecordPlayback()
            && (changeType == "GetCard" || changeType == "GetCardNoAnimation" || changeType == "GetGangReplacementCardNoLayout");
    }

    private void ApplyFreeDraw(int tileId, bool animate) {
        CancelCompetingHandReflowAnimations("自由摸牌");
        ClearHandDrawMarks();
        List<TileCard> main = GetMainHandCardsOrdered();
        if (AutoAction.Instance.IsAutoArrangeHandCards)
            main.Sort((a, b) => TileIdOrder.Compare(a.tileId, b.tileId));

        TileCard draw = Instantiate(tileCardPrefab, handCardsContainer).GetComponent<TileCard>();
        draw.SetTile(tileId, true);
        for (int i = 0; i < main.Count; i++) {
            main[i].handSortIndex = i;
            main[i].transform.SetSiblingIndex(i);
        }
        draw.handSortIndex = main.Count;
        draw.transform.SetSiblingIndex(main.Count);
        _freeDrawTargets = BuildHandLayoutPositions(main, draw);
        RectTransform drawRect = draw.GetComponent<RectTransform>();
        drawRect.anchoredPosition = _freeDrawTargets[drawRect] + (animate ? Vector2.up * tileCardWidth * 0.45f : Vector2.zero);
        // 不用透明淡入；连续摸牌重新定向时，每一张已确认的牌都保持可见。
        CanvasGroup opacity = draw.GetComponent<CanvasGroup>();
        if (opacity != null) opacity.alpha = 1f;
        if (animate) _freeDrawAnimation = StartCoroutine(AnimateFreeDraw(_freeDrawTargets, _handLayoutAnimEpoch));
        else StopFreeDrawAnimation(finish: true);
        GameRecordManager.Instance?.ReapplySelf2DHandChongOverlay();
        if (NormalGameStateManager.Instance.IsSelfActionRequired) RefreshHandTileSelectability();
    }

    private IEnumerator AnimateFreeDraw(Dictionary<RectTransform, Vector2> targets, int epoch) {
        var starts = new Dictionary<RectTransform, Vector2>();
        foreach (var pair in targets) starts[pair.Key] = pair.Key.anchoredPosition;
        float elapsed = 0f;
        while (elapsed < FreeDrawDuration && epoch == _handLayoutAnimEpoch) {
            elapsed += Time.deltaTime;
            float t = 1f - Mathf.Pow(1f - Mathf.Clamp01(elapsed / FreeDrawDuration), 3f);
            foreach (var pair in targets) {
                if (pair.Key != null && pair.Key.parent == handCardsContainer)
                    pair.Key.anchoredPosition = Vector2.Lerp(starts[pair.Key], pair.Value, t);
            }
            yield return null;
        }
        if (epoch != _handLayoutAnimEpoch) yield break;
        foreach (var pair in targets) {
            if (pair.Key != null && pair.Key.parent == handCardsContainer) pair.Key.anchoredPosition = pair.Value;
        }
        _freeDrawAnimation = null;
        _freeDrawTargets = null;
    }

    private void StopFreeDrawAnimation(bool finish) {
        if (_freeDrawAnimation != null) StopCoroutine(_freeDrawAnimation);
        _freeDrawAnimation = null;
        if (finish && _freeDrawTargets != null) {
            foreach (var pair in _freeDrawTargets) {
                if (pair.Key != null && pair.Key.parent == handCardsContainer) pair.Key.anchoredPosition = pair.Value;
            }
        }
        _freeDrawTargets = null;
    }
}
