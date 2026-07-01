using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 暗杠/加杠手牌删收拢与杠后岭上摸牌走各家串行队列，保证「删牌 → 收拢 → 摸牌」顺序正确。
/// 出牌 / 普通摸牌 / 吃碰明杠等仍走 Change3DTileCoroutine，避免多家快速出牌时队列互相拖慢。
/// </summary>
public partial class Game3DManager {
    private enum HandAnimOpKind {
        RemoveCards,
        Rearrange,
        DrawCard,
    }

    private sealed class HandAnimOp {
        public HandAnimOpKind Kind;
        public string PlayerPosition;
        public int TileId;
        public int RemoveCount;
        public bool CutClass;
        public int[] CombinationMask;
    }

    private static readonly string[] HandAnimPlayerPositions = { "self", "left", "top", "right" };

    private readonly Dictionary<string, Queue<HandAnimOp>> _handAnimQueues = new Dictionary<string, Queue<HandAnimOp>> {
        { "self", new Queue<HandAnimOp>() },
        { "left", new Queue<HandAnimOp>() },
        { "top", new Queue<HandAnimOp>() },
        { "right", new Queue<HandAnimOp>() },
    };

    private readonly Dictionary<string, Coroutine> _handAnimProcessors = new Dictionary<string, Coroutine>();

    /// <summary>暗杠/加杠广播后，下一笔该玩家的 GetCard（岭上摸牌）须入队。</summary>
    private readonly Dictionary<string, bool> _ankanPendingDrawByPlayer = new Dictionary<string, bool> {
        { "self", false },
        { "left", false },
        { "top", false },
        { "right", false },
    };

    /// <summary>他家出牌后，手牌收拢动画开始前的停顿。</summary>
    private const float DiscardSettlePauseSec = 0.2f;

    private static bool IsHandAnimPlayer(string playerPosition) {
        return playerPosition == "self" || playerPosition == "left" || playerPosition == "top" || playerPosition == "right";
    }

    private static bool IsMeldHandAction(string actionType) {
        return actionType == "chi_left" || actionType == "chi_mid" || actionType == "chi_right"
            || actionType == "peng" || actionType == "gang" || actionType == "angang" || actionType == "jiagang";
    }

    private void ClearAnkanPendingDrawFlags() {
        foreach (string pos in HandAnimPlayerPositions) {
            _ankanPendingDrawByPlayer[pos] = false;
        }
    }

    private void StopAllHandAnimationQueues() {
        foreach (var kv in _handAnimProcessors) {
            if (kv.Value != null) {
                StopCoroutine(kv.Value);
            }
        }
        _handAnimProcessors.Clear();
        foreach (Queue<HandAnimOp> queue in _handAnimQueues.Values) {
            queue.Clear();
        }
        ClearAnkanPendingDrawFlags();
    }

    private void EnqueueHandAnimOp(string playerPosition, HandAnimOp op) {
        if (!IsHandAnimPlayer(playerPosition)) {
            return;
        }
        op.PlayerPosition = playerPosition;
        _handAnimQueues[playerPosition].Enqueue(op);
        EnsureHandAnimProcessorRunning(playerPosition);
    }

    private void EnsureHandAnimProcessorRunning(string playerPosition) {
        if (_handAnimProcessors.TryGetValue(playerPosition, out Coroutine running) && running != null) {
            return;
        }
        _handAnimProcessors[playerPosition] = StartCoroutine(RunHandAnimQueue(playerPosition));
    }

    private IEnumerator RunHandAnimQueue(string playerPosition) {
        Queue<HandAnimOp> queue = _handAnimQueues[playerPosition];
        while (queue.Count > 0) {
            HandAnimOp op = queue.Dequeue();
            yield return ExecuteHandAnimOp(op);
        }
        _handAnimProcessors[playerPosition] = null;
    }

    private IEnumerator ExecuteHandAnimOp(HandAnimOp op) {
        PosPanel3D panel = GetPosPanel(op.PlayerPosition);
        if (panel == null) {
            yield break;
        }

        switch (op.Kind) {
            case HandAnimOpKind.RemoveCards:
                yield return RemoveHandCardsFromQueue(panel.cardsPosition, op);
                break;
            case HandAnimOpKind.Rearrange:
                yield return Rearrange3DCardsWithAnimation(panel.cardsPosition);
                break;
            case HandAnimOpKind.DrawCard:
                if (op.PlayerPosition == "self") {
                    yield break;
                }
                yield return Get3DTileCoroutine(op.PlayerPosition, "get", op.TileId);
                break;
        }
    }

    private IEnumerator RemoveHandCardsFromQueue(Transform cardPosition, HandAnimOp op) {
        if (IsSelfCardsPosition(cardPosition)) {
            yield return RemoveSelfHandCardsCoroutine(cardPosition, op.RemoveCount, op.CutClass, op.TileId, op.CombinationMask, skipRearrange: true, op.PlayerPosition);
        }
        else {
            yield return RemoveHandCardsCoroutine(cardPosition, op.RemoveCount, op.CutClass, op.TileId, op.CombinationMask, skipRearrange: true, op.PlayerPosition);
        }
    }

