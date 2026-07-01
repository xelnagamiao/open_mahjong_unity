using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// 2D 手牌拖拽理牌：以 activeGapIndex 为唯一显示状态（所见即所得）。
/// 指针在手牌行上才更新空位并播放让位动画，离开行时冻结空位；松手吸附到当前空位并提交。
/// 吸附目标与提交布局共用同一套累加计算，避免坐标偏差。
/// </summary>
public class HandCardDragController : MonoBehaviour {
    public static HandCardDragController Instance { get; private set; }
    public static int BlockTileClickUntilFrame { get; private set; } = -1;

    public static void MarkTileClickHandledThisFrame() {
        BlockTileClickUntilFrame = Time.frameCount;
    }
    public static bool IsDragging { get; private set; }
    public static bool SuppressPointerHover { get; private set; }

    [SerializeField] private RectTransform discardDropZone;
    [SerializeField] private float dragLiftY = 24f;
    [SerializeField] private float clickMoveThreshold = 6f;
    [SerializeField] private float dragActivationDelay = 0.12f;
    [SerializeField] private float gapShiftAnimDuration = 0.15f;
    [SerializeField] private float snapAnimDuration = 0.2f;
    // 误入拖拽会话的"微点击"判定：位移与按压时长都很小且顺序未变时按点击出牌处理
    [SerializeField] private float microClickMaxDistance = 16f;
    [SerializeField] private float microClickMaxDuration = 0.35f;

    private GameCanvas gameCanvas;
    private TileCard dragCard;
    private RectTransform dragCardRect;
    private float dragCardWidth;
    private Vector2 dragOffsetLocal;
    private Vector2 dragStartScreenPos;
    private Camera pointerPressCamera;
    private bool pendingPress;
    private bool dragSessionActive;
    private bool isFinishingDrag;
    private bool hadPointerMove;
    private bool wasArmedAtPress;   // 按下瞬间该牌是否处于"确认出牌"选中态（拖拽会话会清除选中，微点击提交时需还原）
    private int activeGapIndex = -1;
    private bool activeMergeDraw;
    private float pressStartUnscaledTime;
    private float handRowHalfHeight;

    // 看门狗：物理指针抬起却仍处于按压/拖拽（漏掉 OnPointerUp、触摸丢失等）后强制复位的依据
    private float lastPointerDownUnscaledTime;
    private float finishStartUnscaledTime;
    // 物理指针抬起后允许的宽限（避免与同帧到来的合法 OnPointerUp 抢跑）
    private const float LostPointerGraceSeconds = 0.2f;
    // 收尾动画最长存活时间，远大于实际动画时长，仅用于协程异常中断后的兜底复位
    private const float FinishWatchdogTimeoutSeconds = 1.5f;

    // 按下瞬间的快照：用于原始顺序、分界判定与复位
    private List<TileCard> snapshotOrder;          // 全部手牌按 handSortIndex 升序（含摸牌、含拖拽牌）
    private List<float> snapshotCenterXs;          // 与 snapshotOrder 对应的中心 X
    private int snapshotDragIndex;                 // 拖拽牌在 snapshotOrder 中的索引
    private int snapshotMainGapIndex;              // 拖拽牌在 layoutMain 中的空位索引
    private Dictionary<TileCard, int> snapshotSortIndex;
    private Dictionary<TileCard, bool> snapshotPinned;

    private Canvas dragCanvas;
    private Coroutine gapAnimCoroutine;
    private Coroutine finishDragCoroutine;
    // 收尾协程独占的牌与参数（与 dragCard 实例字段分离，避免连拖竞态）
    private TileCard finishingCard;
    private bool finishingIsCommit;
    private int finishingInsertIndex;
    private bool finishingMergeDraw;

    private void Awake() {
        Instance = this;
        gameCanvas = GetComponent<GameCanvas>();
    }

    private void OnDestroy() {
        if (Instance == this) {
            Instance = null;
        }
    }

