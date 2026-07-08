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
        DiscardTile,
    }

    private sealed class HandAnimOp {
        public HandAnimOpKind Kind;
        public string PlayerPosition;
        public int TileId;
        public int RemoveCount;
        public bool CutClass;
        public int[] CombinationMask;
        public bool IsRiichi;
        public bool PlayCutPhysicsSound;
        public float DelayBeforeStart;
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
    private const float BatchDiscardIntervalSec = 0.55f;

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

    private bool HasPendingHandAnimWork(string playerPosition) {
        if (!IsHandAnimPlayer(playerPosition)) {
            return false;
        }
        if (_handAnimQueues.TryGetValue(playerPosition, out Queue<HandAnimOp> queue) && queue.Count > 0) {
            return true;
        }
        return _handAnimProcessors.TryGetValue(playerPosition, out Coroutine running) && running != null;
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
        if (op.DelayBeforeStart > 0f) {
            yield return new WaitForSeconds(op.DelayBeforeStart);
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
            case HandAnimOpKind.DiscardTile:
                yield return DiscardTileFromQueue(panel, op);
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

    private IEnumerator DiscardTileFromQueue(PosPanel3D panel, HandAnimOp op) {
        if (IsRecordShowCardsModeActive() && op.PlayerPosition != "self") {
            yield return RecordDiscardShowCardsCoroutine(op.PlayerPosition, op.TileId, op.CutClass, op.IsRiichi);
            yield break;
        }

        if (IsSelfCardsPosition(panel.cardsPosition)) {
            yield return RemoveSelfHandCardsCoroutine(panel.cardsPosition, 1, op.CutClass, op.TileId, null, skipRearrange: true, op.PlayerPosition);
        }
        else {
            yield return RemoveHandCardsCoroutine(panel.cardsPosition, 1, op.CutClass, op.TileId, null, skipRearrange: true, op.PlayerPosition);
        }
        if (op.PlayCutPhysicsSound) {
            SoundManager.Instance.PlayPhysicsSound("cut");
        }
        bool moqieGrayOnDiscard = ShouldApplyMoqieDiscardGray(op.CutClass);
        yield return Set3DTileCoroutine(op.TileId, panel.discardsPosition, "Discard", op.PlayerPosition, moqieGrayOnDiscard, isRiichi: op.IsRiichi);
        if (op.PlayerPosition != "self" && DiscardSettlePauseSec > 0f) {
            yield return new WaitForSeconds(DiscardSettlePauseSec);
        }
        yield return Rearrange3DCardsWithAnimation(panel.cardsPosition);
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

    private void EnqueueDiscardHandWork(string playerPosition, int tileId, bool cutClass, bool isRiichi, bool playCutPhysicsSound, float delayBeforeStart = 0f) {
        EnqueueHandAnimOp(playerPosition, new HandAnimOp {
            Kind = HandAnimOpKind.DiscardTile,
            TileId = tileId,
            RemoveCount = 1,
            CutClass = cutClass,
            IsRiichi = isRiichi,
            PlayCutPhysicsSound = playCutPhysicsSound,
            DelayBeforeStart = delayBeforeStart,
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
    /// 吃碰明杠等：仅回收 RegisterLastDiscard 登记的河牌切子（加杠/暗杠除外）。
    /// 认不到登记对象则打 Warning 并放弃，不扫描河牌末张。
    /// </summary>
    private void TryReturnLastCutTileForMeld(string actionType, string discarderPosOverride = null, int claimedTileOverride = 0) {
        if (actionType == "jiagang" || actionType == "angang") {
            return;
        }
        string discarderPos = discarderPosOverride;
        int claimedTile = claimedTileOverride;

        if (string.IsNullOrEmpty(discarderPos) && NormalGameStateManager.Instance != null) {
            discarderPos = NormalGameStateManager.Instance.currentMeldDiscarderPos;
            if (string.IsNullOrEmpty(discarderPos)) {
                discarderPos = NormalGameStateManager.Instance.lastDiscardPlayerPosition;
            }
            if (claimedTile <= 0) claimedTile = NormalGameStateManager.Instance.currentMeldClaimedTileId;
            if (claimedTile <= 0) claimedTile = NormalGameStateManager.Instance.currentAskCutTileId;
        }

        GameObject obj = ResolveLastDiscardObject(discarderPos, claimedTile);
        if (obj == null) {
            int regTile = !string.IsNullOrEmpty(discarderPos)
                && _lastDiscardTileIdByPlayer.TryGetValue(discarderPos, out int t) ? t : 0;
            Debug.LogWarning(
                $"TryReturnLastCutTileForMeld: 未认到登记弃牌，放弃河牌回收 action={actionType}, "
                + $"discarder={discarderPos}, claimed={claimedTile}, regTile={regTile}");
            return;
        }

        // 停掉该打牌者的飞牌协程并清登记，避免被认走的牌仍在飞行/落到河里或被重复认走。
        OnLastDiscardTaken(discarderPos);
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
        int drawSlotTileId = 0,
        string discarderPos = null,
        int claimedTile = 0) {
        PosPanel3D panel = GetPosPanel(playerPosition);
        // 透传 discarder+tile：回放路径不写 currentMeldDiscarderPos/lastDiscardPlayerPosition，
        // 必须显式传入才能正确认走「该家最新弃牌」并停掉其飞牌协程，避免被鸣牌仍落到河里。
        TryReturnLastCutTileForMeld(actionType, discarderPos, claimedTile);
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
        if (actionType == "GetCard" && (_ankanPendingDrawByPlayer[playerPosition] || HasPendingHandAnimWork(playerPosition))) {
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

