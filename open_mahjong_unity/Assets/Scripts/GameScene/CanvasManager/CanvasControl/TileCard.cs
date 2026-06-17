using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// 麻将牌UI组件
/// 层级结构：
/// TileCard (空物体)
/// ├── fill（原槽位，仅点击出牌，不触发拖拽）
/// ├── TileImage (Image组件)
/// └── TileButton (Button组件，拖拽与点牌面)
/// </summary>
public class TileCard : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler {
    [Header("UI Components")]
    [SerializeField] private Image tileImage;    // 牌面图片组件
    [SerializeField] private Button tileButton;  // 按钮组件

    // 将私有字段改为公共属性
    public int tileId;   // 牌的ID（如"11"表示一万）
    public bool currentGetTile;   // 摸牌标记：仅用于服务端摸切/手切判定（拖入手牌排序后仍需保留）
    public bool isDrawSlotPinned;   // 固定标记：是否固定显示在独立摸牌区（手动理牌拖入主列后清除，不影响 currentGetTile）
    public int handSortIndex;   // 手牌排序位置，数值越大越靠右

    private bool isHovering = false; // 是否正在悬停
    private bool isSelectable = true;
    private static int lastHandledPointerFrame = -1;
    private static int lastGlobalPointerUpFrame = -1;
    private static TileCard pointerDownCard;
    private RectTransform slotHitRect;

    private void Awake() {
        if (tileButton != null) {
            TileCardDragRelay relay = tileButton.GetComponent<TileCardDragRelay>();
            if (relay == null) {
                relay = tileButton.gameObject.AddComponent<TileCardDragRelay>();
            }
            relay.Bind(this);
        }
        EnsureSlotHitArea();
    }

    private void EnsureSlotHitArea() {
        Transform slotTransform = transform.Find("fill");
        if (slotTransform == null) {
            slotTransform = transform.Find("SlotHitArea");
        }
        GameObject slotGo;
        if (slotTransform != null) {
            slotGo = slotTransform.gameObject;
            if (slotTransform.name == "fill") {
                Transform legacySlot = transform.Find("SlotHitArea");
                if (legacySlot != null) {
                    Destroy(legacySlot.gameObject);
                }
            }
        }
        else if (tileButton == null) {
            return;
        }
        else {
            RectTransform btnRt = tileButton.GetComponent<RectTransform>();
            slotGo = new GameObject("fill", typeof(RectTransform), typeof(Image), typeof(TileCardSlotClickRelay));
            slotGo.transform.SetParent(transform, false);
            slotGo.transform.SetAsFirstSibling();
            RectTransform slotRt = slotGo.GetComponent<RectTransform>();
            slotRt.anchorMin = btnRt.anchorMin;
            slotRt.anchorMax = btnRt.anchorMax;
            slotRt.pivot = btnRt.pivot;
            slotRt.sizeDelta = btnRt.sizeDelta;
            slotRt.anchoredPosition = btnRt.anchoredPosition;
            Image img = slotGo.GetComponent<Image>();
            img.color = Color.clear;
            img.raycastTarget = true;
        }
        Image slotImage = slotGo.GetComponent<Image>();
        if (slotImage == null) {
            slotImage = slotGo.AddComponent<Image>();
            slotImage.color = Color.clear;
        }
        slotImage.raycastTarget = true;
        slotHitRect = slotGo.GetComponent<RectTransform>();
        TileCardSlotClickRelay slotRelay = slotGo.GetComponent<TileCardSlotClickRelay>();
        if (slotRelay == null) {
            slotRelay = slotGo.AddComponent<TileCardSlotClickRelay>();
        }
        slotRelay.Bind(this);
    }

    private Camera GetUICamera() {
        Canvas canvas = GetComponentInParent<Canvas>();
        if (canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay) {
            return canvas.worldCamera;
        }
        return null;
    }

    public bool IsSelectableForCut() {
        return isSelectable;
    }

    public void ForceExitHover() {
        if (!isHovering) {
            return;
        }
        isHovering = false;
        TipsContainer.Instance.HideTips();
        if (Card3DHoverManager.Instance != null) {
            Card3DHoverManager.Instance.OnCardExit();
        }
    }

    private void OnEnable()
    {
        // Unity 的 EventSystem 在“物体出现在鼠标下方”时不会自动触发 OnPointerEnter。
        // 这里做一次主动检测，确保提示能立刻出现。
        StartCoroutine(CheckHoverOnEnableNextFrame());
    }