    private void Update() {
        // 看门狗1：按住/拖拽中的卡牌被销毁（补花、出牌移除等）时不会再收到 OnPointerUp，
        // 若不复位会让 IsDragging/SuppressPointerHover 永久卡住，表现为"鼠标放在牌上不再弹起"。
        if ((pendingPress || dragSessionActive) && !isFinishingDrag && dragCardRect == null) {
            AbortPressSession("按压中的卡牌已销毁");
            return;
        }
        // 看门狗2：物理指针已抬起但仍处于按压/拖拽会话（漏掉 OnPointerUp、触摸丢失、左右键同时按等），
        // 否则 dragCard 会被永久绑定，点击它只会走拖拽收尾分支而无法出牌（需重连才能恢复）。
        if ((pendingPress || dragSessionActive) && !isFinishingDrag) {
            if (IsPhysicalPointerDown()) {
                lastPointerDownUnscaledTime = Time.unscaledTime;
            } else if (Time.unscaledTime - lastPointerDownUnscaledTime > LostPointerGraceSeconds) {
                AbortPressSession("指针已抬起但拖拽状态未复位");
                return;
            }
        }
        // 看门狗3：收尾动画因卡牌被并发销毁/重排而中断、未能跑到 ResetState 时，超时兜底复位。
        if (isFinishingDrag && Time.unscaledTime - finishStartUnscaledTime > FinishWatchdogTimeoutSeconds) {
            Debug.LogWarning("[HandInput] 收尾动画超时未结束，强制复位拖拽状态");
            CancelFinishingDrag(commitPendingReorder: true, relayout: true);
            ResetState();
            SuppressPointerHover = false;
            BlockTileClickUntilFrame = Time.frameCount;
            TileCard.ClearPendingPointerState();
            return;
        }
        if (!pendingPress || isFinishingDrag) {
            return;
        }
        if (!dragSessionActive) {
            if (ShouldActivate(Input.mousePosition)) {
                ActivateDragSession(Input.mousePosition, pointerPressCamera);
            }
            return;
        }
        UpdateDragCardScreenPosition(Input.mousePosition, pointerPressCamera);
    }

