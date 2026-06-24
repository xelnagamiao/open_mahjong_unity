using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public partial class GameCanvas {
    public static float GetCardWidth(RectTransform cardRect) {
        return cardRect.rect.width;
    }

    public bool IsHandRecordPlayback() {
        return GameRecordManager.Instance != null && GameRecordManager.Instance.gameObject.activeSelf;
    }

    // 服务端摸切判定用：返回带摸牌标记(currentGetTile)的牌（即便已被理牌拖入主列）。
    public TileCard GetDrawTile() {
        for (int i = 0; i < handCardsContainer.childCount; i++) {
            TileCard tc = handCardsContainer.GetChild(i).GetComponent<TileCard>();
            if (tc != null && tc.currentGetTile) {
                return tc;
            }
        }
        return null;
    }

    // 布局用：返回固定显示在独立摸牌区(isDrawSlotPinned)的牌；被拖入主列后即不再返回。
    public TileCard GetPinnedDrawTile() {
        for (int i = 0; i < handCardsContainer.childCount; i++) {
            TileCard tc = handCardsContainer.GetChild(i).GetComponent<TileCard>();
            if (tc != null && tc.isDrawSlotPinned) {
                return tc;
            }
        }
        return null;
    }

    public List<TileCard> GetMainHandCardsOrdered(TileCard exclude = null) {
        List<TileCard> list = new List<TileCard>();
        for (int i = 0; i < handCardsContainer.childCount; i++) {
            TileCard tc = handCardsContainer.GetChild(i).GetComponent<TileCard>();
            if (tc == null || tc == exclude || tc.isDrawSlotPinned) {
                continue;
            }
            list.Add(tc);
        }
        list.Sort((a, b) => a.handSortIndex.CompareTo(b.handSortIndex));
        return list;
    }

    /// <summary>
    /// 按主列顺序与可选独立摸牌区计算各牌 anchoredPosition（中心 pivot）。
    /// </summary>
    public Dictionary<RectTransform, Vector2> BuildHandLayoutPositions(
        List<TileCard> mainOrdered,
        TileCard drawTileSeparate,
        TileCard positionExclude = null) {
        Dictionary<RectTransform, Vector2> positions = new Dictionary<RectTransform, Vector2>();
        float x = 0f;
        float lastMainWidth = 0f;
        for (int i = 0; i < mainOrdered.Count; i++) {
            TileCard card = mainOrdered[i];
            if (card == positionExclude) {
                continue;
            }
            RectTransform rt = card.GetComponent<RectTransform>();
            float w = GetCardWidth(rt);
            positions[rt] = new Vector2(x, 0f);
            lastMainWidth = w;
            x += w;
        }
        if (drawTileSeparate != null && drawTileSeparate.isDrawSlotPinned && drawTileSeparate != positionExclude) {
            RectTransform drawRt = drawTileSeparate.GetComponent<RectTransform>();
            float dw = GetCardWidth(drawRt);
            float lastHalf = mainOrdered.Count > 0 ? lastMainWidth * 0.5f : dw * 0.5f;
            float drawCenterX = x + lastHalf + dw * 0.5f;
            positions[drawRt] = new Vector2(drawCenterX, 0f);
        }
        return positions;
    }

    /// <summary>
    /// 主列布局并在 gapInsertIndex 处留出宽度为 gapWidth 的空位（拖拽牌不占位）。
    /// </summary>
    public Dictionary<RectTransform, Vector2> BuildHandLayoutWithGap(
        List<TileCard> mainOrdered,
        int gapInsertIndex,
        float gapWidth,
        TileCard drawTileSeparate) {
        Dictionary<RectTransform, Vector2> positions = new Dictionary<RectTransform, Vector2>();
        gapInsertIndex = Mathf.Clamp(gapInsertIndex, 0, mainOrdered.Count);
        float x = 0f;
        float lastMainWidth = 0f;
        for (int i = 0; i < mainOrdered.Count; i++) {
            if (i == gapInsertIndex) {
                x += gapWidth;
            }
            TileCard card = mainOrdered[i];
            RectTransform rt = card.GetComponent<RectTransform>();
            float w = GetCardWidth(rt);
            positions[rt] = new Vector2(x, 0f);
            lastMainWidth = w;
            x += w;
        }
        if (gapInsertIndex == mainOrdered.Count) {
            x += gapWidth;
        }
        if (drawTileSeparate != null && drawTileSeparate.isDrawSlotPinned) {
            RectTransform drawRt = drawTileSeparate.GetComponent<RectTransform>();
            float dw = GetCardWidth(drawRt);
            float lastHalf = mainOrdered.Count > 0 ? lastMainWidth * 0.5f : dw * 0.5f;
            float drawCenterX = x + lastHalf + dw * 0.5f;
            positions[drawRt] = new Vector2(drawCenterX, 0f);
        }
        return positions;
    }

    public void ApplyHandLayoutPositions(Dictionary<RectTransform, Vector2> positions, List<TileCard> mainOrdered, TileCard drawTileSeparate) {
        for (int i = 0; i < mainOrdered.Count; i++) {
            TileCard card = mainOrdered[i];
            card.handSortIndex = i;
            card.transform.SetSiblingIndex(i);
            RectTransform rt = card.GetComponent<RectTransform>();
            if (positions.TryGetValue(rt, out Vector2 pos)) {
                rt.anchoredPosition = pos;
            }
        }
        int sibling = mainOrdered.Count;
        if (drawTileSeparate != null && drawTileSeparate.isDrawSlotPinned) {
            drawTileSeparate.handSortIndex = mainOrdered.Count;
            drawTileSeparate.transform.SetSiblingIndex(sibling);
            RectTransform drawRt = drawTileSeparate.GetComponent<RectTransform>();
            if (positions.TryGetValue(drawRt, out Vector2 drawPos)) {
                drawRt.anchoredPosition = drawPos;
            }
        }
    }

    /// <summary>
    /// 根据当前 handSortIndex / currentGetTile 布局全部手牌（无动画）。
    /// </summary>
    public void LayoutHandCardsFromCurrentOrder() {
        List<TileCard> main = GetMainHandCardsOrdered();
        TileCard draw = GetPinnedDrawTile();
        Dictionary<RectTransform, Vector2> positions = BuildHandLayoutPositions(main, draw, null);
        ApplyHandLayoutPositions(positions, main, draw);
    }

    public bool IsMainHandSortedByTileId() {
        List<TileCard> main = GetMainHandCardsOrdered();
        for (int i = 1; i < main.Count; i++) {
            if (TileIdOrder.Compare(main[i - 1].tileId, main[i].tileId) > 0) {
                return false;
            }
        }
        return true;
    }

    /// <summary>
    /// 主列按牌值排序并布局，摸牌张保持独立不参与排序；与发牌后理牌相同使用移动动画。
    /// </summary>
    public void SortMainHandByTileIdIfNeeded() {
        if (IsHandRecordPlayback() || IsChangeHandCardProcessing || IsHandReflowAnimating) {
            return;
        }
        if (IsMainHandSortedByTileId()) {
            return;
        }
        if (_sortMainHandCoroutine != null) {
            return;
        }
        _sortMainHandCoroutine = StartCoroutine(SortMainHandByTileIdWrapped());
    }

    private IEnumerator SortMainHandByTileIdWrapped() {
        CancelCompetingHandReflowAnimations("主列排序");
        yield return RunHandReflowAnim(SortMainHandByTileIdCoroutine());
    }

    private void CancelCompetingHandReflowAnimations(string reason) {
        if (_sortMainHandCoroutine != null) {
            StopCoroutine(_sortMainHandCoroutine);
            _sortMainHandCoroutine = null;
            Debug.Log($"[HandLayout] 取消主列排序动画 | 原因={reason}");
        }
        if (_discardLayoutCoroutine != null) {
            StopCoroutine(_discardLayoutCoroutine);
            _discardLayoutCoroutine = null;
            Debug.Log($"[HandLayout] 取消出牌收拢动画 | 原因={reason}");
        }
        if (handCardDragController != null) {
            handCardDragController.CancelGapLayoutAnimation();
        }
    }

    private IEnumerator RunHandReflowAnim(IEnumerator inner) {
        _handReflowAnimDepth++;
        yield return inner;
        _handReflowAnimDepth--;
    }

    private System.Collections.IEnumerator SortMainHandByTileIdCoroutine() {
        List<TileCard> main = GetMainHandCardsOrdered();
        main.Sort((a, b) => TileIdOrder.Compare(a.tileId, b.tileId));
        TileCard draw = GetPinnedDrawTile();
        Dictionary<RectTransform, Vector2> targetPositions = BuildHandLayoutPositions(main, draw, null);

        bool needsAnimation = false;
        foreach (var kvp in targetPositions) {
            if (Vector2.Distance(kvp.Key.anchoredPosition, kvp.Value) > 0.01f) {
                needsAnimation = true;
                break;
            }
        }

        for (int i = 0; i < main.Count; i++) {
            main[i].handSortIndex = i;
            main[i].transform.SetSiblingIndex(i);
        }
        if (draw != null && draw.isDrawSlotPinned) {
            draw.handSortIndex = main.Count;
            draw.transform.SetSiblingIndex(main.Count);
        }

        if (needsAnimation) {
            List<RectTransform> cards = new List<RectTransform>(targetPositions.Keys);
            List<Vector2> targets = new List<Vector2>();
            for (int i = 0; i < cards.Count; i++) {
                targets.Add(targetPositions[cards[i]]);
            }
            yield return AnimateCardsToPositions(cards, targets);
        }
        else {
            foreach (var kvp in targetPositions) {
                kvp.Key.anchoredPosition = kvp.Value;
            }
        }
        SetHandArranged(true);
        _sortMainHandCoroutine = null;
    }

    public Vector2 GetDrawTileTargetPosition(List<TileCard> mainOrdered) {
        TileCard draw = GetPinnedDrawTile();
        if (draw == null) {
            return Vector2.zero;
        }
        Dictionary<RectTransform, Vector2> positions = BuildHandLayoutPositions(mainOrdered, draw, null);
        RectTransform drawRt = draw.GetComponent<RectTransform>();
        return positions.TryGetValue(drawRt, out Vector2 pos) ? pos : drawRt.anchoredPosition;
    }

    public Coroutine AnimateHandCardsToLayout(Dictionary<RectTransform, Vector2> targetPositions, float duration = 0.3f) {
        List<RectTransform> cards = new List<RectTransform>(targetPositions.Keys);
        List<Vector2> targets = new List<Vector2>();
        for (int i = 0; i < cards.Count; i++) {
            targets.Add(targetPositions[cards[i]]);
        }
        return StartCoroutine(AnimateCardsToPositions(cards, targets, duration));
    }

    /// <summary>
    /// 出牌前收拢其余手牌（动画），打出牌保持当前视觉位置不被复位。
    /// </summary>
    public void AnimateHandLayoutForDiscard(TileCard discardCard) {
        if (discardCard == null) {
            return;
        }
        if (_discardLayoutCoroutine != null) {
            StopCoroutine(_discardLayoutCoroutine);
        }
        _discardLayoutCoroutine = StartCoroutine(AnimateHandLayoutForDiscardCoroutine(discardCard, null, null));
    }

    /// <summary>
    /// 拖拽出牌时可传入已算好的 main/draw（含理牌顺序变更）。
    /// </summary>
    public void AnimateHandLayoutForDiscard(TileCard discardCard, List<TileCard> main, TileCard draw) {
        if (discardCard == null) {
            return;
        }
        if (_discardLayoutCoroutine != null) {
            StopCoroutine(_discardLayoutCoroutine);
        }
        _discardLayoutCoroutine = StartCoroutine(AnimateHandLayoutForDiscardCoroutine(discardCard, main, draw));
    }

    private IEnumerator AnimateHandLayoutForDiscardCoroutine(
        TileCard discardCard,
        List<TileCard> mainOverride,
        TileCard drawOverride) {
        List<TileCard> main = mainOverride ?? GetMainHandCardsOrdered(discardCard);
        TileCard draw = drawOverride;
        if (mainOverride == null) {
            // 手切（打出非摸牌张）时，摸牌张应并入手牌主列、补到空缺处。
            // 这里直接让它在收拢预览中归位（与服务端返回后的最终重排一致），
            // 避免出现"手牌先收拢、摸牌张随后才塞入空缺"的两段式排序动画。
            TileCard pinnedDraw = discardCard.isDrawSlotPinned ? null : GetPinnedDrawTile();
            if (pinnedDraw != null && pinnedDraw != discardCard) {
                pinnedDraw.isDrawSlotPinned = false;
                if (!main.Contains(pinnedDraw)) {
                    main.Add(pinnedDraw);
                }
                bool autoArrange = AutoAction.Instance != null && AutoAction.Instance.IsAutoArrangeHandCards;
                if (autoArrange) {
                    main.Sort((a, b) => TileIdOrder.Compare(a.tileId, b.tileId));
                } else {
                    main.Sort((a, b) => a.handSortIndex.CompareTo(b.handSortIndex));
                }
            }
            draw = null;
        }

        Dictionary<RectTransform, Vector2> positions = BuildHandLayoutPositions(main, draw, discardCard);
        for (int i = 0; i < main.Count; i++) {
            main[i].handSortIndex = i;
            main[i].transform.SetSiblingIndex(i);
        }
        if (draw != null && draw.isDrawSlotPinned) {
            draw.handSortIndex = main.Count;
            draw.transform.SetSiblingIndex(main.Count);
        }

        List<RectTransform> cards = new List<RectTransform>(positions.Keys);
        List<Vector2> targets = new List<Vector2>(cards.Count);
        for (int i = 0; i < cards.Count; i++) {
            targets.Add(positions[cards[i]]);
        }

        bool needsAnimation = false;
        for (int i = 0; i < cards.Count; i++) {
            if (cards[i] == null) {
                yield break;
            }
            if (Vector2.Distance(cards[i].anchoredPosition, targets[i]) > 0.01f) {
                needsAnimation = true;
                break;
            }
        }

        if (needsAnimation) {
            yield return AnimateCardsToPositions(cards, targets);
        }
        else {
            foreach (var kvp in positions) {
                kvp.Key.anchoredPosition = kvp.Value;
            }
        }
        _discardLayoutCoroutine = null;
    }
}