    private void Update() {
        if (HandCardDragController.IsDragging || HandCardDragController.SuppressPointerHover) {
            return;
        }
        if (Input.GetMouseButtonUp(0)) {
            HandleGlobalPointerUp(Input.mousePosition);
        }

        for (int i = 0; i < Input.touchCount; i++) {
            Touch touch = Input.GetTouch(i);
            if (touch.phase == TouchPhase.Ended) {
                HandleGlobalPointerUp(touch.position);
            }
        }
    }

    public static void NotifyPointerDown(TileCard card) {
        pointerDownCard = card;
    }

    public static void NotifyPointerUp(TileCard card) {
        if (pointerDownCard == card) {
            pointerDownCard = null;
        }
    }

    public static void ClearPendingPointerState() {
        if (pointerDownCard != null) {
            Debug.Log($"[HandInput] 清除跨回合左键按下缓存 | tileId={pointerDownCard.tileId}");
        }
        pointerDownCard = null;
    }

    /// <summary>
    /// 松手须仍在按下时的同一张牌上：命中该牌 UI，或落在该牌根节点手牌槽位内（含浮起下方的原位置）。
    /// skipSameCardCheck=true 供微点击路径使用：调用方已确认按下与松手在同一张牌上（悬停浮起已被拖拽会话清除，射线可能落空）。
    /// </summary>
    public static bool TryCommitClick(TileCard pressCard, Vector2 releaseScreenPos, bool skipSameCardCheck = false) {
        if (pressCard == null || !pressCard.IsSelectableForCut()) {
            return false;
        }
        if (lastHandledPointerFrame == Time.frameCount) {
            return false;
        }
        if (!skipSameCardCheck && !pressCard.IsSameCardReleasePoint(releaseScreenPos)) {
            return false;
        }
        bool canCut = NormalGameStateManager.Instance != null && (
            NormalGameStateManager.Instance.allowActionList.Contains("cut")
            || (RiichiCutSelectionController.Instance != null && RiichiCutSelectionController.Instance.IsActive));
        if (!canCut) {
            Debug.LogWarning($"[HandInput] 左键出牌被拦截(无cut权限) | tileId={pressCard.tileId}");
            pointerDownCard = null;
            return false;
        }

        if (ShouldUseHandCutConfirm()) {
            HandCardSelectionController selection = HandCardSelectionController.Instance;
            if (selection == null || !selection.IsArmed(pressCard)) {
                selection?.Arm(pressCard);
                // 消费本帧：同一次松手会经由 Relay 与全局 Update 两条路径进入，
                // 否则第二条路径会把刚立起的牌当成"二次确认"直接打出。
                lastHandledPointerFrame = Time.frameCount;
                return false;
            }
            selection.CommitDiscard(pressCard);
            if (GameCanvas.Instance != null) {
                GameCanvas.Instance.AnimateHandLayoutForDiscard(pressCard);
            }
        } else {
            HandCardSelectionController.Instance?.DisarmAll();
        }

        lastHandledPointerFrame = Time.frameCount;
        HandCardDragController.MarkTileClickHandledThisFrame();
        Debug.Log($"[HandInput] 左键出牌提交 | tileId={pressCard.tileId} | moqie={pressCard.currentGetTile}");
        pressCard.TriggerClick();
        return true;
    }

    private static bool ShouldUseHandCutConfirm() {
        if (ConfigManager.Instance == null || !ConfigManager.Instance.IsHandCutConfirmEnabled) {
            return false;
        }
        if (RiichiCutSelectionController.Instance != null && RiichiCutSelectionController.Instance.IsActive) {
            return false;
        }
        if (GameCanvas.Instance != null && GameCanvas.Instance.IsHandRecordPlayback()) {
            return false;
        }
        return true;
    }

    private bool IsSameCardReleasePoint(Vector2 screenPosition) {
        if (RaycastTopTileCard(screenPosition) == this) {
            return true;
        }
        return IsScreenPointInHandSlot(screenPosition);
    }

    private bool IsScreenPointInHandSlot(Vector2 screenPosition) {
        if (slotHitRect == null) {
            return false;
        }
        return RectTransformUtility.RectangleContainsScreenPoint(slotHitRect, screenPosition, GetUICamera());
    }

    private static void HandleGlobalPointerUp(Vector2 screenPosition) {
        if (pointerDownCard == null) {
            return;
        }
        if (Time.frameCount <= HandCardDragController.BlockTileClickUntilFrame) {
            pointerDownCard = null;
            return;
        }
        if (lastGlobalPointerUpFrame == Time.frameCount) {
            return;
        }
        lastGlobalPointerUpFrame = Time.frameCount;
        TileCard pressCard = pointerDownCard;
        pointerDownCard = null;
        TryCommitClick(pressCard, screenPosition);
    }

