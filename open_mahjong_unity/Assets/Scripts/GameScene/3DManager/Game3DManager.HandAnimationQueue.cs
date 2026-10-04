using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 暗杠/加杠手牌删收拢与杠后岭上摸牌走各家串行队列，保证「删牌 → 收拢 → 摸牌」顺序正确。
/// 所有出牌（含普通出牌、牌谱与多张切）从第一张起入队，完整落河、收拢后再出下一张。
/// 自由模式的结构变更按座位串行；空闲时摸牌立即入手，短收拢动画追随最新布局。
/// 队列空闲时的普通摸牌 / 吃碰明杠等仍走 Change3DTileCoroutine，各家的队列互不阻塞。
/// </summary>
public partial class Game3DManager {
    private enum HandAnimOpKind {
        RemoveCards,
        Rearrange,
        DrawCard,
        DiscardTile,
        BuhuaTile,
        FreeMeld,
        FreeClaimRiver,
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
        public bool ConcealedDiscard;
        public bool MergeBeforeDraw;
        public int MeldIndex;
        public int[] HandTiles;
        public HandAnimOp RiverClaim;
        public bool Completed;
    }

    private static readonly string[] HandAnimPlayerPositions = { "self", "left", "top", "right" };

    private readonly Dictionary<string, Queue<HandAnimOp>> _handAnimQueues = new Dictionary<string, Queue<HandAnimOp>> {
        { "self", new Queue<HandAnimOp>() },
        { "left", new Queue<HandAnimOp>() },
        { "top", new Queue<HandAnimOp>() },
        { "right", new Queue<HandAnimOp>() },
    };

    private readonly Dictionary<string, Coroutine> _handAnimProcessors = new Dictionary<string, Coroutine>();
    private int _handAnimationGeneration;

    private int _recordHandAnimationCount;
    private int _recordHandAnimationGeneration;
    private readonly Dictionary<string, int> _recordHandAnimationsByPlayer = new Dictionary<string, int>();

    /// <summary>牌谱明牌的删牌、飞牌和收拢尚未结束；自动播放须等它们完成再修改下一笔手牌状态。</summary>
    public bool HasPendingRecordHandAnimations => _recordHandAnimationCount > 0;

    public bool HasPendingRecordStepAnimations(string playerPosition) {
        return HasPendingHandAnimWork(playerPosition)
            || (_recordHandAnimationsByPlayer.TryGetValue(playerPosition, out int count) && count > 0);
    }

    public bool HasPendingRecordTableAnimations {
        get {
            foreach (string position in HandAnimPlayerPositions) {
                if (HasPendingRecordStepAnimations(position)) return true;
            }
            return false;
        }
    }

