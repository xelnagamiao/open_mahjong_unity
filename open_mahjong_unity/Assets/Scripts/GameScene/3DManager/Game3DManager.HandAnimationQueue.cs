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
            yield return RemoveSelfHandCardsCoroutine(cardPosition, op.RemoveCount, op.CutClass, op.TileId, op.CombinationMask, skipRearrange: true);
        }
        else {
            yield return RemoveHandCardsCoroutine(cardPosition, op.RemoveCount, op.CutClass, op.TileId, op.CombinationMask, skipRearrange: true);
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

    /// <summary>吃碰明杠等：被鸣牌来自河牌时回收 lastCutJiagang3DObject（加杠/暗杠除外）。</summary>
    private void TryReturnLastCutTileForMeld(string actionType) {
        if (lastCutJiagang3DObject == null || actionType == "jiagang" || actionType == "angang") {
            return;
        }
        if (_currentDiscardMoveCoroutine != null) {
            StopCoroutine(_currentDiscardMoveCoroutine);
            _currentDiscardMoveCoroutine = null;
        }
        MahjongObjectPool.Instance.Return(-1, lastCutJiagang3DObject);
        lastCutJiagang3DObject = null;
    }

    /// <summary>吃碰明杠等：回收河牌切子并启动副露动画（不进入暗杠手牌队列）。</summary>
    private void StartMeldPresentation(string actionType, string playerPosition, int[] combinationMask) {
        TryReturnLastCutTileForMeld(actionType);
        StartCoroutine(ActionAnimationCoroutine(playerPosition, actionType, combinationMask, true));
    }

    private IEnumerator RecordDiscardShowCardsCoroutine(string playerPosition, int tileId, bool fromDrawSlot, bool isRiichi) {
        PosPanel3D panel = GetPosPanel(playerPosition);
        yield return RemoveRecordShowHandCardCoroutine(panel.ShowCardsPosition, tileId, fromDrawSlot);
        if (fromDrawSlot) {
            ClearRecordPlayerDrawSlotState(playerPosition);
        }
        bool moqieGrayOnDiscard = ShouldApplyMoqieDiscardGray(fromDrawSlot);
        yield return Set3DTileCoroutine(tileId, panel.discardsPosition, "Discard", playerPosition, moqieGrayOnDiscard, isRiichi: isRiichi);
        yield return RearrangeRecordShowMergeAllWithAnimation(panel.ShowCardsPosition, playerPosition);
    }

    private IEnumerator RecordBuhuaShowCardsCoroutine(string playerPosition, int tileId, bool fromDrawSlot) {
        PosPanel3D panel = GetPosPanel(playerPosition);
        yield return RemoveRecordShowHandCardCoroutine(panel.ShowCardsPosition, tileId, fromDrawSlot);
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
            yield return RemoveRecordShowHandCardCoroutine(panel.ShowCardsPosition, drawSlotTileId, fromDrawSlot: true);
            ClearRecordPlayerDrawSlotState(playerPosition);
        }
        yield return RemoveRecordShowHandCardsByMaskCoroutine(panel.ShowCardsPosition, combinationMask);
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