    private static bool IsPhysicalPointerDown() {
        if (Input.GetMouseButton(0)) {
            return true;
        }
        for (int i = 0; i < Input.touchCount; i++) {
            TouchPhase phase = Input.GetTouch(i).phase;
            if (phase != TouchPhase.Ended && phase != TouchPhase.Canceled) {
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// 外部（回合切换 / 右键摸切 / 清理输入残留）清理"指针已抬起但状态未复位"的悬挂按压/拖拽会话，
    /// 避免左右键同时点击等遗留把某张牌永久绑定为 dragCard 而无法出牌。
    /// 关键：指针仍按下时说明是玩家正在进行的合法拖拽（如拖拽出牌途中其他家出牌），绝不能中断，
    /// 否则会出现卡牌悬浮卡死、之后无法再拖拽的问题；漏掉松手的真实悬挂由 Update 看门狗2 兜底复位。
    /// </summary>
    public void AbortActivePress(string reason) {
        if (IsPhysicalPointerDown()) {
            return;
        }
        if (isFinishingDrag || finishDragCoroutine != null) {
            CancelFinishingDrag(commitPendingReorder: true, relayout: true);
            ResetState();
            SuppressPointerHover = false;
            BlockTileClickUntilFrame = Time.frameCount;
        }
        if (pendingPress || dragSessionActive) {
            AbortPressSession(reason);
        }
    }

    private void AbortPressSession(string reason) {
        Debug.LogWarning($"[HandInput] 强制复位拖拽输入状态 | 原因={reason}");
        StopGapAnimation();
        CancelFinishingDrag(commitPendingReorder: false, relayout: false);
        bool wasDragging = dragSessionActive;
        CleanupAllHandDragCanvases();
        ResetState();
        SuppressPointerHover = false;
        BlockTileClickUntilFrame = Time.frameCount;
        TileCard.ClearPendingPointerState();
        // 拖拽会话被异常中止时，按快照标记复位并按当前顺序重新布局，避免被拖拽牌悬浮/错位。
        if (wasDragging && gameCanvas != null && gameCanvas.HandCardsContainer != null
            && !gameCanvas.IsChangeHandCardProcessing) {
            try {
                RestoreSnapshotData();
                gameCanvas.LayoutHandCardsFromCurrentOrder();
            } catch (System.Exception e) {
                Debug.LogWarning($"[HandInput] 中止拖拽后复位布局失败: {e.Message}");
            }
        }
    }

    // 仅当指针实际移动超过阈值时才进入拖拽；停留不动（无论按住多久）一律视为点击，
    // 避免左击稍久即激活拖拽会话、松手复位导致"卡牌缩回、需再次点击才出牌"的不一致。
    private bool ShouldActivate(Vector2 screenPos) {
        return Vector2.Distance(screenPos, dragStartScreenPos) >= clickMoveThreshold;
    }

    public bool CanStartDrag() {
        if (gameCanvas.HandCardsContainer == null) {
            return false;
        }
        if (gameCanvas.IsHandRecordPlayback()) {
            return false;
        }
        return !gameCanvas.IsChangeHandCardProcessing;
    }

    public void OnPointerDown(TileCard card, PointerEventData eventData) {
        if (!CanStartDrag()) {
            return;
        }
        // 新拖拽按下：立刻结束上一段收尾（同步提交尚未落地的理牌），避免协程与实例字段竞态导致 Canvas 残留、两牌叠位。
        CancelFinishingDrag(commitPendingReorder: true, relayout: true);
        CleanupAllHandDragCanvases();
        if ((pendingPress || dragSessionActive) && dragCard != null && dragCard != card) {
            AbortPressSession("新牌按下打断旧按压");
        }
        dragCard = card;
        dragCardRect = card.GetComponent<RectTransform>();
        dragCardWidth = GameCanvas.GetCardWidth(dragCardRect);
        handRowHalfHeight = dragCardRect.rect.height * 0.5f;
        for (int i = 0; i < gameCanvas.HandCardsContainer.childCount; i++) {
            RectTransform rt = gameCanvas.HandCardsContainer.GetChild(i).GetComponent<RectTransform>();
            if (rt != null) {
                handRowHalfHeight = Mathf.Max(handRowHalfHeight, rt.rect.height * 0.5f);
            }
        }
        pendingPress = true;
        dragSessionActive = false;
        isFinishingDrag = false;
        hadPointerMove = false;
        wasArmedAtPress = HandCardSelectionController.Instance != null
            && HandCardSelectionController.Instance.IsArmed(card);
        dragStartScreenPos = eventData.position;
        pointerPressCamera = eventData.pressEventCamera;
        pressStartUnscaledTime = Time.unscaledTime;
        lastPointerDownUnscaledTime = Time.unscaledTime;
        TakeSnapshot();
    }

    private void ActivateDragSession(Vector2 screenPos, Camera eventCamera) {
        if (dragSessionActive) {
            return;
        }
        dragSessionActive = true;
        IsDragging = true;
        SuppressPointerHover = true;
        HandCardSelectionController.Instance?.DisarmAll();
        ClearAllHandHover();
        dragCard.transform.SetAsLastSibling();
        EnsureDragCanvas(dragCard);

        RectTransform container = gameCanvas.HandCardsContainerRect;
        RectTransformUtility.ScreenPointToLocalPointInRectangle(
            container, screenPos, eventCamera, out Vector2 localPoint);
        dragOffsetLocal = dragCardRect.anchoredPosition - localPoint;

        activeMergeDraw = false;
        ApplyGapLayout(snapshotMainGapIndex, false, false);
        UpdateDragCardScreenPosition(screenPos, eventCamera);
    }

    public void OnDrag(TileCard card, PointerEventData eventData) {
        if (!pendingPress || dragCard != card || isFinishingDrag) {
            return;
        }
        pointerPressCamera = eventData.pressEventCamera;
        if (!dragSessionActive) {
            if (ShouldActivate(eventData.position)) {
                ActivateDragSession(eventData.position, eventData.pressEventCamera);
            }
            return;
        }
        UpdateDragCardScreenPosition(eventData.position, eventData.pressEventCamera);
    }

    /// <returns>已消费本次按下（拖拽/短按），Relay 不再走通用出牌判定。</returns>
    public bool OnPointerUp(TileCard card, PointerEventData eventData) {
        if (!pendingPress || dragCard != card) {
            return false;
        }
        pendingPress = false;
        pointerPressCamera = eventData.pressEventCamera;

        // 未进入拖拽：短按且按下与松手在同一张牌上才可能出牌
        if (!dragSessionActive) {
            if (Vector2.Distance(eventData.position, dragStartScreenPos) < clickMoveThreshold) {
                TileCard.TryCommitClick(card, eventData.position);
            }
            ResetState();
            return true;
        }

        IsDragging = false;
        StopGapAnimation();
        RemoveDragCanvas(card);
        ClearAllHandHover();
        BlockTileClickUntilFrame = Time.frameCount;

        if (IsInDiscardZone(eventData.position, eventData.pressEventCamera)) {
            FinishDiscardImmediate(card);
            return true;
        }
        if (!OrderChanged(activeGapIndex, activeMergeDraw) && IsMicroClick(eventData.position)) {
            FinishMicroClickDiscard(card, eventData.position);
            return true;
        }
        if (OrderChanged(activeGapIndex, activeMergeDraw)) {
            StartFinishCoroutine(
                FinishCommitDrag(card, activeGapIndex, activeMergeDraw),
                card, isCommit: true, activeGapIndex, activeMergeDraw);
        }
        else {
            StartFinishCoroutine(FinishRevertDrag(card), card, isCommit: false);
        }
        return true;
    }

    private bool IsMicroClick(Vector2 releaseScreenPos) {
        return Vector2.Distance(releaseScreenPos, dragStartScreenPos) < microClickMaxDistance
            && Time.unscaledTime - pressStartUnscaledTime <= microClickMaxDuration;
    }

    /// <summary>
    /// 快速点击时指针轻微抖动（≥clickMoveThreshold）会误入拖拽会话，原逻辑松手只复位不出牌，
    /// 且会话激活时已 DisarmAll，确认模式下表现为"牌能浮起就是打不出去"。
    /// 顺序未变 + 位移小 + 按压短：立即复位布局并按点击提交（还原按下时的确认选中态）。
    /// </summary>
    private void FinishMicroClickDiscard(TileCard card, Vector2 releaseScreenPos) {
        MarkFinishingDrag();
        bool armed = wasArmedAtPress;
        RestoreSnapshotLayout();
        EndFinishDrag();
        if (armed) {
            HandCardSelectionController.Instance?.Arm(card);
        }
        Debug.Log($"[HandInput] 微点击按出牌处理 | tileId={card.tileId} | armedAtPress={armed}");
        TileCard.TryCommitClick(card, releaseScreenPos, skipSameCardCheck: true);
    }

    // 出牌区松手：立即出牌；仅收拢其余牌布局，打出牌保持松手时的位置，避免闪回槽位
    private void FinishDiscardImmediate(TileCard card) {
        MarkFinishingDrag();
        if (!CanTryDiscard(card)) {
            StartFinishCoroutine(FinishRevertDrag(card), card, isCommit: false);
            return;
        }
        StopGapAnimation();
        ApplyHandLayoutForDiscard(card);
        card.TriggerClick();
        EndFinishDrag();
    }

    private void ApplyHandLayoutForDiscard(TileCard discardCard) {
        List<TileCard> main;
        TileCard draw;
        if (OrderChanged(activeGapIndex, activeMergeDraw)) {
            main = BuildOrderForInsert(activeGapIndex, activeMergeDraw);
            draw = gameCanvas.GetPinnedDrawTile();
            gameCanvas.SetHandArranged(true);
            if (AutoAction.Instance != null && AutoAction.Instance.IsAutoArrangeHandCards) {
                AutoAction.Instance.SetAutoArrangeHandCards(false);
            }
        }
        else {
            RestoreSnapshotData();
            SplitSnapshot(out main, out draw);
        }
        gameCanvas.AnimateHandLayoutForDiscard(discardCard, main, draw);
    }

    private void StartFinishCoroutine(
        IEnumerator routine,
        TileCard card,
        bool isCommit,
        int insertIndex = 0,
        bool mergeDraw = false) {
        CancelFinishingDrag(commitPendingReorder: true, relayout: false);
        finishingCard = card;
        finishingIsCommit = isCommit;
        finishingInsertIndex = insertIndex;
        finishingMergeDraw = mergeDraw;
        finishDragCoroutine = StartCoroutine(RunFinishCoroutine(routine));
    }

    private IEnumerator RunFinishCoroutine(IEnumerator routine) {
        yield return routine;
        finishDragCoroutine = null;
        finishingCard = null;
        finishingIsCommit = false;
    }

    /// <summary>
    /// 中止收尾协程。commitPendingReorder=true 时把未播完的理牌顺序同步落地，避免 handSortIndex 与视觉脱节。
    /// </summary>
    private void CancelFinishingDrag(bool commitPendingReorder, bool relayout) {
        if (finishDragCoroutine != null) {
            StopCoroutine(finishDragCoroutine);
            finishDragCoroutine = null;
        }
        StopGapAnimation();
        TileCard card = finishingCard;
        bool isCommit = finishingIsCommit;
        int insertIndex = finishingInsertIndex;
        bool mergeDraw = finishingMergeDraw;
        finishingCard = null;
        finishingIsCommit = false;
        isFinishingDrag = false;

        if (card != null && commitPendingReorder) {
            if (isCommit) {
                dragCard = card;
                dragCardRect = card.GetComponent<RectTransform>();
                CommitReorder(insertIndex, mergeDraw);
            }
            else {
                RestoreSnapshotLayout();
            }
        }
        else if (relayout && gameCanvas != null && gameCanvas.HandCardsContainer != null
            && !gameCanvas.IsChangeHandCardProcessing) {
            try {
                gameCanvas.LayoutHandCardsFromCurrentOrder();
            }
            catch (System.Exception e) {
                Debug.LogWarning($"[HandInput] 收尾中止后重排失败: {e.Message}");
            }
        }

        CleanupAllHandDragCanvases();
    }

    private IEnumerator FinishCommitDrag(TileCard card, int insertIndex, bool mergeDraw) {
        MarkFinishingDrag();
        RectTransform cardRect = card.GetComponent<RectTransform>();
        dragCard = card;
        dragCardRect = cardRect;
        ApplyGapLayout(insertIndex, mergeDraw, false);   // 其余牌瞬间到位，避免提交时跳动
        yield return AnimateDragCardTo(cardRect, GetDragCardTargetPosition(insertIndex, mergeDraw));
        if (cardRect != null) {
            CommitReorder(insertIndex, mergeDraw);
        }
        EndFinishDrag();
    }

    private IEnumerator FinishRevertDrag(TileCard card) {
        MarkFinishingDrag();
        RectTransform cardRect = card.GetComponent<RectTransform>();
        RestoreSnapshotData();
        SplitSnapshot(out List<TileCard> main, out TileCard draw);
        Dictionary<RectTransform, Vector2> positions = gameCanvas.BuildHandLayoutPositions(main, draw, null);
        Vector2 dragTarget = positions.TryGetValue(cardRect, out Vector2 t) ? t : Vector2.zero;
        foreach (var kvp in positions) {
            if (kvp.Key != cardRect) {
                kvp.Key.anchoredPosition = kvp.Value;
            }
        }
        yield return AnimateDragCardTo(cardRect, dragTarget);
        if (cardRect != null) {
            gameCanvas.ApplyHandLayoutPositions(positions, main, draw);
        }
        EndFinishDrag();
    }

    private void MarkFinishingDrag() {
        isFinishingDrag = true;
        finishStartUnscaledTime = Time.unscaledTime;
    }

    private void EndFinishDrag() {
        CleanupAllHandDragCanvases();
        finishingCard = null;
        finishingIsCommit = false;
        finishDragCoroutine = null;
        ResetState();
        BlockTileClickUntilFrame = Time.frameCount;
        StartCoroutine(EnableHoverNextFrame());
    }

    private void ResetState() {
        dragCard = null;
        dragCardRect = null;
        pendingPress = false;
        dragSessionActive = false;
        isFinishingDrag = false;
        IsDragging = false;
        activeGapIndex = -1;
        activeMergeDraw = false;
        wasArmedAtPress = false;
    }

    private IEnumerator EnableHoverNextFrame() {
        yield return null;
        SuppressPointerHover = false;
        BlockTileClickUntilFrame = Time.frameCount;
    }

    private IEnumerator AnimateDragCardTo(RectTransform cardRect, Vector2 targetPos) {
        if (cardRect == null) {
            yield break;
        }
        Vector2 start = cardRect.anchoredPosition;
        targetPos.y = 0f;
        float elapsed = 0f;
        while (elapsed < snapAnimDuration) {
            if (cardRect == null) {
                yield break;
            }
            elapsed += Time.deltaTime;
            float p = Mathf.Clamp01(elapsed / snapAnimDuration);
            float smooth = 1f - Mathf.Pow(1f - p, 3f);
            cardRect.anchoredPosition = Vector2.Lerp(start, targetPos, smooth);
            yield return null;
        }
        if (cardRect != null) {
            cardRect.anchoredPosition = targetPos;
        }
    }

    private void UpdateDragCardScreenPosition(Vector2 screenPos, Camera eventCamera) {
        RectTransform container = gameCanvas.HandCardsContainerRect;
        RectTransformUtility.ScreenPointToLocalPointInRectangle(
            container, screenPos, eventCamera, out Vector2 localPoint);
        dragCardRect.anchoredPosition = localPoint + dragOffsetLocal + new Vector2(0f, dragLiftY);
        if (Vector2.Distance(screenPos, dragStartScreenPos) >= clickMoveThreshold) {
            hadPointerMove = true;
        }
        // 离开手牌行只冻结当前空位，不复位，避免松手时反复横跳
        if (!IsPointerOverHandRow(localPoint.x, localPoint.y)) {
            return;
        }
        float probeX = dragCardRect.anchoredPosition.x;
        ResolveInsert(probeX, out int insertIndex, out bool mergeDraw);
        if (insertIndex != activeGapIndex || mergeDraw != activeMergeDraw) {
            ApplyGapLayout(insertIndex, mergeDraw, true);
        }
    }

    private bool IsPointerOverHandRow(float localX, float localY) {
        if (localY < -handRowHalfHeight || localY > handRowHalfHeight) {
            return false;
        }
        if (snapshotCenterXs == null || snapshotCenterXs.Count == 0) {
            return true;
        }
        // 以快照行跨度为准（稳定），两端各放宽一张牌宽以便插到首尾
        float left = snapshotCenterXs[0] - dragCardWidth;
        float right = snapshotCenterXs[snapshotCenterXs.Count - 1] + dragCardWidth;
        return localX >= left && localX <= right;
    }

    private bool CanTryDiscard(TileCard card) {
        if (!card.IsSelectableForCut() || NormalGameStateManager.Instance == null) {
            return false;
        }
        if (RiichiCutSelectionController.Instance != null && RiichiCutSelectionController.Instance.IsActive) {
            return true;
        }
        return NormalGameStateManager.Instance.allowActionList.Contains("cut");
    }

    private bool IsInDiscardZone(Vector2 screenPos, Camera eventCamera) {
        if (discardDropZone == null) {
            return false;
        }
        return RectTransformUtility.RectangleContainsScreenPoint(discardDropZone, screenPos, eventCamera);
    }

    /// <summary>
    /// 用按下时快照相邻中心的中点分界（含拖拽牌，避免跨过空位合并分界）。
    /// 以拖拽牌中心 X 判定；摸牌张左半区冻结空位，越过中心可并入主列。
    /// </summary>
    private void ResolveInsert(float probeX, out int insertIndex, out bool mergeDraw) {
        mergeDraw = false;
        TileCard draw = gameCanvas.GetPinnedDrawTile();
        if (draw != null && draw != dragCard) {
            float drawCenter = GetSnapshotCenterX(draw);
            float drawLeft = drawCenter - GameCanvas.GetCardWidth(draw.GetComponent<RectTransform>()) * 0.5f;
            if (!dragCard.isDrawSlotPinned) {
                if (probeX >= drawLeft && probeX < drawCenter) {
                    insertIndex = activeGapIndex >= 0 ? activeGapIndex : snapshotMainGapIndex;
                    return;
                }
                mergeDraw = probeX >= drawCenter;
            }
        }

        List<float> centers = new List<float>();
        for (int i = 0; i < snapshotOrder.Count; i++) {
            TileCard tc = snapshotOrder[i];
            if (tc.isDrawSlotPinned && !mergeDraw && tc != dragCard) {
                continue;
            }
            centers.Add(snapshotCenterXs[i]);
        }
        int mainCount = BuildLayoutMainList(mergeDraw).Count;
        insertIndex = 0;
        for (int i = 0; i < centers.Count - 1; i++) {
            float boundary = (centers[i] + centers[i + 1]) * 0.5f;
            if (probeX > boundary) {
                insertIndex++;
            }
        }
        insertIndex = Mathf.Clamp(insertIndex, 0, mainCount);
    }

    private float GetSnapshotCenterX(TileCard card) {
        for (int i = 0; i < snapshotOrder.Count; i++) {
            if (snapshotOrder[i] == card) {
                return snapshotCenterXs[i];
            }
        }
        return card.GetComponent<RectTransform>().anchoredPosition.x;
    }

    /// <summary>拖拽牌插入 insertIndex 后的最终中心 X（与 CommitReorder 的布局完全一致）。</summary>
    private Vector2 GetDragCardTargetPosition(int insertIndex, bool mergeDraw) {
        List<TileCard> main = BuildLayoutMainList(mergeDraw);
        insertIndex = Mathf.Clamp(insertIndex, 0, main.Count);
        float x = 0f;
        for (int i = 0; i < insertIndex; i++) {
            x += GameCanvas.GetCardWidth(main[i].GetComponent<RectTransform>());
        }
        return new Vector2(x, 0f);
    }

    private List<TileCard> BuildLayoutMainList(bool mergeDraw) {
        List<TileCard> main = gameCanvas.GetMainHandCardsOrdered(dragCard);
        if (mergeDraw) {
            TileCard draw = gameCanvas.GetPinnedDrawTile();
            if (draw != null && !main.Contains(draw)) {
                main.Add(draw);
                main.Sort((a, b) => a.handSortIndex.CompareTo(b.handSortIndex));
            }
        }
        return main;
    }

    private void ApplyGapLayout(int gapIndex, bool mergeDraw, bool animate) {
        activeGapIndex = gapIndex;
        activeMergeDraw = mergeDraw;
        List<TileCard> layoutMain = BuildLayoutMainList(mergeDraw);
        TileCard layoutDraw = mergeDraw ? null : gameCanvas.GetPinnedDrawTile();
        // 若被拖拽的就是摸牌张：让位动画不再把它固定回摸牌区，由拖拽位置跟随指针。
        if (layoutDraw == dragCard) {
            layoutDraw = null;
        }
        Dictionary<RectTransform, Vector2> targets = gameCanvas.BuildHandLayoutWithGap(
            layoutMain, gapIndex, dragCardWidth, layoutDraw);
        if (animate) {
            StopGapAnimation();
            gapAnimCoroutine = gameCanvas.AnimateHandCardsToLayout(targets, gapShiftAnimDuration);
        }
        else {
            foreach (var kvp in targets) {
                kvp.Key.anchoredPosition = kvp.Value;
            }
        }
    }

    /// <summary>
    /// 出牌重排等全局手牌动画开始前调用，避免让位动画(0.15s)与重排动画(0.3s)并发导致中途瞬移。
    /// </summary>
    public void CancelGapLayoutAnimation() {
        StopGapAnimation();
    }

    private void StopGapAnimation() {
        if (gapAnimCoroutine != null) {
            gameCanvas.StopCoroutine(gapAnimCoroutine);
            gapAnimCoroutine = null;
        }
    }

    private void CommitReorder(int insertIndex, bool mergeDraw) {
        StopGapAnimation();
        List<TileCard> order = BuildOrderForInsert(insertIndex, mergeDraw);
        TileCard draw = gameCanvas.GetPinnedDrawTile();
        Dictionary<RectTransform, Vector2> positions = gameCanvas.BuildHandLayoutPositions(order, draw, null);
        gameCanvas.ApplyHandLayoutPositions(positions, order, draw);
        gameCanvas.SetHandArranged(true);
        if (AutoAction.Instance != null && AutoAction.Instance.IsAutoArrangeHandCards) {
            AutoAction.Instance.SetAutoArrangeHandCards(false);
        }
    }

    private List<TileCard> BuildOrderForInsert(int insertIndex, bool mergeDraw) {
        List<TileCard> main = gameCanvas.GetMainHandCardsOrdered(dragCard);
        TileCard draw = gameCanvas.GetPinnedDrawTile();
        // 仅清除"摸牌区固定标记"，保留 currentGetTile（摸切/手切判定仍需要）。
        if (mergeDraw && draw != null) {
            draw.isDrawSlotPinned = false;
            if (!main.Contains(draw)) {
                main.Add(draw);
            }
            main.Sort((a, b) => a.handSortIndex.CompareTo(b.handSortIndex));
        }
        if (dragCard.isDrawSlotPinned) {
            dragCard.isDrawSlotPinned = false;
        }
        insertIndex = Mathf.Clamp(insertIndex, 0, main.Count);
        main.Insert(insertIndex, dragCard);
        return main;
    }

    private bool OrderChanged(int insertIndex, bool mergeDraw) {
        return mergeDraw || insertIndex != snapshotMainGapIndex;
    }

    private void TakeSnapshot() {
        snapshotSortIndex = new Dictionary<TileCard, int>();
        snapshotPinned = new Dictionary<TileCard, bool>();
        snapshotOrder = new List<TileCard>();
        snapshotCenterXs = new List<float>();
        snapshotDragIndex = 0;
        snapshotMainGapIndex = 0;
        for (int i = 0; i < gameCanvas.HandCardsContainer.childCount; i++) {
            TileCard tc = gameCanvas.HandCardsContainer.GetChild(i).GetComponent<TileCard>();
            if (tc == null) {
                continue;
            }
            snapshotSortIndex[tc] = tc.handSortIndex;
            snapshotPinned[tc] = tc.isDrawSlotPinned;
            snapshotOrder.Add(tc);
        }
        snapshotOrder.Sort((a, b) => a.handSortIndex.CompareTo(b.handSortIndex));
        for (int i = 0; i < snapshotOrder.Count; i++) {
            TileCard tc = snapshotOrder[i];
            if (tc == dragCard) {
                snapshotDragIndex = i;
            }
            snapshotCenterXs.Add(tc.GetComponent<RectTransform>().anchoredPosition.x);
        }
        snapshotMainGapIndex = 0;
        for (int i = 0; i < snapshotDragIndex; i++) {
            if (!snapshotOrder[i].isDrawSlotPinned) {
                snapshotMainGapIndex++;
            }
        }
    }

    private void RestoreSnapshotData() {
        if (snapshotSortIndex == null) {
            return;
        }
        foreach (var kvp in snapshotSortIndex) {
            kvp.Key.handSortIndex = kvp.Value;
        }
        foreach (var kvp in snapshotPinned) {
            kvp.Key.isDrawSlotPinned = kvp.Value;
        }
    }

    private void RestoreSnapshotLayout() {
        RestoreSnapshotData();
        SplitSnapshot(out List<TileCard> main, out TileCard draw);
        Dictionary<RectTransform, Vector2> positions = gameCanvas.BuildHandLayoutPositions(main, draw, null);
        gameCanvas.ApplyHandLayoutPositions(positions, main, draw);
    }

    private void SplitSnapshot(out List<TileCard> main, out TileCard draw) {
        main = new List<TileCard>();
        draw = null;
        foreach (TileCard tc in snapshotOrder) {
            if (tc.isDrawSlotPinned) {
                draw = tc;
            }
            else {
                main.Add(tc);
            }
        }
        main.Sort((a, b) => a.handSortIndex.CompareTo(b.handSortIndex));
    }

    private void ClearAllHandHover() {
        TipsContainer.Instance.EndCutPreviewTips();
        if (Card3DHoverManager.Instance != null) {
            Card3DHoverManager.Instance.OnCardExit();
        }
        HandCardSelectionController.Instance?.DisarmAll();
        for (int i = 0; i < gameCanvas.HandCardsContainer.childCount; i++) {
            Transform child = gameCanvas.HandCardsContainer.GetChild(i);
            child.GetComponent<TileCard>()?.ForceExitHover();
            child.GetComponent<HoverEventTrigger>()?.ForceResetHover();
        }
    }

    private void EnsureDragCanvas(TileCard card) {
        CleanupAllHandDragCanvases();
        dragCanvas = card.gameObject.GetComponent<Canvas>();
        if (dragCanvas == null) {
            dragCanvas = card.gameObject.AddComponent<Canvas>();
        }
        dragCanvas.overrideSorting = true;
        dragCanvas.sortingOrder = 300;
    }

    private static bool IsDragSortCanvas(Canvas canvas) {
        return canvas != null && canvas.overrideSorting && canvas.sortingOrder == 300;
    }

    private void RemoveDragCanvas(TileCard card) {
        if (card == null) {
            return;
        }
        Canvas canvas = card.GetComponent<Canvas>();
        if (IsDragSortCanvas(canvas)) {
            Destroy(canvas);
        }
        if (dragCanvas != null && dragCanvas.gameObject == card.gameObject) {
            dragCanvas = null;
        }
    }

    private void CleanupAllHandDragCanvases() {
        if (gameCanvas == null || gameCanvas.HandCardsContainer == null) {
            dragCanvas = null;
            return;
        }
        for (int i = 0; i < gameCanvas.HandCardsContainer.childCount; i++) {
            TileCard tc = gameCanvas.HandCardsContainer.GetChild(i).GetComponent<TileCard>();
            RemoveDragCanvas(tc);
        }
        dragCanvas = null;
    }
}