    private IEnumerator TrackRecordHandAnimation(IEnumerator animation, string playerPosition) {
        int generation = _recordHandAnimationGeneration;
        _recordHandAnimationCount++;
        _recordHandAnimationsByPlayer.TryGetValue(playerPosition, out int count);
        _recordHandAnimationsByPlayer[playerPosition] = count + 1;
        try {
            yield return animation;
        } finally {
            if (generation == _recordHandAnimationGeneration) {
                _recordHandAnimationCount--;
                _recordHandAnimationsByPlayer[playerPosition]--;
            }
        }
    }

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
        foreach (string position in HandAnimPlayerPositions) StopFreeDrawReflow(position, finish: false);
        _handAnimationGeneration++;
        // 跳转/切局会中断嵌套动画；旧协程的 finally 不得扣减新一轮播放的计数。
        _recordHandAnimationGeneration++;
        _recordHandAnimationCount = 0;
        _recordHandAnimationsByPlayer.Clear();
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
        int generation = _handAnimationGeneration;
        Queue<HandAnimOp> queue = _handAnimQueues[playerPosition];
        while (generation == _handAnimationGeneration && queue.Count > 0) {
            HandAnimOp op = queue.Dequeue();
            yield return ExecuteHandAnimOp(op);
        }
        if (generation == _handAnimationGeneration) _handAnimProcessors[playerPosition] = null;
    }

    private IEnumerator ExecuteHandAnimOp(HandAnimOp op) {
        PosPanel3D panel = GetPosPanel(op.PlayerPosition);
        if (panel == null) {
            yield break;
        }
        if (op.Kind != HandAnimOpKind.DrawCard || !op.MergeBeforeDraw)
            StopFreeDrawReflow(op.PlayerPosition, finish: true);

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
                if (op.MergeBeforeDraw) {
                    PlayFreeDrawReflow(op.PlayerPosition);
                    break;
                }
                yield return Get3DTileCoroutine(op.PlayerPosition, "get");
                break;
            case HandAnimOpKind.DiscardTile:
                yield return DiscardTileFromQueue(panel, op);
                break;
            case HandAnimOpKind.BuhuaTile:
                yield return Change3DTileCoroutine("Buhua", op.TileId, 0, op.PlayerPosition, op.CutClass, null);
                break;
            case HandAnimOpKind.FreeMeld:
                yield return FreeMeldFromQueue(panel, op);
                break;
            case HandAnimOpKind.FreeClaimRiver:
                yield return ReturnLastCutTileForMeldCoroutine(FreeActionWords.Meld, op.PlayerPosition, op.TileId);
                op.Completed = true;
                break;
        }
    }

    /// <summary>仅为服务器已确认的新副露播放动画；固定本次组号，连续广播不会串到末组。</summary>
    public void PlayFreeMeld(string playerPosition, int[] mask, int meldIndex, int[] handTiles,
        string discarderPosition, int claimedTile) {
        if (mask == null || handTiles == null || meldIndex < 0) return;
        if (!IsHandAnimPlayer(playerPosition)) return;
        var op = new HandAnimOp {
            Kind = HandAnimOpKind.FreeMeld,
            CombinationMask = (int[])mask.Clone(),
            MeldIndex = meldIndex,
            HandTiles = (int[])handTiles.Clone(),
        };
        // 认牌也排进出牌者队列：在其上一张落河后、下一张出牌前回收，避免自由操作覆盖末张登记。
        if (IsHandAnimPlayer(discarderPosition) && claimedTile > 0) {
            op.RiverClaim = new HandAnimOp { Kind = HandAnimOpKind.FreeClaimRiver, TileId = claimedTile };
            EnqueueHandAnimOp(discarderPosition, op.RiverClaim);
        }
        EnqueueHandAnimOp(playerPosition, op);
    }

    private IEnumerator FreeMeldFromQueue(PosPanel3D panel, HandAnimOp op) {
        int generation = _handAnimationGeneration;
        while (op.RiverClaim != null && !op.RiverClaim.Completed) {
            yield return null;
            if (generation != _handAnimationGeneration) yield break;
        }

        // 自家、推倒明牌按实际牌值移除；暗手只扣数量。朝向 1 也可以是自家选中的横牌。
        bool revealed = false;
        if (FreeGameState.Active != null) {
            foreach (var player in TableMirror.Current.IndexToPosition) {
                if (player.Value == op.PlayerPosition) {
                    FreeGameState.Active.Revealed.TryGetValue(player.Key, out revealed);
                    break;
                }
            }
        }
        if (op.PlayerPosition == "self" || revealed) {
            // 自家正常立牌只显示 2D 手牌，推倒后才有 cardsPosition 下的明牌。
            if (panel.cardsPosition.childCount > 0)
                foreach (int tile in op.HandTiles) RemoveOneSelfHandCardByTileId(panel.cardsPosition, tile, op.PlayerPosition);
        } else if (op.HandTiles.Length > 0) {
            yield return RemoveHandCardsCoroutine(panel.cardsPosition, op.HandTiles.Length, false, 0, null, true, op.PlayerPosition);
            if (generation != _handAnimationGeneration) yield break;
        }

        yield return ActionAnimationCoroutine(op.PlayerPosition, FreeActionWords.Meld, op.CombinationMask, true, op.MeldIndex);
        if (generation != _handAnimationGeneration) yield break;
        yield return Rearrange3DCardsWithAnimation(panel.cardsPosition);
    }

    /// <summary>自由模式的他家摸牌、切牌和补花共用队列，避免旧摸牌尚未收拢就增删下一张。</summary>
    private bool TryEnqueueFreeHandChange(string actionType, int tileId, string playerPosition,
        bool cutClass, bool isRiichi, bool playCutPhysicsSound, bool concealedDiscard = false) {
        if (FreeGameState.Active == null || playerPosition == "self" || !IsHandAnimPlayer(playerPosition)
                || IsRecordShowCardsModeActive()) return false;

        switch (actionType) {
            case "GetCard":
                if (!HasPendingHandAnimWork(playerPosition)) {
                    PlayFreeDrawReflow(playerPosition);
                    return true;
                }
                EnqueueHandAnimOp(playerPosition, new HandAnimOp {
                    Kind = HandAnimOpKind.DrawCard,
                    TileId = tileId,
                    MergeBeforeDraw = true,
                });
                return true;
            case "Discard":
                EnqueueDiscardHandWork(playerPosition, tileId, cutClass, isRiichi, playCutPhysicsSound);
                return true;
            case "Buhua":
                EnqueueHandAnimOp(playerPosition, new HandAnimOp {
                    Kind = HandAnimOpKind.BuhuaTile,
                    TileId = tileId,
                    CutClass = cutClass,
                });
                return true;
            default:
                return false;
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
            yield return RecordDiscardShowCardsCoroutine(op.PlayerPosition, op.TileId, op.CutClass, op.IsRiichi, op.ConcealedDiscard);
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
        yield return Set3DTileCoroutine(op.ConcealedDiscard ? 0 : op.TileId, panel.discardsPosition, "Discard", op.PlayerPosition, moqieGrayOnDiscard, isRiichi: op.IsRiichi);
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

    private void EnqueueDiscardHandWork(string playerPosition, int tileId, bool cutClass, bool isRiichi, bool playCutPhysicsSound, bool concealedDiscard = false) {
        EnqueueHandAnimOp(playerPosition, new HandAnimOp {
            Kind = HandAnimOpKind.DiscardTile,
            TileId = tileId,
            RemoveCount = 1,
            CutClass = cutClass,
            IsRiichi = isRiichi,
            PlayCutPhysicsSound = playCutPhysicsSound,
            ConcealedDiscard = concealedDiscard,
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
    /// 虹雀杠只把一张手牌并入既有副露：串行删除该张并收拢手牌；
    /// 副露本身由 HongqueGameState 按权威增量重建，这里不播放通用加杠且不安排杠后摸牌。
    /// </summary>
    public void RemoveHongqueKongHandTile(string playerPosition, int tileId) {
        if (!IsHandAnimPlayer(playerPosition) || tileId <= 0) return;
        EnqueueHandAnimOp(playerPosition, new HandAnimOp {
            Kind = HandAnimOpKind.RemoveCards,
            RemoveCount = 1,
            CutClass = false,
            TileId = tileId,
        });
        EnqueueHandAnimOp(playerPosition, new HandAnimOp {
            Kind = HandAnimOpKind.Rearrange,
        });
    }

    /// <summary>
    /// 打牌者 cut 若仍卡在手牌动画队列，RegisterLastDiscard 会晚于紧随的 peng。
    /// 鸣牌回收最多等这么久，避免逻辑河已删、3D 河牌残留。
    /// </summary>
    private const float MeldRiverRegisterWaitSec = 2f;

    /// <summary>
    /// 吃碰明杠：回收 RegisterLastDiscard 登记的河牌切子（加杠/暗杠除外）。
    /// discarderPos / claimedTile 须由调用方显式传入（对局 cut_from_player/cut_tile，牌谱 tick 推断）。
    /// 若 cut 仍在打牌者手牌队列中尚未登记，则等待登记完成后再回收；超时仍认不到则 Warning 放弃。
    /// </summary>
    private IEnumerator ReturnLastCutTileForMeldCoroutine(
        string actionType,
        string discarderPosOverride = null,
        int claimedTileOverride = 0) {
        int generation = _handAnimationGeneration;
        if (actionType == "jiagang" || actionType == "angang") {
            yield break;
        }

        string discarderPos = discarderPosOverride;
        int claimedTile = claimedTileOverride;
        if (string.IsNullOrEmpty(discarderPos) || claimedTile <= 0) {
            Debug.LogError(
                $"ReturnLastCutTileForMeld: 缺少显式打牌者/牌张 action={actionType}, "
                + $"discarder={discarderPos}, claimed={claimedTile}");
            yield break;
        }

        GameObject obj = ResolveLastDiscardObject(discarderPos, claimedTile);
        float elapsed = 0f;
        int idleFrames = 0;
        while (obj == null && elapsed < MeldRiverRegisterWaitSec) {
            // 打牌者仍有手牌队列工作时，cut 的 Discard 可能尚未跑到 Register。
            bool pendingCut = !string.IsNullOrEmpty(discarderPos) && HasPendingHandAnimWork(discarderPos);
            if (!pendingCut) {
                idleFrames++;
                // 无待办且已空等过一帧：直路径 cut 早应登记完，属真失败，不再空耗。
                if (idleFrames > 1) {
                    break;
                }
            } else {
                idleFrames = 0;
            }
            yield return null;
            if (generation != _handAnimationGeneration) yield break;
            elapsed += Time.unscaledDeltaTime;
            obj = ResolveLastDiscardObject(discarderPos, claimedTile);
        }

        if (obj == null) {
            int regTile = !string.IsNullOrEmpty(discarderPos)
                && _lastDiscardTileIdByPlayer.TryGetValue(discarderPos, out int t) ? t : 0;
            Debug.LogWarning(
                $"ReturnLastCutTileForMeld: 未认到登记弃牌，放弃河牌回收 action={actionType}, "
                + $"discarder={discarderPos}, claimed={claimedTile}, regTile={regTile}, "
                + $"waited={elapsed:F2}s, pending={(!string.IsNullOrEmpty(discarderPos) && HasPendingHandAnimWork(discarderPos))}");
            yield break;
        }

        // 停掉该打牌者的飞牌协程并清登记，避免被认走的牌仍在飞行/落到河里或被重复认走。
        OnLastDiscardTaken(discarderPos);
        MahjongObjectPool.Instance.Return(-1, obj);
    }

    /// <summary>吃碰明杠等：回收河牌切子并启动副露动画（不进入暗杠手牌队列）。</summary>
    private void StartMeldPresentation(string actionType, string playerPosition, int[] combinationMask, string discarderPosOverride = null, int claimedTileOverride = 0) {
        StartCoroutine(MeldPresentationCoroutine(actionType, playerPosition, combinationMask, discarderPosOverride, claimedTileOverride));
    }

    private IEnumerator MeldPresentationCoroutine(
        string actionType,
        string playerPosition,
        int[] combinationMask,
        string discarderPosOverride,
        int claimedTileOverride) {
        yield return ReturnLastCutTileForMeldCoroutine(actionType, discarderPosOverride, claimedTileOverride);
        yield return ActionAnimationCoroutine(playerPosition, actionType, combinationMask, true);
    }

    private IEnumerator RecordDiscardShowCardsCoroutine(string playerPosition, int tileId, bool fromDrawSlot, bool isRiichi, bool concealedDiscard = false) {
        return TrackRecordHandAnimation(RecordDiscardShowCardsCore(playerPosition, tileId, fromDrawSlot, isRiichi, concealedDiscard), playerPosition);
    }

    private IEnumerator RecordDiscardShowCardsCore(string playerPosition, int tileId, bool fromDrawSlot, bool isRiichi, bool concealedDiscard = false) {
        PosPanel3D panel = GetPosPanel(playerPosition);
        yield return RemoveRecordShowHandCardCoroutine(panel.ShowCardsPosition, tileId, fromDrawSlot, playerPosition);
        if (fromDrawSlot) {
            ClearRecordPlayerDrawSlotState(playerPosition);
        }
        bool moqieGrayOnDiscard = ShouldApplyMoqieDiscardGray(fromDrawSlot);
        yield return Set3DTileCoroutine(concealedDiscard ? 0 : tileId, panel.discardsPosition, "Discard", playerPosition, moqieGrayOnDiscard, isRiichi: isRiichi);
        yield return RearrangeRecordShowMergeAllWithAnimation(panel.ShowCardsPosition, playerPosition);
    }

    private IEnumerator RecordBuhuaShowCardsCoroutine(string playerPosition, int tileId, bool fromDrawSlot) {
        return TrackRecordHandAnimation(RecordBuhuaShowCardsCore(playerPosition, tileId, fromDrawSlot), playerPosition);
    }

    private IEnumerator RecordBuhuaShowCardsCore(string playerPosition, int tileId, bool fromDrawSlot) {
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
        return TrackRecordHandAnimation(RecordMeldShowCardsCore(
            playerPosition, actionType, combinationMask, removeDrawSlotFirst, drawSlotTileId, discarderPos, claimedTile), playerPosition);
    }

    private IEnumerator RecordMeldShowCardsCore(
        string playerPosition,
        string actionType,
        int[] combinationMask,
        bool removeDrawSlotFirst,
        int drawSlotTileId,
        string discarderPos,
        int claimedTile) {
        PosPanel3D panel = GetPosPanel(playerPosition);
        // 透传 discarder+tile：回放路径不写 currentMeldDiscarderPos/lastDiscardPlayerPosition，
        // 必须显式传入才能正确认走「该家最新弃牌」并停掉其飞牌协程，避免被鸣牌仍落到河里。
        yield return ReturnLastCutTileForMeldCoroutine(actionType, discarderPos, claimedTile);
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