    private static TileCard RaycastTopTileCard(Vector2 screenPosition) {
        if (EventSystem.current == null) {
            return null;
        }
        PointerEventData pointerData = new PointerEventData(EventSystem.current) {
            position = screenPosition
        };
        List<RaycastResult> results = new List<RaycastResult>();
        EventSystem.current.RaycastAll(pointerData, results);
        foreach (RaycastResult result in results) {
            TileCard tileCard = result.gameObject.GetComponentInParent<TileCard>();
            if (tileCard != null) {
                return tileCard;
            }
        }
        return null;
    }

    /// <summary>
    /// 设置牌面图片
    /// </summary>
    public void SetTile(int id,bool isCurrentGetTile) {
        tileId = id;
        currentGetTile = isCurrentGetTile;
        isDrawSlotPinned = isCurrentGetTile;   // 新摸入牌默认固定在摸牌区，手动理牌时再清除

        int faceResourceId = id;
        if (ConfigManager.Instance.UseBlankWhiteDragonFace(id)) {
            faceResourceId = ConfigManager.BlankFaceImageId;
        }
        // 不需要添加扩展名
        string path = $"image/CardFaceImage_xuefun/{faceResourceId}";
        Sprite sprite = Resources.Load<Sprite>(path);
        
        if (sprite != null) {
            tileImage.sprite = sprite;
        } else {
            Debug.LogError($"找不到牌面图片: {path}");
        }
    }

    /// OntileClick 是出牌方法 如果牌属性currentGetTile为flase则为手切，如果为true则为摸切
    private void OnTileClick()
    {
        Debug.Log($"点击了牌: {tileId},{currentGetTile}");
        if (!IsSelectableForCut()) {
            Debug.Log("该牌不可出（定缺/食替/立直限制）");
            return;
        }
        // 立直选牌模式优先：仅向服务器发送 riichi_cut 请求；候选过滤已由 SetSelectable 完成。
        if (RiichiCutSelectionController.Instance != null && RiichiCutSelectionController.Instance.IsActive) {
            int cutIndex = transform.GetSiblingIndex();
            GameStateNetworkManager.Instance.SendRiichiCut(currentGetTile, tileId, cutIndex);
            RiichiCutSelectionController.Instance.ExitRiichiCutMode();
            return;
        }
        // 如果切牌在允许操作列表中
        if (NormalGameStateManager.Instance.allowActionList.Contains("cut")){
            int cutIndex = transform.GetSiblingIndex();// 获取切牌是父物体的第几个子物体
            GameStateNetworkManager.Instance.SendChineseGameTile(currentGetTile,tileId,cutIndex); // 发送切牌请求
        } else {
            Debug.Log("没有权限出牌");
        }
    }

    /// <summary>
    /// 两次点击确认：选中立起/取消时由 HandCardSelectionController 回调。
    /// 立起时无论指针悬停与否都显示该牌的切牌听牌提示；取消且未悬停时收起提示。
    /// </summary>
    public void OnArmedStateChanged(bool armed) {
        if (armed) {
            CheckCutTileTips(ignoreHoverGate: true);
        }
        else if (!isHovering) {
            TipsContainer.Instance.HideTips();
        }
    }

    /// <summary>
    /// 控制本卡牌是否可点击；不可选时整体调灰（保留 alpha=1，仅 RGB 调暗），用于立直选牌/食替禁切/立直锁定/四川定缺。
    /// 与日麻一致：只改 tileImage.color + Button.interactable，不碰 targetGraphic（否则牌面会变白不可见）。
    /// </summary>
    public void SetSelectable(bool selectable) {
        isSelectable = selectable;
        if (tileButton != null) tileButton.interactable = selectable;
        if (tileImage != null) {
            tileImage.color = selectable ? Color.white : new Color(0.55f, 0.55f, 0.55f, 1f);
        }
    }

    /// <summary>
    /// 公共方法：触发出牌（用于自动出牌功能）
    /// </summary>
    public void TriggerClick() {
        OnTileClick();
    }

    
    /// <summary>
    /// 鼠标进入时检测切牌后的听牌，并高亮所有相同tileId的3D卡牌
    /// </summary>
    public void OnPointerEnter(PointerEventData eventData)
    {
        if (HandCardDragController.IsDragging || HandCardDragController.SuppressPointerHover) {
            return;
        }
        isHovering = true;
        // 异步检测切牌后的听牌
        CheckCutTileTips();
        // 高亮所有相同tileId的3D卡牌
        if (tileId != -1 && Card3DHoverManager.Instance != null)
        {
            Card3DHoverManager.Instance.OnCardHover(tileId);
        }
    }
    
