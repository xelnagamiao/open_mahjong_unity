using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public partial class Game3DManager : MonoBehaviour {
    private bool IsSelfCardsPosition(Transform cardPosition) {
        return selfPosPanel != null && cardPosition == selfPosPanel.cardsPosition;
    }

    // 移除3D手牌显示；skipRearrange 为 true 时仅删牌，收拢由调用方在出牌等操作完成后再执行
    private IEnumerator RemoveHandCardsCoroutine(Transform cardPosition, int removeCount, bool cut_class, int discardTileId = -1, int[] combinationMaskForSelf = null, bool skipRearrange = false) {
        if (IsSelfCardsPosition(cardPosition)) {
            yield return RemoveSelfHandCardsCoroutine(cardPosition, removeCount, cut_class, discardTileId, combinationMaskForSelf);
            yield break;
        }

        Debug.Log($"移除其他玩家手牌 {cardPosition}, 删除数量: {removeCount}, 摸切: {cut_class}");

        // 如果removeCount > 1，使用组合删除方法
        if (removeCount > 1) {
            // 计算当前卡牌总数
            int totalCardCount = cardPosition.childCount;

            // 检查是否有足够的卡牌可以删除
            if (totalCardCount < removeCount) {
                Debug.LogWarning($"卡牌数量不足: 需要删除{removeCount}张，但只有{totalCardCount}张");
                yield break;
            }

            // 计算可以开始删除的随机起始位置
            int maxStartIndex = totalCardCount - removeCount;
            int startIndex = Random.Range(0, maxStartIndex + 1);

            Debug.Log($"开始组合删除卡牌: 总数={totalCardCount}, 删除数量={removeCount}, 起始索引={startIndex}");

            // 始终从 startIndex 连续删除：Unity 子节点在删除后会前移，若用 startIndex+i 会在靠近尾部时少删牌
            for (int i = 0; i < removeCount; i++) {
                if (startIndex >= cardPosition.childCount) break;
                Transform cardToRemove = cardPosition.GetChild(startIndex);
                Debug.Log($"删除卡牌: {cardToRemove.name} (起始索引: {startIndex}, 第{i + 1}/{removeCount}张)");

                lastRemove3DPosition = cardToRemove.position;
                MahjongObjectPool.Instance.Return(-1, cardToRemove.gameObject);
            }
            Debug.Log($"组合删除完成: 已删除{removeCount}张卡牌");

            if (!skipRearrange) {
                yield return StartCoroutine(Rearrange3DCardsWithAnimation(cardPosition));
            }
        }
        // 如果removeCount <= 1，使用单张删除方法
        else {
            // 如果摸切就删除最后一张牌（倒数第一张）
            if (cut_class) {
                int childCount = cardPosition.childCount;
                if (childCount > 0) {
                    int lastIndex = childCount - 1;
                    Transform lastCard = cardPosition.GetChild(lastIndex);
                    Debug.Log($"删除最后一张卡牌: {lastCard.name} (索引: {lastIndex})");

                    // 保存最后一张删牌的位置
                    lastRemove3DPosition = lastCard.position;

                    // 将卡牌归还到对象池
                    MahjongObjectPool.Instance.Return(-1, lastCard.gameObject);
                }
                else {
                    Debug.LogWarning($"摸切：无法删除最后一张牌");
                }
            }
            // 如果手切则随机删除一张主牌区的牌
            else {
                // 计算子物体数量，获取随机索引，随机删除选中的子物体
                int childCount = cardPosition.childCount;
                int randomIndex = UnityEngine.Random.Range(0, childCount);
                Transform randomChild = cardPosition.GetChild(randomIndex);
                Debug.Log($"随机删除了索引为 {randomIndex} 的牌");

                // 保存最后一张删牌的位置
                lastRemove3DPosition = randomChild.position;

                // 将卡牌归还到对象池
                MahjongObjectPool.Instance.Return(-1, randomChild.gameObject);
            }

            yield return null;

            if (!skipRearrange) {
                yield return StartCoroutine(Rearrange3DCardsWithAnimation(cardPosition));
            }
        }
    }

    private IEnumerator RemoveSelfHandCardsCoroutine(Transform cardPosition, int removeCount, bool cut_class, int discardTileId, int[] combinationMaskForSelf, bool skipRearrange = false) {
        if (removeCount > 1 && combinationMaskForSelf != null) {
            List<int> tilesToRemove = new List<int>();
            for (int i = 0; i + 1 < combinationMaskForSelf.Length; i += 2) {
                int flag = combinationMaskForSelf[i];
                int tid = combinationMaskForSelf[i + 1];
                if (flag != 1 && tid >= 10) {
                    tilesToRemove.Add(tid);
                }
            }
            for (int i = 0; i < tilesToRemove.Count && i < removeCount; i++) {
                RemoveOneSelfHandCardByTileId(cardPosition, tilesToRemove[i]);
            }
            if (!skipRearrange) {
                yield return null;
                yield return StartCoroutine(Rearrange3DCardsWithAnimation(cardPosition));
            }
            yield break;
        }

        if (removeCount == 1) {
            if (cut_class) {
                // 摸切：固定最右/末子（须 tileId 一致）→ 任意 tileId 匹配
                RemoveOneSelfMoqieHandCard(cardPosition, discardTileId);
            }
            else if (discardTileId >= 2) {
                RemoveOneSelfHandCardByTileId(cardPosition, discardTileId);
            }
            if (!skipRearrange) {
                yield return null;
                yield return StartCoroutine(Rearrange3DCardsWithAnimation(cardPosition));
            }
        }
    }

    private Transform GetSelfRightmostHandCard(Transform cardPosition) {
        if (cardPosition.childCount == 0) return null;
        Vector3 start = cardPosition.position;
        Transform best = null;
        float bestDot = float.NegativeInfinity;
        for (int i = 0; i < cardPosition.childCount; i++) {
            Transform c = cardPosition.GetChild(i);
            float d = Vector3.Dot(c.position - start, RightDirection);
            if (d > bestDot) {
                bestDot = d;
                best = c;
            }
        }
        return best;
    }

    private void RemoveOneSelfMoqieHandCard(Transform cardPosition, int tileId) {
        if (tileId >= 2) {
            // 如果摸切就删除最右/最后一张牌（须 tileId 一致）
            Transform rightmost = GetSelfRightmostHandCard(cardPosition);
            if (TryReturnSelfHandCardIfTileId(rightmost, tileId)) {
                return;
            }
            if (cardPosition.childCount > 0) {
                Transform lastCard = cardPosition.GetChild(cardPosition.childCount - 1);
                if (TryReturnSelfHandCardIfTileId(lastCard, tileId)) {
                    return;
                }
            }
            RemoveOneSelfHandCardByTileId(cardPosition, tileId);
            return;
        }

        Transform fallback = GetSelfRightmostHandCard(cardPosition);
        if (fallback != null) {
            lastRemove3DPosition = fallback.position;
            MahjongObjectPool.Instance.Return(-1, fallback.gameObject);
        }
        else {
            Debug.LogWarning($"摸切：无法删除最后一张牌");
        }
    }

    private bool TryReturnSelfHandCardIfTileId(Transform card, int tileId) {
        if (card == null) {
            return false;
        }
        Tile3D t3 = card.GetComponent<Tile3D>();
        if (t3 != null && t3.GetTileId() == tileId) {
            lastRemove3DPosition = card.position;
            MahjongObjectPool.Instance.Return(-1, card.gameObject);
            return true;
        }
        return false;
    }

    private void RemoveOneSelfHandCardByTileId(Transform cardPosition, int tileId) {
        for (int i = 0; i < cardPosition.childCount; i++) {
            Transform child = cardPosition.GetChild(i);
            Tile3D t3 = child.GetComponent<Tile3D>();
            if (t3 != null && t3.GetTileId() == tileId) {
                lastRemove3DPosition = child.position;
                MahjongObjectPool.Instance.Return(-1, child.gameObject);
                return;
            }
        }
        Debug.LogWarning($"自家3D手牌未找到要删除的牌 id={tileId}");
    }

    private IEnumerator Rearrange3DCardsWithAnimation(Transform cardPosition) {
        Debug.Log($"3D手牌重新排列");

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

        // 收集所有手牌物体和当前位置
        List<Transform> cards = new List<Transform>();
        Dictionary<Transform, Vector3> currentPositions = new Dictionary<Transform, Vector3>();

        int cardCount = cardPosition.childCount;
        for (int i = 0; i < cardCount; i++) {
            Transform child = cardPosition.GetChild(i);
            cards.Add(child);
            currentPositions[child] = child.position;
        }

        // 按实际位置排序手牌（沿方向向量的投影距离）
        // 这样可以确保手牌按照实际位置顺序排列，而不是删除后的索引顺序
        Vector3 startPosition = cardPosition.position;
        cards.Sort((a, b) => {
            Vector3 posA = a.position - startPosition;
            Vector3 posB = b.position - startPosition;
            float distA = Vector3.Dot(posA, direction);
            float distB = Vector3.Dot(posB, direction);
            return distA.CompareTo(distB);
        });

        // 计算目标位置（按排序后的顺序）
        Dictionary<Transform, Vector3> targetPositions = new Dictionary<Transform, Vector3>();
        for (int i = 0; i < cardCount; i++) {
            Vector3 targetPos = startPosition + direction * cardWidth * i;
            targetPositions[cards[i]] = targetPos;
            cards[i].name = $"ReSeTCard_{i}";
        }

        // 检查是否需要动画：比较每个手牌的当前位置和目标位置
        bool needsAnimation = false;
        foreach (var kvp in currentPositions) {
            Transform card = kvp.Key;
            Vector3 currentPos = kvp.Value;
            Vector3 targetPos = targetPositions[card];

            if (Vector3.Distance(currentPos, targetPos) > 0.01f) {
                needsAnimation = true;
                break;
            }
        }

        if (!needsAnimation) {
            Debug.Log($"3D手牌位置无需调整，跳过动画");
            yield break;
        }
        else {
            // 执行动画
            List<Transform> cardList = new List<Transform>(currentPositions.Keys);
            List<Vector3> targetPosList = new List<Vector3>();
            foreach (Transform card in cardList) {
                targetPosList.Add(targetPositions[card]);
            }
            yield return StartCoroutine(Animate3DCardsToPositions(cardList, targetPosList, cardPosition));
        }
    }
}
