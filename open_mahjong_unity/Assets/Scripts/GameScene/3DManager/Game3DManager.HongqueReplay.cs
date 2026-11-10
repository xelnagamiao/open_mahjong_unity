using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public partial class Game3DManager {
    // 单次杠先移到目标组上方，留出可辨认的停顿，再落入按牌组规则排好的槽位。
    private const float HongqueKongTravelSeconds = 0.28f;
    private const float HongqueKongHoverSeconds = 0.12f;
    private const float HongqueKongDropSeconds = 0.24f;
    private int hongqueKongStartedGeneration = -1;
    private float hongqueKongStartedAt;

    internal float HongqueKongDeclarationDelayRemaining => hongqueKongStartedGeneration == _handAnimationGeneration
        ? Mathf.Max(0f, hongqueKongStartedAt + HepaiRevealTiming.HongqueKongDeclarationIntervalSeconds - Time.time) : 0f;

    // 与连续杠共用落牌队列；末次杠起算 0.64 秒，不额外等待杠字和语音。
    internal bool HasPendingHongqueWinAnnouncement => HasPendingRecordTableAnimations
        || HongqueKongDeclarationDelayRemaining > 0f;

    public void ExtendHongqueReplayMeld(string position, List<int> addedTiles, int[] meldMask,
        bool fromDraw, int meldIndex = -1) {
        var record = GameRecordManager.Instance;
        if (record == null || !record.recordPlayer_to_info.TryGetValue(position, out var player)) return;
        if (meldIndex < 0) meldIndex = player.combinationMasks.FindIndex(mask => mask.SequenceEqual(meldMask));
        PlayHongqueKong(position, addedTiles, player.combinationMasks, meldIndex, fromDraw);
    }

    /// <summary>
    /// 实时与牌谱共用每家手牌队列。固定本步的全部副露快照，连续事件不得偷读下一步最终牌型。
    /// 逻辑手牌由消息投影立即更新；可见的 2D/3D 手牌在轮到本步时才移除。
    /// </summary>
    public void PlayHongqueKong(string position, IList<int> addedTiles, IList<int[]> melds,
        int meldIndex, bool fromDraw = false) {
        if (!IsHandAnimPlayer(position) || addedTiles == null || addedTiles.Count == 0
            || melds == null || meldIndex < 0 || meldIndex >= melds.Count) return;
        EnqueueHandAnimOp(position, new HandAnimOp {
            Kind = HandAnimOpKind.HongqueKong,
            HandTiles = addedTiles.ToArray(),
            ReplacementMelds = melds.Select(mask => (int[])mask.Clone()).ToArray(),
            MeldIndex = meldIndex,
            CutClass = fromDraw,
        });
    }

    private IEnumerator HongqueKongFromQueue(PosPanel3D panel, HandAnimOp op) {
        int generation = _handAnimationGeneration;
        // 吃碰是独立跟踪的动画；它完成后才能移动已经摆在桌上的目标组。
        while (_recordHandAnimationsByPlayer.TryGetValue(op.PlayerPosition, out int playing) && playing > 0) {
            yield return null;
            if (generation != _handAnimationGeneration) yield break;
        }
        if (op.PlayerPosition == "self") {
            while (GameCanvas.Instance != null && GameCanvas.Instance.IsChangeHandCardProcessing) {
                yield return null;
                if (generation != _handAnimationGeneration) yield break;
            }
        }

        hongqueKongStartedGeneration = generation;
        hongqueKongStartedAt = Time.time;
        SoundManager.Instance?.PlayActionSound(op.PlayerPosition, "gang");
        GameCanvas.Instance?.ShowActionDisplay(op.PlayerPosition, "gang", "hongque");
        bool expanded = IsRecordShowCardsModeActive() && op.PlayerPosition != "self";
        var starts = new List<Vector3>();
        for (int i = 0; i < op.HandTiles.Length; i++) {
            int tile = op.HandTiles[i];
            if (op.PlayerPosition == "self") {
                GameCanvas.Instance?.ChangeHandCards("RemoveJiagangCard", tile, null, null);
                yield return RemoveSelfHandCardsCoroutine(panel.cardsPosition, 1, op.CutClass && i == 0,
                    tile, null, true, op.PlayerPosition);
                starts.Add(panel.outputPos != null ? panel.outputPos.position : panel.cardsPosition.position);
            } else if (expanded) {
                yield return RemoveRecordShowHandCardCoroutine(panel.ShowCardsPosition, tile,
                    op.CutClass && i == 0, op.PlayerPosition);
                starts.Add(GetLastRemovePos(op.PlayerPosition));
            } else {
                yield return RemoveHandCardsCoroutine(panel.cardsPosition, 1, op.CutClass && i == 0,
                    tile, null, true, op.PlayerPosition);
                starts.Add(GetLastRemovePos(op.PlayerPosition));
            }
            if (generation != _handAnimationGeneration) yield break;
        }

        EnsureMeldGroups(panel, op.ReplacementMelds.Length);
        Quaternion rotation = Quaternion.Euler(90, 0, op.PlayerPosition == "self" ? 180
            : op.PlayerPosition == "left" ? 90 : op.PlayerPosition == "top" ? 0 : 270);
        Vector3 direction = op.PlayerPosition == "self" ? LeftDirection
            : op.PlayerPosition == "left" ? FrontDirection
            : op.PlayerPosition == "top" ? RightDirection : BackDirection;
        InitializeTilePlacement();
        GetTileSurface(out _, out Vector3 normal);
        normal.Normalize();

        var oldCards = new List<Transform>();
        var oldStarts = new List<Vector3>();
        var oldTargets = new List<Vector3>();
        var movingCards = new List<Transform>();
        var movingTargets = new List<Vector3>();
        var movingStarts = new List<Vector3>();
        var remainingAdded = op.HandTiles.ToList();
        var remainingStarts = new List<Vector3>(starts);
        Vector3 cursor = panel.combinationsPosition.position;
        Vector3 groupCenter = Vector3.zero;
        int groupCount = 0;

        for (int groupIndex = 0; groupIndex < op.ReplacementMelds.Length; groupIndex++) {
            Transform parent = panel.combination3DObjects[groupIndex];
            int[] mask = HongqueDisplayOrder.MeldMask(op.ReplacementMelds[groupIndex], vertical: true);
            var existing = new List<Transform>();
            foreach (Transform child in parent) existing.Add(child);
            for (int pair = mask.Length - 2; pair >= 0; pair -= 2) {
                int tileId = mask[pair + 1];
                cursor += direction * (cardWidth + (pair == mask.Length - 2 && groupIndex > 0 ? MeldSpacingGap() : 0f));
                Vector3 target = PlaceTileOnTable(cursor, rotation);
                Transform card = existing.FirstOrDefault(child => child != null
                    && MahjongObjectPool.GetTile3D(child.gameObject)?.GetTileId() == tileId);
                if (card != null) {
                    existing.Remove(card);
                    oldCards.Add(card);
                    oldStarts.Add(card.position);
                    oldTargets.Add(target);
                } else {
                    int addedIndex = groupIndex == op.MeldIndex ? remainingAdded.IndexOf(tileId) : -1;
                    if (addedIndex < 0) {
                        Debug.LogError($"虹雀加杠动画缺少原副露牌: {op.PlayerPosition}/{groupIndex}/{tileId}");
                        yield break;
                    }
                    Vector3 start = remainingStarts[addedIndex];
                    remainingAdded.RemoveAt(addedIndex);
                    remainingStarts.RemoveAt(addedIndex);
                    GameObject obj = MahjongObjectPool.Instance.Spawn(tileId, start, rotation);
                    if (obj == null) yield break;
                    card = obj.transform;
                    card.SetParent(parent, true);
                    MahjongObjectPool.GetTile3D(obj)?.ApplyCombinationPeekState(tileId, 0);
                    MahjongObjectPool.Instance.RefreshTileCollider(obj);
                    Card3DHoverManager.Instance.RegisterCard(obj, tileId);
                    movingCards.Add(card);
                    movingStarts.Add(start);
                    movingTargets.Add(target);
                }
                card.SetSiblingIndex((mask.Length - 2 - pair) / 2);
                if (groupIndex == op.MeldIndex) { groupCenter += target; groupCount++; }
            }
        }
        if (movingCards.Count == 0 || groupCount == 0) yield break;
        groupCenter /= groupCount;
        Vector3 hover = groupCenter + normal * (cardHeight * 1.35f);
        var hoverPositions = new List<Vector3>();
        for (int i = 0; i < movingCards.Count; i++)
            hoverPositions.Add(hover + direction * ((i - (movingCards.Count - 1) * 0.5f) * cardWidth));

        float elapsed = 0f;
        while (elapsed < HongqueKongTravelSeconds) {
            if (generation != _handAnimationGeneration) yield break;
            elapsed += Time.deltaTime;
            float t = Mathf.SmoothStep(0f, 1f, elapsed / HongqueKongTravelSeconds);
            for (int i = 0; i < movingCards.Count; i++)
                if (IsCardDrivable(movingCards[i], panel.combination3DObjects[op.MeldIndex]))
                    movingCards[i].position = Vector3.Lerp(movingStarts[i], hoverPositions[i], t);
            yield return null;
        }
        yield return new WaitForSeconds(HongqueKongHoverSeconds);
        if (generation != _handAnimationGeneration) yield break;
        elapsed = 0f;
        while (elapsed < HongqueKongDropSeconds) {
            if (generation != _handAnimationGeneration) yield break;
            elapsed += Time.deltaTime;
            float t = Mathf.SmoothStep(0f, 1f, elapsed / HongqueKongDropSeconds);
            for (int i = 0; i < oldCards.Count; i++)
                if (IsCardDrivable(oldCards[i], null)) oldCards[i].position = Vector3.Lerp(oldStarts[i], oldTargets[i], t);
            for (int i = 0; i < movingCards.Count; i++)
                if (IsCardDrivable(movingCards[i], panel.combination3DObjects[op.MeldIndex]))
                    movingCards[i].position = Vector3.Lerp(hoverPositions[i], movingTargets[i], t);
            yield return null;
        }
        StoreCombinationCursor(op.PlayerPosition, cursor);
        SetCombinationLastSlotWidth(op.PlayerPosition, cardWidth);
        SoundManager.Instance?.PlayPhysicsSound("gang");
        if (expanded) {
            ClearRecordPlayerDrawSlotState(op.PlayerPosition);
            yield return RearrangeRecordShowMergeAllWithAnimation(panel.ShowCardsPosition, op.PlayerPosition);
        } else {
            yield return Rearrange3DCardsWithAnimation(panel.cardsPosition);
        }
        while (op.PlayerPosition == "self" && GameCanvas.Instance != null && GameCanvas.Instance.IsChangeHandCardProcessing) {
            yield return null;
            if (generation != _handAnimationGeneration) yield break;
        }
        if (generation != _handAnimationGeneration) yield break;
    }
}