    private void EnqueueAnkanHandWork(string playerPosition, int[] combinationMask, bool isMoGang = false, int angangTileId = 0) {
        if (isMoGang) {
            EnqueueHandAnimOp(playerPosition, new HandAnimOp {
                Kind = HandAnimOpKind.RemoveCards,
                RemoveCount = 1,
                CutClass = true,
                TileId = angangTileId,
            });
            EnqueueHandAnimOp(playerPosition, new HandAnimOp {
                Kind = HandAnimOpKind.RemoveCards,
                RemoveCount = 3,
                CombinationMask = combinationMask,
            });
        } else {
            EnqueueHandAnimOp(playerPosition, new HandAnimOp {
                Kind = HandAnimOpKind.RemoveCards,
                RemoveCount = 4,
                CombinationMask = combinationMask,
            });
        }
        EnqueueHandAnimOp(playerPosition, new HandAnimOp {
            Kind = HandAnimOpKind.Rearrange,
        });
    }

    private void EnqueueGangDraw(string playerPosition, int tileId) {
        EnqueueHandAnimOp(playerPosition, new HandAnimOp {
            Kind = HandAnimOpKind.DrawCard,
            TileId = tileId,
        });
    }

    private void EnqueueJiagangHandWork(string playerPosition, int jiagangTileId, bool isMoGang) {
        EnqueueHandAnimOp(playerPosition, new HandAnimOp {
            Kind = HandAnimOpKind.RemoveCards,
            RemoveCount = 1,
            CutClass = isMoGang,
            TileId = jiagangTileId,
        });
        EnqueueHandAnimOp(playerPosition, new HandAnimOp {
            Kind = HandAnimOpKind.Rearrange,
        });
    }

    /// <summary>
    /// 吃碰明杠等：被鸣牌来自河牌时回收河牌切子（加杠/暗杠除外）。
    /// 旧实现依赖全局 lastCutJiagang3DObject，一旦「吃→立刻出牌」紧邻发生，全局引用会被新切牌覆盖，
    /// 导致误回收新切牌而河牌残留；这里改为按「打牌者位置 + 被鸣牌张 id」在河里精确查找要回收的对象，
    /// 并只终止该打牌者的飞牌协程（按玩家隔离），不再触碰他家飞牌与全局引用。
    /// 打牌者位置 + 被鸣牌张优先取 NormalGameStateManager 由 action_tick 回查得到的
    /// currentMeldDiscarderPos / currentMeldClaimedTileId（乱序下比 lastDiscardPlayerPosition 可靠），
    /// 回退到 lastDiscardPlayerPosition / currentAskCutTileId 兼容回放/边界。
    /// </summary>
    private void TryReturnLastCutTileForMeld(string actionType, string discarderPosOverride = null, int claimedTileOverride = 0) {
        if (actionType == "jiagang" || actionType == "angang") {
            return;
        }
        string discarderPos = null;
        int claimedTile = 0;
        // 优先用派发时捕获的 override（延迟动画下避免共享字段被后续鸣牌覆盖）；
        // 否则回退到 NormalGameStateManager 的 currentMeld* / lastDiscardPlayerPosition。
        if (!string.IsNullOrEmpty(discarderPosOverride)) {
            discarderPos = discarderPosOverride;
            claimedTile = claimedTileOverride;
        }
        else if (NormalGameStateManager.Instance != null) {
            discarderPos = NormalGameStateManager.Instance.currentMeldDiscarderPos;
            claimedTile = NormalGameStateManager.Instance.currentMeldClaimedTileId;
            if (string.IsNullOrEmpty(discarderPos)) {
                discarderPos = NormalGameStateManager.Instance.lastDiscardPlayerPosition;
            }
            if (claimedTile <= 0) {
                claimedTile = NormalGameStateManager.Instance.currentAskCutTileId;
            }
        }

        GameObject obj = FindDiscardTileObject(discarderPos, claimedTile);
        if (obj == null) {
            // 回放/边界兜底：退回旧全局引用，保持兼容
            obj = lastCutJiagang3DObject;
        }
        if (obj == null) {
            return;
        }

        StopDiscardMoveCoroutine(discarderPos);
        MahjongObjectPool.Instance.Return(-1, obj);
        if (lastCutJiagang3DObject == obj) {
            lastCutJiagang3DObject = null;
        }
    }

    /// <summary>吃碰明杠等：回收河牌切子并启动副露动画（不进入暗杠手牌队列）。</summary>
    private void StartMeldPresentation(string actionType, string playerPosition, int[] combinationMask, string discarderPosOverride = null, int claimedTileOverride = 0) {
        TryReturnLastCutTileForMeld(actionType, discarderPosOverride, claimedTileOverride);
        StartCoroutine(ActionAnimationCoroutine(playerPosition, actionType, combinationMask, true));
    }