    /// <summary>
    /// 鼠标离开时隐藏提示，并恢复所有3D卡牌
    /// </summary>
    public void OnPointerExit(PointerEventData eventData)
    {
        isHovering = false;
        // 直接隐藏提示容器（内部会先清空内容）
        TipsContainer.Instance.HideTips();
        // 恢复所有3D卡牌
        if (Card3DHoverManager.Instance != null)
        {
            Card3DHoverManager.Instance.OnCardExit();
        }
    }
    
    /// <summary>
    /// 检测切牌后的听牌提示。ignoreHoverGate=true 用于两次点击确认的立起提示（不要求指针悬停）。
    /// </summary>
    private void CheckCutTileTips(bool ignoreHoverGate = false)
    {
        // 检查是否开启了提示功能
        if (!NormalGameStateManager.Instance.tips){
            return;
        }
        
        // 检查是否有切牌权限
        if (!NormalGameStateManager.Instance.allowActionList.Contains("cut")){
            return;
        }
        
        // 临时移除当前牌，进行听牌检测
        List<int> tempHandTiles = new List<int>(NormalGameStateManager.Instance.selfHandTiles);
        tempHandTiles.Remove(tileId);
        
        // 执行听牌检测
        HashSet<int> waitingTiles = new HashSet<int>();
        try
        {
            if (NormalGameStateManager.Instance.roomRule == "guobiao"){
                waitingTiles = GBtingpai.TingpaiCheck(
                    tempHandTiles,
                    NormalGameStateManager.Instance.player_to_info["self"].combination_tiles,
                    false
                );
            }
            else if (NormalGameStateManager.Instance.roomRule == "qingque"){
                waitingTiles = Qingque13External.TingpaiCheck(
                    tempHandTiles,
                    NormalGameStateManager.Instance.player_to_info["self"].combination_tiles ?? new List<string>(),
                    false
                );
            }
            else if (NormalGameStateManager.Instance.roomRule == "classical"){
                waitingTiles = ClassicalExternal.TingpaiCheck(
                    tempHandTiles,
                    NormalGameStateManager.Instance.player_to_info["self"].combination_tiles ?? new List<string>(),
                    false
                );
            }
            else if (NormalGameStateManager.Instance.roomRule == "riichi"){
                waitingTiles = RiichiExternal.TingpaiCheck(
                    tempHandTiles,
                    NormalGameStateManager.Instance.player_to_info["self"].combination_tiles ?? new List<string>(),
                    false
                );
            }
            else if (NormalGameStateManager.Instance.roomRule == "sichuan"){
                waitingTiles = SichuanExternal.TingpaiCheck(
                    tempHandTiles,
                    NormalGameStateManager.Instance.player_to_info["self"].combination_tiles ?? new List<string>()
                );
                // 四川：和牌张不得为定缺花色
                int dingque = NormalGameStateManager.Instance.selfDingqueSuit;
                if (dingque == 1 || dingque == 2 || dingque == 3){
                    waitingTiles.RemoveWhere(w => (w / 10) == dingque);
                }
            }
            else
            {
                Debug.LogWarning($"未知的规则类型: {NormalGameStateManager.Instance.roomRule}");
                waitingTiles = new HashSet<int>();
            }
        }
        catch (System.Exception e)
        {
            Debug.LogError($"检测切牌提示时出错: {e.Message}");
            waitingTiles = new HashSet<int>();
        }
        
        // 检查是否还在悬停状态（避免异步返回时已经离开）；立起提示不受悬停限制
        if (!isHovering && !ignoreHoverGate)
        {
            return;
        }
        
        // 如果听牌列表不为空，则显示提示
        if (waitingTiles.Count > 0)
        {
            Debug.Log($"显示切牌提示，听牌列表数量：{waitingTiles.Count}");
            // 这里传入“切掉当前牌后的手牌”tempHandTiles，避免多算一张牌
            TipsContainer.Instance.SetTipsWithHand(tempHandTiles, waitingTiles.ToList(), tileId);
            TipsContainer.Instance.hasTips = true;
            TipsContainer.Instance.ShowTips();
        }
        else
        {
            Debug.Log($"切牌后无听牌");
            TipsContainer.Instance.hasTips = false;
            TipsContainer.Instance.HideTips();
        }
    }

