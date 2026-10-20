using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public partial class Game3DManager {
    /// <summary>Queue a phase boundary after earlier draws and before subsequent hand actions.</summary>
    public void ConsolidateAllHands() {
        foreach (string seat in HandAnimPlayerPositions)
            EnqueueHandAnimOp(seat, new HandAnimOp { Kind = HandAnimOpKind.Consolidate });
    }

    private IEnumerator ConsolidateHandFromQueue(string seat, PosPanel3D panel) {
        // Expanded replay may still have a flower flight outside the standing-hand queue.
        while (_recordHandAnimationsByPlayer.TryGetValue(seat, out int count) && count > 0) yield return null;
        StopHandDrawReflow(seat, finish: true);
        if (washizuRealtimeLayouts.TryGetValue(seat, out var layout)) layout.draw_pinned = false;
        yield return IsRecordShowCardsModeActive() && seat != "self"
            ? RearrangeRecordShowMergeAllWithAnimation(panel.ShowCardsPosition, seat, preserveDrawSource: true)
            : Rearrange3DCardsWithAnimation(panel.cardsPosition);
    }

    // 检测手牌是否需要排列（检查手牌位置是否整齐）
    private bool NeedsRearrangement(Transform cardPosition) {
        if (cardPosition.childCount == 0) {
            return false;
        }

        // 确定方向向量
        Vector3 direction = Vector3.zero;
        if (cardPosition == rightPosPanel.cardsPosition) {
            direction = FrontDirection; // 向前
        }
        else if (cardPosition == leftPosPanel.cardsPosition) {
            direction = BackDirection; // 向后
        }
        else if (cardPosition == topPosPanel.cardsPosition) {
            direction = LeftDirection; // 向左
        }
        else if (cardPosition == selfPosPanel.cardsPosition) {
            direction = RightDirection; // 向右
        }
        else {
            // self 位置不需要排列（自己的手牌是2D显示的）
            return false;
        }

        // 收集所有手牌物体和当前位置
        List<Transform> cards = new List<Transform>();
        int cardCount = cardPosition.childCount;
        for (int i = 0; i < cardCount; i++) {
            Transform child = cardPosition.GetChild(i);
            cards.Add(child);
        }

        // 按实际位置排序手牌（沿方向向量的投影距离）
        Vector3 startPosition = HandRowOrigin(cardPosition, direction);
        cards.Sort((a, b) => {
            Vector3 posA = a.position - startPosition;
            Vector3 posB = b.position - startPosition;
            float distA = Vector3.Dot(posA, direction);
            float distB = Vector3.Dot(posB, direction);
            return distA.CompareTo(distB);
        });

        // 检查每个手牌的当前位置和目标位置是否一致
        for (int i = 0; i < cardCount; i++) {
            Vector3 currentPos = cards[i].position;
            Vector3 targetPos = PlaceTileOnTable(startPosition + direction * handStep * i, cards[i].rotation);

            if (Vector3.Distance(currentPos, targetPos) > 0.01f) {
                return true; // 需要排列
            }
        }

        return false; // 不需要排列
    }

    /// <summary>
    /// 检测并排列各家 3D 手牌。skipPlayerPosition 为当前行动者时不收拢，保留摸牌区间隙（与 2D askHand 不收拢摸牌区一致）。
    /// </summary>
    public void CheckAndRearrangeAllPlayersHandCards(string skipPlayerPosition = null) {
        foreach (string playerPosition in HandAnimPlayerPositions) {
            if (!string.IsNullOrEmpty(skipPlayerPosition) && playerPosition == skipPlayerPosition) {
                continue;
            }
            PosPanel3D panel = GetPosPanel(playerPosition);
            if (panel != null && NeedsRearrangement(panel.cardsPosition)) {
                StartHandRearrange(playerPosition);
            }
        }
    }
}