    private IEnumerator RecordDiscardShowCardsCoroutine(string playerPosition, int tileId, bool fromDrawSlot, bool isRiichi) {
        PosPanel3D panel = GetPosPanel(playerPosition);
        yield return RemoveRecordShowHandCardCoroutine(panel.ShowCardsPosition, tileId, fromDrawSlot, playerPosition);
        if (fromDrawSlot) {
            ClearRecordPlayerDrawSlotState(playerPosition);
        }
        bool moqieGrayOnDiscard = ShouldApplyMoqieDiscardGray(fromDrawSlot);
        yield return Set3DTileCoroutine(tileId, panel.discardsPosition, "Discard", playerPosition, moqieGrayOnDiscard, isRiichi: isRiichi);
        yield return RearrangeRecordShowMergeAllWithAnimation(panel.ShowCardsPosition, playerPosition);
    }

    private IEnumerator RecordBuhuaShowCardsCoroutine(string playerPosition, int tileId, bool fromDrawSlot) {
        PosPanel3D panel = GetPosPanel(playerPosition);
        yield return RemoveRecordShowHandCardCoroutine(panel.ShowCardsPosition, tileId, fromDrawSlot, playerPosition);
        if (fromDrawSlot) {
            ClearRecordPlayerDrawSlotState(playerPosition);
        }
        yield return Set3DTileCoroutine(tileId, panel.buhuaPosition, "Buhua", playerPosition);
        if (!fromDrawSlot) {
            yield return RearrangeRecordShowMergeAllWithAnimation(panel.ShowCardsPosition, playerPosition);
        }
    }

    private IEnumerator RecordMeldShowCardsCoroutine(
        string playerPosition,
        string actionType,
        int[] combinationMask,
        bool removeDrawSlotFirst = false,
        int drawSlotTileId = 0) {
        PosPanel3D panel = GetPosPanel(playerPosition);
        TryReturnLastCutTileForMeld(actionType);
        if (removeDrawSlotFirst) {
            yield return RemoveRecordShowHandCardCoroutine(panel.ShowCardsPosition, drawSlotTileId, fromDrawSlot: true, playerPosition);
            ClearRecordPlayerDrawSlotState(playerPosition);
        }
        yield return RemoveRecordShowHandCardsByMaskCoroutine(panel.ShowCardsPosition, combinationMask, playerPosition);
        yield return ActionAnimationCoroutine(playerPosition, actionType, combinationMask, true);
        ClearRecordPlayerDrawSlotState(playerPosition);
        yield return RearrangeRecordShowMergeAllWithAnimation(panel.ShowCardsPosition, playerPosition);
    }

    private void StartHandRearrange(string playerPosition) {
        PosPanel3D panel = GetPosPanel(playerPosition);
        if (panel != null) {
            StartCoroutine(Rearrange3DCardsWithAnimation(panel.cardsPosition));
        }
    }

    /// <returns>true 表示已处理（暗杠/加杠链或杠后摸牌），不再走 Change3DTileCoroutine。</returns>
    private bool TryEnqueueAnkanHandChange(string actionType, int tileId, int removeCount, string playerPosition, int[] combinationMask, bool isMoGang = false) {
        if (actionType == "angang") {
            int angangTileId = tileId;
            if (angangTileId < 2 && combinationMask != null && combinationMask.Length > 1) {
                angangTileId = combinationMask[1];
            }
            if (IsRecordShowCardsModeActive() && playerPosition != "self") {
                StartCoroutine(RecordMeldShowCardsCoroutine(playerPosition, actionType, combinationMask, isMoGang, angangTileId));
                return true;
            }
            _ankanPendingDrawByPlayer[playerPosition] = true;
            StartMeldPresentation(actionType, playerPosition, combinationMask);
            EnqueueAnkanHandWork(playerPosition, combinationMask, isMoGang, angangTileId);
            return true;
        }
        if (actionType == "jiagang") {
            int jiagangTileId = tileId >= 2 ? tileId : 0;
            if (jiagangTileId < 2 && combinationMask != null) {
                jiagangTileId = GameRecordMeldCodec.ExtractTileByFlag(combinationMask, 3) ?? jiagangTileId;
            }
            if (IsRecordShowCardsModeActive() && playerPosition != "self") {
                StartCoroutine(RecordMeldShowCardsCoroutine(playerPosition, actionType, combinationMask, isMoGang, jiagangTileId));
                return true;
            }
            _ankanPendingDrawByPlayer[playerPosition] = true;
            StartMeldPresentation(actionType, playerPosition, combinationMask);
            EnqueueJiagangHandWork(playerPosition, jiagangTileId, isMoGang);
            return true;
        }
        if (actionType == "GetCard" && _ankanPendingDrawByPlayer[playerPosition]) {
            if (IsRecordShowCardsModeActive() && playerPosition != "self") {
                StartCoroutine(RecordShowCardGetCoroutine(playerPosition, tileId));
                _ankanPendingDrawByPlayer[playerPosition] = false;
                return true;
            }
            _ankanPendingDrawByPlayer[playerPosition] = false;
            EnqueueGangDraw(playerPosition, tileId);
            return true;
        }
        return false;
    }
}