    private System.Collections.IEnumerator CheckHoverOnEnableNextFrame()
    {
        // 等一帧，保证 UI 布局/RectTransform 已就绪，否则射线检测可能拿到旧位置
        yield return null;

        if (!isActiveAndEnabled) yield break;
        if (EventSystem.current == null) yield break;
        if (HandCardDragController.SuppressPointerHover) yield break;

        // 构造一次 PointerEventData，进行 UI Raycast
        PointerEventData pointerData = new PointerEventData(EventSystem.current)
        {
            position = Input.mousePosition
        };

        List<RaycastResult> results = new List<RaycastResult>();
        EventSystem.current.RaycastAll(pointerData, results);

        // 找到射线命中的第一个 TileCard 是否就是自己（或自己的子物体）
        foreach (var r in results)
        {
            if (r.gameObject == null) continue;
            if (r.gameObject == gameObject || r.gameObject.transform.IsChildOf(transform))
            {
                isHovering = true;
                CheckCutTileTips();
                break;
            }
        }
    }

    private void OnDestroy()
    {
        HandCardSelectionController.Instance?.OnCardDestroyed(this);
        // 隐藏提示（参照tips的设计模式）
        TipsContainer.Instance.HideTips();
        // 清除3D卡牌高亮效果（如果正在悬停）
        if (isHovering && Card3DHoverManager.Instance != null)
        {
            Card3DHoverManager.Instance.OnCardExit();
        }
    }
}

/// <summary>
/// 将 Button 上的指针事件转发给父级 TileCard，供手牌拖拽使用。
/// </summary>
public class TileCardDragRelay : MonoBehaviour, IPointerDownHandler, IDragHandler, IPointerUpHandler, IPointerClickHandler {
    private TileCard owner;

    public void Bind(TileCard card) {
        owner = card;
    }

    public void OnPointerDown(PointerEventData eventData) {
        if (owner == null || eventData.button != PointerEventData.InputButton.Left) {
            return;
        }
        TileCard.NotifyPointerDown(owner);
        HandCardDragController.Instance.OnPointerDown(owner, eventData);
    }

    public void OnDrag(PointerEventData eventData) {
        if (owner == null || eventData.button != PointerEventData.InputButton.Left) {
            return;
        }
        HandCardDragController.Instance.OnDrag(owner, eventData);
    }

    public void OnPointerUp(PointerEventData eventData) {
        // 手牌只接受左键的拖拽与点击出牌；右键/中键交由快捷键控制器处理，手牌本身不出牌。
        if (owner == null || eventData.button != PointerEventData.InputButton.Left) {
            return;
        }
        bool dragHandled = HandCardDragController.Instance.OnPointerUp(owner, eventData);
        if (!dragHandled) {
            TileCard.TryCommitClick(owner, eventData.position);
        }
        TileCard.NotifyPointerUp(owner);
    }

    public void OnPointerClick(PointerEventData eventData) {
        // 右键转发至快捷键控制器（摸切/Pass），不触发出牌。
        if (eventData.button != PointerEventData.InputButton.Right) {
            return;
        }
        GameSceneMouseInputController.Instance?.HandleExternalPointerClick(eventData);
    }
}

/// <summary>
/// 固定在原槽位的透明点击区：仅记录按下/松手并出牌，不转发拖拽。
/// </summary>
public class TileCardSlotClickRelay : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerClickHandler {
    private TileCard owner;

    public void Bind(TileCard card) {
        owner = card;
    }

    public void OnPointerDown(PointerEventData eventData) {
        if (owner != null && eventData.button == PointerEventData.InputButton.Left) {
            TileCard.NotifyPointerDown(owner);
        }
    }

    public void OnPointerUp(PointerEventData eventData) {
        // 槽位点击区同样只接受左键出牌；右键不触发任何出牌。
        if (owner == null || eventData.button != PointerEventData.InputButton.Left) {
            return;
        }
        if (Time.frameCount > HandCardDragController.BlockTileClickUntilFrame) {
            TileCard.TryCommitClick(owner, eventData.position);
        }
        TileCard.NotifyPointerUp(owner);
    }

    public void OnPointerClick(PointerEventData eventData) {
        if (eventData.button != PointerEventData.InputButton.Right) {
            return;
        }
        GameSceneMouseInputController.Instance?.HandleExternalPointerClick(eventData);
    }
}