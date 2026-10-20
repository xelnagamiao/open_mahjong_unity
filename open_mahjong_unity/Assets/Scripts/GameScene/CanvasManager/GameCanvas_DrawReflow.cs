using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public partial class GameCanvas {
    private Coroutine _drawReflowAnimation;
    private Dictionary<RectTransform, Vector2> _drawReflowTargets;
    private const float DrawReflowDuration = 0.16f;

    private bool ShouldUseDrawReflow(string changeType) {
        if (FreeGameState.Active != null && !IsHandRecordPlayback())
            return changeType == "GetCard" || changeType == "GetCardNoAnimation" || changeType == "GetGangReplacementCardNoLayout";
        // 在队列执行时读取实际槽位，补牌不再依赖规则层另记一份是否已摸牌。
        return changeType == "GetCard" && GetPinnedDrawTile() != null;
    }

    private void ApplyDrawReflow(int tileId, bool animate) {
        CancelCompetingHandReflowAnimations("摸牌收拢");
        ClearHandDrawMarks();
        List<TileCard> main = GetMainHandCardsOrdered();
        if (IsHandRecordPlayback() ? !WashizuTile.IsRealtimeHandOrder : AutoAction.Instance.IsAutoArrangeHandCards)
            TileIdOrder.SortHand(main, card => card.tileId);

        TileCard draw = Instantiate(tileCardPrefab, handCardsContainer).GetComponent<TileCard>();
        draw.SetTile(tileId, true);
        isArranged = false;
        for (int i = 0; i < main.Count; i++) {
            main[i].handSortIndex = i;
            main[i].transform.SetSiblingIndex(i);
        }
        draw.handSortIndex = main.Count;
        draw.transform.SetSiblingIndex(main.Count);
        _drawReflowTargets = BuildHandLayoutPositions(main, draw);
        RectTransform drawRect = draw.GetComponent<RectTransform>();
        drawRect.anchoredPosition = _drawReflowTargets[drawRect] + (animate ? Vector2.up * tileCardWidth * 0.45f : Vector2.zero);
        // 不用透明淡入；连续摸牌重新定向时，每一张已确认的牌都保持可见。
        CanvasGroup opacity = draw.GetComponent<CanvasGroup>();
        if (opacity != null) opacity.alpha = 1f;
        if (animate) _drawReflowAnimation = StartCoroutine(AnimateDrawReflow(_drawReflowTargets, _handLayoutAnimEpoch));
        else StopDrawReflowAnimation(finish: true);
        GameRecordManager.Instance?.ReapplySelf2DHandChongOverlay();
        if (NormalGameStateManager.Instance.IsSelfActionRequired) RefreshHandTileSelectability();
    }

    private IEnumerator AnimateDrawReflow(Dictionary<RectTransform, Vector2> targets, int epoch) {
        var starts = new Dictionary<RectTransform, Vector2>();
        foreach (var pair in targets) starts[pair.Key] = pair.Key.anchoredPosition;
        float elapsed = 0f;
        while (elapsed < DrawReflowDuration && epoch == _handLayoutAnimEpoch) {
            elapsed += Time.deltaTime;
            float t = 1f - Mathf.Pow(1f - Mathf.Clamp01(elapsed / DrawReflowDuration), 3f);
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
        _drawReflowAnimation = null;
        _drawReflowTargets = null;
    }

    private void StopDrawReflowAnimation(bool finish) {
        if (_drawReflowAnimation != null) StopCoroutine(_drawReflowAnimation);
        _drawReflowAnimation = null;
        if (finish && _drawReflowTargets != null) {
            foreach (var pair in _drawReflowTargets) {
                if (pair.Key != null && pair.Key.parent == handCardsContainer) pair.Key.anchoredPosition = pair.Value;
            }
        }
        _drawReflowTargets = null;
    }
}
