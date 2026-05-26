using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using System.Threading.Tasks;

public partial class Game3DManager : MonoBehaviour {
    [SerializeField] private GameObject tile3DPrefab;    // 3D预制体
    [SerializeField] private GameObject riichiTenbouPrefab; // 立直点棒（1000）3D预制体
    [Header("3D位置面板")]
    [SerializeField] private PosPanel3D selfPosPanel;    // 自家位置面板
    [SerializeField] private PosPanel3D leftPosPanel;    // 左家位置面板
    [SerializeField] private PosPanel3D topPosPanel;     // 对家位置面板
    [SerializeField] private PosPanel3D rightPosPanel;    // 右家位置面板

    private Vector3 selfSetCombinationsPoint; // 组合指针用于存储各家组合牌生成位置
    private Vector3 leftSetCombinationsPoint;
    private Vector3 topSetCombinationsPoint;
    private Vector3 rightSetCombinationsPoint;

    private GameObject lastCut3DObject; // 最后一张弃牌的3D对象
    private Coroutine _currentDiscardMoveCoroutine; // 当前出牌飞行动画协程，鸣牌时需终止
    private Vector3 lastRemove3DPosition; // 最后一张删除的3D对象
    private Dictionary<int,Vector3> pengToJiagangPosDict = new Dictionary<int,Vector3>(); // 碰牌的加杠预留指针

    private float cardWidth; // 卡片宽度 组合牌 3D手牌使用
    private float cardHeight; // 卡片高度
    private float cardThickness; // 卡片厚度（红色轴），用于暗杠抬高修正
    private float cardScale; // 卡片缩放
    private float widthSpacing; // 间距为卡片宽度的1.05倍 弃牌 补花 使用
    private float heightSpacing; // 间距为卡片高度的1.05倍
    private Vector3 RightDirection; // 右方向
    private Vector3 LeftDirection; // 左方向
    private Vector3 FrontDirection; // 前方向
    private Vector3 BackDirection; // 后方向
    private Vector3 UpDirection; // 上方向
    private Vector3 DownDirection; // 下方向

    public static Game3DManager Instance { get; private set; }

    private void Awake() {
        if (Instance != null && Instance != this) {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        // 初始化配置
        this.cardScale = tile3DPrefab.transform.localScale.z; // 卡片缩放比例
        this.cardWidth = tile3DPrefab.GetComponent<Renderer>().bounds.size.x * 1.06f; // 卡片宽度（红色轴）
        this.cardHeight = tile3DPrefab.GetComponent<Renderer>().bounds.size.y * 1.04f; // 卡片高度（绿色轴）
        this.cardThickness = tile3DPrefab.GetComponent<Renderer>().bounds.size.z; // 卡片厚度（蓝色轴）
        this.widthSpacing = cardWidth * 1.05f; // 间距为卡片宽度的1.1倍
        this.heightSpacing = cardHeight * 1.05f; // 间距为卡片高度的1.1倍
        // 初始化放置组合牌指针
        selfSetCombinationsPoint = selfPosPanel.combinationsPosition.position;
        leftSetCombinationsPoint = leftPosPanel.combinationsPosition.position;
        topSetCombinationsPoint = topPosPanel.combinationsPosition.position;
        rightSetCombinationsPoint = rightPosPanel.combinationsPosition.position;
        // 初始化世界位置
        RightDirection = new Vector3(1,0,0);
        LeftDirection = new Vector3(-1,0,0);
        FrontDirection = new Vector3(0,0,1);
        BackDirection = new Vector3(0,0,-1);
        UpDirection = new Vector3(0,1,0);
        DownDirection = new Vector3(0,-1,0);
        ResetHandRevealAnimators();
    }

    /// <summary>
    /// 重置四家手牌展开动画到 Idle 状态，避免上一局展开末帧影响下一局摆牌。
    /// </summary>
    public void ResetHandRevealAnimators() {
        foreach (PosPanel3D panel in new[] { selfPosPanel, leftPosPanel, topPosPanel, rightPosPanel }) {
            ForceHandRevealIdle(panel);
        }
    }

    /// <summary>
    /// 立直棒动画：从该位玩家的 outputPos 飞向 tenbouPos；预制体由 inspector 直接绑定 riichiTenbouPrefab。
    /// </summary>
    public void PlayRiichiTenbouFlight(string playerPosition) {
        PosPanel3D panel = GetPosPanel(playerPosition);
        Transform startTransform = panel.outputPos != null ? panel.outputPos : panel.cardsPosition;
        Transform endTransform = panel.tenbouPos;
        if (startTransform == null || endTransform == null) {
            Debug.LogWarning($"PosPanel3D[{playerPosition}] 缺少 outputPos 或 tenbouPos，无法播放立直棒飞行动画");
            return;
        }
        if (riichiTenbouPrefab == null) {
            Debug.LogWarning("Game3DManager.riichiTenbouPrefab 未绑定");
            return;
        }
        Quaternion endRot = RiichiTenbouPlacementRotation(playerPosition);
        GameObject tenbou = Instantiate(riichiTenbouPrefab, startTransform.position, endRot);
        tenbou.transform.SetParent(panel.tenbouPos.parent, worldPositionStays: true);
        StartCoroutine(MoveTenbouCoroutine(tenbou, startTransform.position, endTransform.position, endRot, 0.6f));
    }

    /// <summary>
    /// 重连/牌谱场景：直接把立直点棒摆放到 tenbouPos，不播放飞行动画；用于断线回归后玩家立直状态恢复展示。
    /// 同一位置已有立直棒时不重复创建。
    /// </summary>
    public void PlaceRiichiTenbouAt(string playerPosition) {
        PosPanel3D panel = GetPosPanel(playerPosition);
        Transform endTransform = panel.tenbouPos;
        if (endTransform == null || riichiTenbouPrefab == null) return;
        if (panel.tenbouPos.parent != null) {
            for (int i = 0; i < panel.tenbouPos.parent.childCount; i++) {
                Transform t = panel.tenbouPos.parent.GetChild(i);
                if (t != null && t.name.StartsWith("RiichiTenbou_")) return;
            }
        }
        Quaternion endRot = RiichiTenbouPlacementRotation(playerPosition);
        GameObject tenbou = Instantiate(riichiTenbouPrefab, endTransform.position, endRot);
        tenbou.name = $"RiichiTenbou_{playerPosition}";
        tenbou.transform.SetParent(endTransform.parent, worldPositionStays: true);
    }

    /// <summary>立直棒落点朝向：与各家河牌俯视一致，不继承 UI 节点旋转。</summary>
    private Quaternion RiichiTenbouPlacementRotation(string playerPosition) {
        if (playerPosition == "self") return Quaternion.Euler(90, 0, 180);
        if (playerPosition == "left") return Quaternion.Euler(90, 0, 90);
        if (playerPosition == "top") return Quaternion.Euler(90, 0, 0);
        if (playerPosition == "right") return Quaternion.Euler(90, 0, 270);
        return Quaternion.identity;
    }

    /// <summary>清空场上所有立直点棒（每局 Clear3DTile 时调用，避免下一局残留）。</summary>
    public void ClearAllRiichiTenbous() {
        foreach (PosPanel3D panel in new[] { selfPosPanel, leftPosPanel, topPosPanel, rightPosPanel }) {
            if (panel.tenbouPos == null || panel.tenbouPos.parent == null) continue;
            for (int i = panel.tenbouPos.parent.childCount - 1; i >= 0; i--) {
                Transform t = panel.tenbouPos.parent.GetChild(i);
                if (t != null && t.name.StartsWith("RiichiTenbou_")) {
                    Destroy(t.gameObject);
                }
            }
        }
    }

    private IEnumerator MoveTenbouCoroutine(GameObject obj, Vector3 from, Vector3 to, Quaternion endRot, float duration) {
        float t = 0f;
        Quaternion startRot = obj.transform.rotation;
        while (t < duration) {
            t += Time.deltaTime;
            float u = Mathf.Clamp01(t / duration);
            obj.transform.position = Vector3.Lerp(from, to, u);
            obj.transform.rotation = Quaternion.Slerp(startRot, endRot, u);
            yield return null;
        }
        obj.transform.position = to;
        obj.transform.rotation = endRot;
    }

    /// <summary>自家手牌立牌朝向（与他家 Get3D 侧视一致）；日麻流局听牌展示时由 Animator 播倒下。</summary>
    private Quaternion SelfHandStandingRotation() {
        return Quaternion.Euler(-180, 180, 0);
    }

    /// <summary>局终/牌谱明牌摆牌：与摸牌时相同的立面朝向，供 Cube 展开动画推倒。</summary>
    private Quaternion RecordHandTileRotation(string playerPosition) {
        if (playerPosition == "self") return SelfHandStandingRotation();
        if (playerPosition == "left") return Quaternion.Euler(0, 90, 0);
        if (playerPosition == "top") return Quaternion.Euler(0, 180, 0);
        if (playerPosition == "right") return Quaternion.Euler(180, 270, 0);
        return Quaternion.identity;
    }

    /// <summary>自家牌：仅手牌容器为立面；河、副露、补花仍为俯视位姿。</summary>
    private Quaternion SelfTileWorldRotation(Transform setPosition) {
        if (setPosition == selfPosPanel.cardsPosition) {
            return SelfHandStandingRotation();
        }
        return Quaternion.Euler(90, 0, 180);
    }

    /// <summary>清空自家 3D 手牌区；对局中使用 2D 手牌，不在此处生成白背牌。</summary>
    public void ClearSelf3DHandTiles() {
        if (selfPosPanel == null) return;
        List<GameObject> objectsToReturn = new List<GameObject>();
        CollectChildren(selfPosPanel.cardsPosition, objectsToReturn);
        foreach (GameObject obj in objectsToReturn) {
            MahjongObjectPool.Instance.Return(-1, obj);
        }
    }

    // 3D手牌处理入口 协程并行执行，多个 Change3DTileCoroutine 可同时运行
    // 本方法管理所有来自GameSceneManager的3D手牌处理请求 除了Clear3DTile清空面板；牌谱重建等可直调 ActionAnimationCoroutine
    public void Change3DTile(string actionType,int tileId,int removeCount,string PlayerPosition,bool cut_class,int[] combination_mask, bool isRiichi = false){
        // 牌谱重建/重连的无动画分支直接执行，避免队列协程逐帧处理
        if (actionType == "SetDiscardWithoutAnimation" || actionType == "SetBuhuacardWithoutAnimation" || actionType == "SetRecordDiscardWithoutAnimation"){
            PosPanel3D panel = GetPosPanel(PlayerPosition);
            if (panel == null) return;

            if (actionType == "SetDiscardWithoutAnimation"){
                Set3DTile(tileId, panel.discardsPosition, "DiscardWithoutAnimation", PlayerPosition, isMoqie: false, isRiichi: isRiichi);
            }
            else if (actionType == "SetRecordDiscardWithoutAnimation"){
                Set3DTile(tileId, panel.discardsPosition, "DiscardWithoutAnimation", PlayerPosition, cut_class, isRiichi: isRiichi);
            }
            else {
                Set3DTile(tileId, panel.buhuaPosition, "BuhuaWithoutAnimation", PlayerPosition);
            }
            return;
        }

        // 初始化手牌走同步路径，避免与紧随其后的补花/摸牌广播触发的删/增手牌协程交错，
        // 导致 InitHandCards 在 yield 帧之后才补出 13 张手牌叠加在已增量的牌上。
        if (actionType == "InitHandCards"){
            InitHandCardsImmediate();
            return;
        }

        // 直接启动协程，允许多个 Change3DTileCoroutine 并行执行
        StartCoroutine(Change3DTileCoroutine(actionType,tileId,removeCount,PlayerPosition,cut_class,combination_mask, isRiichi));
    }

    // 同步初始化各家手牌：清空当前 cardsPosition，按 player_to_info 与 selfHandTiles 立即生成
    private void InitHandCardsImmediate() {
        ClearAllRiichiTenbous();
        List<GameObject> objectsToReturn = new List<GameObject>();
        CollectChildren(leftPosPanel.cardsPosition, objectsToReturn);
        CollectChildren(topPosPanel.cardsPosition, objectsToReturn);
        CollectChildren(rightPosPanel.cardsPosition, objectsToReturn);
        CollectChildren(selfPosPanel.cardsPosition, objectsToReturn);
        foreach (GameObject obj in objectsToReturn) {
            MahjongObjectPool.Instance.Return(-1, obj);
        }

        int leftCount = NormalGameStateManager.Instance.player_to_info["left"].hand_tiles_count;
        int topCount = NormalGameStateManager.Instance.player_to_info["top"].hand_tiles_count;
        int rightCount = NormalGameStateManager.Instance.player_to_info["right"].hand_tiles_count;
        for (int i = 0; i < leftCount; i++) {
            Get3DTile("left", "init", 0);
        }
        for (int i = 0; i < topCount; i++) {
            Get3DTile("top", "init", 0);
        }
        for (int i = 0; i < rightCount; i++) {
            Get3DTile("right", "init", 0);
        }
    }
    
    // Change3DTile 管理3D手牌组的变更行为
    public IEnumerator Change3DTileCoroutine(string actionType,int tileId,int removeCount,string PlayerPosition,bool cut_class,int[] combination_mask, bool isRiichi = false){
        // Change3DTile 所有类型：
        // InitHandCards 初始化手牌 => ClearHandCardsCoroutine => Get3DTile
        // GetCard 摸牌 => Get3DTile
        // Discard 弃牌 => Set3DTile(discard) => RemoveHandCardsCoroutine
        // Buhua 补花 => Set3DTile(buhua) => RemoveHandCardsCoroutine // 其实补花也有摸打补花的说法，后续需要优化这种情况
        // 吃碰杠 => ActionAnimation => RemoveHandCardsCoroutine（含自家，按掩码删牌）

        // 初始化手牌：统一走同步路径，避免协程 yield 期间与补花/摸牌广播交错产生多余手牌
        if (actionType == "InitHandCards"){
            InitHandCardsImmediate();
            yield break;
        }

        if (actionType == "InitHandCardsFromRecord"){
            yield return StartCoroutine(ClearRecordHandCardsCoroutine());
            RenderRecordPlayerHand("left");
            RenderRecordPlayerHand("top");
            RenderRecordPlayerHand("right");
        }

        // 摸牌 
        else if (actionType == "GetCard"){
            if (IsRecordShowCardsModeActive() && PlayerPosition != "self") {
                yield return StartCoroutine(RecordShowCardGetCoroutine(PlayerPosition, tileId));
                yield break;
            }
            yield return StartCoroutine(Get3DTileCoroutine(PlayerPosition, "get", tileId));
        }

        // 仅设置弃牌但不删除玩家手牌且无动画的牌谱转移和重连方法
        else if (actionType == "SetDiscardWithoutAnimation"){
            PosPanel3D panel = GetPosPanel(PlayerPosition);
            Set3DTile(tileId, panel.discardsPosition, "DiscardWithoutAnimation", PlayerPosition, isMoqie: false, isRiichi: isRiichi); // 弃牌区增加弃牌
        }

        // 弃牌
        else if (actionType == "Discard"){
            PosPanel3D panel = GetPosPanel(PlayerPosition);
            if (IsRecordShowCardsModeActive() && PlayerPosition != "self") {
                yield return StartCoroutine(RemoveRecordShowHandCardCoroutine(panel.ShowCardsPosition, tileId, cut_class));
                yield return StartCoroutine(Set3DTileCoroutine(tileId, panel.discardsPosition, "Discard", PlayerPosition, isMoqie: false, isRiichi: isRiichi));
                RenderRecordPlayerHand(PlayerPosition);
                yield break;
            }
            yield return StartCoroutine(RemoveHandCardsCoroutine(panel.cardsPosition, 1, cut_class, tileId, null));
            yield return StartCoroutine(Set3DTileCoroutine(tileId, panel.discardsPosition, "Discard", PlayerPosition, isMoqie: false, isRiichi: isRiichi));
        }

        // 牌谱弃牌（摸切显示灰色叠加）
        else if (actionType == "RecordDiscard"){
            PosPanel3D panel = GetPosPanel(PlayerPosition);
            if (IsRecordShowCardsModeActive() && PlayerPosition != "self") {
                yield return StartCoroutine(RemoveRecordShowHandCardCoroutine(panel.ShowCardsPosition, tileId, cut_class));
                yield return StartCoroutine(Set3DTileCoroutine(tileId, panel.discardsPosition, "Discard", PlayerPosition, cut_class, isRiichi: isRiichi));
                yield return StartCoroutine(RearrangeRecordShowCardsWithAnimation(panel.ShowCardsPosition, PlayerPosition));
                yield break;
            }
            yield return StartCoroutine(RemoveHandCardsCoroutine(panel.cardsPosition, 1, cut_class, tileId, null));
            yield return StartCoroutine(Set3DTileCoroutine(tileId, panel.discardsPosition, "Discard", PlayerPosition, cut_class, isRiichi: isRiichi));
        }

        // 仅设置补花但不删除玩家手牌且无动画的牌谱转移和重连方法
        else if (actionType == "SetBuhuacardWithoutAnimation"){
            PosPanel3D panel = GetPosPanel(PlayerPosition);
            Set3DTile(tileId, panel.buhuaPosition, "BuhuaWithoutAnimation", PlayerPosition); // 补花区增加补花
        }

        // 补花
        else if (actionType == "Buhua"){
            PosPanel3D panel = GetPosPanel(PlayerPosition);
            if (IsRecordShowCardsModeActive() && PlayerPosition != "self") {
                yield return StartCoroutine(RemoveRecordShowHandCardCoroutine(panel.ShowCardsPosition, tileId, false));
                yield return StartCoroutine(Set3DTileCoroutine(tileId, panel.buhuaPosition, "Buhua", PlayerPosition));
                yield return StartCoroutine(RearrangeRecordShowCardsWithAnimation(panel.ShowCardsPosition, PlayerPosition));
                yield break;
            }
            yield return StartCoroutine(RemoveHandCardsCoroutine(panel.cardsPosition, 1, false, tileId, null));
            // 删除完成后再创建补花
            yield return StartCoroutine(Set3DTileCoroutine(tileId, panel.buhuaPosition, "Buhua", PlayerPosition));
        }

        // 吃碰杠
        else if (actionType == "chi_left" || actionType == "chi_mid" || actionType == "chi_right" ||
                 actionType == "peng" || actionType == "gang" || actionType == "angang" || actionType == "jiagang"){   
            // 删除上一张3D卡牌，归还到对象池
            if (lastCut3DObject != null){
                // 加杠和暗杠不需要删除上一张3D卡牌
                if (actionType != "jiagang" && actionType != "angang"){
                    // 若最后一张弃牌仍在飞行动画中，先终止动画避免复用后位置被改写
                    if (_currentDiscardMoveCoroutine != null) {
                        StopCoroutine(_currentDiscardMoveCoroutine);
                        _currentDiscardMoveCoroutine = null;
                    }
                    MahjongObjectPool.Instance.Return(-1, lastCut3DObject);
                    lastCut3DObject = null;
                    // 等一帧再构建组合，避免卡牌位置仍被已中止的动画协程决定；组合动画是移动父物体，若牌的世界位置未刷新会错位
                    yield return null;
                }
            }
            else{
                Debug.LogWarning("上一张3D卡牌为空");
            }
            PosPanel3D panel = GetPosPanel(PlayerPosition);
            if (IsRecordShowCardsModeActive() && PlayerPosition != "self") {
                yield return StartCoroutine(RemoveRecordShowHandCardsByMaskCoroutine(panel.ShowCardsPosition, combination_mask));
                yield return StartCoroutine(ActionAnimationCoroutine(PlayerPosition, actionType, combination_mask, true));
                yield return StartCoroutine(RearrangeRecordShowCardsWithAnimation(panel.ShowCardsPosition, PlayerPosition));
                yield break;
            }
            // 放置组合牌：删除手牌（含自家明牌手牌）后摆放副露
            yield return StartCoroutine(RemoveHandCardsCoroutine(panel.cardsPosition, removeCount, false, -1, combination_mask));
            yield return StartCoroutine(ActionAnimationCoroutine(PlayerPosition, actionType, combination_mask,true));
        }
        
        yield break;
    }

    
    // 根据玩家位置获取对应的位置面板
    private PosPanel3D GetPosPanel(string playerPosition){
        switch (playerPosition){
            case "self":
                return selfPosPanel;
            case "left":
                return leftPosPanel;
            case "top":
                return topPosPanel;
            case "right":
                return rightPosPanel;
            default:
                Debug.LogError($"未知的玩家位置: {playerPosition}");
                return null;
        }
    }

    // 清除手牌协程（用于在初始化前清除，确保 childCount 正确）
    private IEnumerator ClearHandCardsCoroutine(){
        // 先收集所有要归还的手牌对象，避免在遍历时修改集合
        List<GameObject> objectsToReturn = new List<GameObject>();
        
        // 收集所有手牌
        CollectChildren(leftPosPanel.cardsPosition, objectsToReturn);
        CollectChildren(topPosPanel.cardsPosition, objectsToReturn);
        CollectChildren(rightPosPanel.cardsPosition, objectsToReturn);
        CollectChildren(selfPosPanel.cardsPosition, objectsToReturn);
        
        // 统一归还所有收集到的对象
        foreach (GameObject obj in objectsToReturn)
        {
            if (obj != null)
            {
                MahjongObjectPool.Instance.Return(-1, obj);
            }
        }
        
        // 等待一帧，确保所有对象都被归还，childCount 更新
        yield return null;
    }

    private IEnumerator ClearRecordHandCardsCoroutine(){
        List<GameObject> objectsToReturn = new List<GameObject>();
        CollectChildren(leftPosPanel.cardsPosition, objectsToReturn);
        CollectChildren(topPosPanel.cardsPosition, objectsToReturn);
        CollectChildren(rightPosPanel.cardsPosition, objectsToReturn);
        CollectChildren(leftPosPanel.ShowCardsPosition, objectsToReturn);
        CollectChildren(topPosPanel.ShowCardsPosition, objectsToReturn);
        CollectChildren(rightPosPanel.ShowCardsPosition, objectsToReturn);
        foreach (GameObject obj in objectsToReturn) {
            MahjongObjectPool.Instance.Return(-1, obj);
        }
        yield return null;
    }

    public void RefreshRecordHandDisplay(){
        Change3DTile("InitHandCardsFromRecord",0,0,null,false,null);
    }

    private void RenderRecordPlayerHand(string playerPosition){
        List<int> handTiles = GameRecordManager.Instance.recordPlayer_to_info[playerPosition].tileList;
        PosPanel3D panel = GetPosPanel(playerPosition);
        ClearPlayerRecordHandObjects(panel);
        bool isShowCardsMode = RecordSetting.Instance.IsShowCardsMode;
        Transform target = isShowCardsMode ? panel.ShowCardsPosition : panel.cardsPosition;
        if (isShowCardsMode){
            List<int> sortedTiles = new List<int>(handTiles);
            sortedTiles.Sort(TileIdOrder.Compare);
            for (int i = 0; i < sortedTiles.Count; i++){
                Set3DTile(sortedTiles[i], target, "Record", playerPosition);
            }
            return;
        }
        for (int i = 0; i < handTiles.Count; i++){
            Get3DTile(playerPosition,"init");
        }
    }

    private bool IsRecordShowCardsModeActive(){
        return RecordSetting.Instance != null &&
               RecordSetting.Instance.IsShowCardsMode &&
               GameRecordManager.Instance != null &&
               GameRecordManager.Instance.gameObject.activeSelf;
    }


    /// <summary>
    /// 牌谱展开模式：在 ShowCardsPosition 右侧以间隔放置摸到的牌（面朝上）
    /// </summary>
    private IEnumerator RecordShowCardGetCoroutine(string playerPosition, int tileId) {
        PosPanel3D panel = GetPosPanel(playerPosition);
        Transform showCards = panel.ShowCardsPosition;

        Vector3 direction = Vector3.zero;
        Quaternion rotation = Quaternion.identity;
        if (playerPosition == "left") { direction = BackDirection; rotation = Quaternion.Euler(90, 0, 90); }
        else if (playerPosition == "top") { direction = LeftDirection; rotation = Quaternion.Euler(90, 0, 0); }
        else if (playerPosition == "right") { direction = FrontDirection; rotation = Quaternion.Euler(90, 0, 270); }
        else { yield break; }

        Vector3 spawnPosition = showCards.position + direction.normalized * widthSpacing * (showCards.childCount + 1);

        GameObject cardObj = MahjongObjectPool.Instance.Spawn(tileId, spawnPosition, rotation);
        if (cardObj == null) yield break;

        cardObj.transform.SetParent(showCards, worldPositionStays: true);
        cardObj.name = $"RecordGet_{showCards.childCount}";

        if (Card3DHoverManager.Instance != null) {
            Card3DHoverManager.Instance.RegisterCard(cardObj, tileId);
        }

        yield return null;
    }

    /// <summary>
    /// 牌谱展开模式：按tileId排序并动画移动到正确位置
    /// </summary>
    private IEnumerator RearrangeRecordShowCardsWithAnimation(Transform showCardsPosition, string playerPosition) {
        if (showCardsPosition == null || showCardsPosition.childCount == 0) yield break;

        Vector3 direction = Vector3.zero;
        if (playerPosition == "left") { direction = BackDirection; }
        else if (playerPosition == "top") { direction = LeftDirection; }
        else if (playerPosition == "right") { direction = FrontDirection; }
        else { yield break; }

        List<Transform> cards = new List<Transform>();
        for (int i = 0; i < showCardsPosition.childCount; i++) {
            cards.Add(showCardsPosition.GetChild(i));
        }

        cards.Sort((a, b) => {
            Tile3D tileA = a.GetComponent<Tile3D>();
            Tile3D tileB = b.GetComponent<Tile3D>();
            int idA = tileA != null ? tileA.GetTileId() : 0;
            int idB = tileB != null ? tileB.GetTileId() : 0;
            return TileIdOrder.Compare(idA, idB);
        });

        Vector3 startPos = showCardsPosition.position;
        List<Vector3> targetPositions = new List<Vector3>();
        for (int i = 0; i < cards.Count; i++) {
            targetPositions.Add(startPos + direction.normalized * widthSpacing * i);
        }

        yield return StartCoroutine(Animate3DCardsToPositions(cards, targetPositions));
    }

    /// <summary>
    /// 牌谱展开模式：根据组合掩码从 ShowCardsPosition 中删除指定手牌
    /// 掩码格式 [flag, tileId, flag, tileId, ...]
    /// flag=0 手中牌(吃碰杠) / flag=2 暗杠牌 / flag=3 加杠牌 → 需从手牌删除
    /// flag=1 来源牌(他人弃牌) → 不需要从手牌删除
    /// </summary>
    private IEnumerator RemoveRecordShowHandCardsByMaskCoroutine(Transform showCardsPosition, int[] combinationMask) {
        if (showCardsPosition == null || combinationMask == null) yield break;

        List<int> tilesToRemove = new List<int>();
        for (int i = 0; i + 1 < combinationMask.Length; i += 2) {
            int flag = combinationMask[i];
            int tileId = combinationMask[i + 1];
            if (flag != 1) {
                tilesToRemove.Add(tileId);
            }
        }

        foreach (int tileId in tilesToRemove) {
            for (int i = 0; i < showCardsPosition.childCount; i++) {
                Transform child = showCardsPosition.GetChild(i);
                Tile3D tile3D = child.GetComponent<Tile3D>();
                if (tile3D != null && tile3D.GetTileId() == tileId) {
                    lastRemove3DPosition = child.position;
                    MahjongObjectPool.Instance.Return(-1, child.gameObject);
                    break;
                }
            }
        }

        yield return null;
    }

    private void ClearPlayerRecordHandObjects(PosPanel3D panel){
        List<GameObject> objectsToReturn = new List<GameObject>();
        CollectChildren(panel.cardsPosition, objectsToReturn);
        CollectChildren(panel.ShowCardsPosition, objectsToReturn);
        foreach (GameObject obj in objectsToReturn){
            MahjongObjectPool.Instance.Return(-1, obj);
        }
    }

    private IEnumerator RemoveRecordShowHandCardCoroutine(Transform showCardsPosition, int tileId, bool cutClass){
        if (showCardsPosition.childCount == 0) {
            yield break;
        }

        Transform targetCard = null;
        if (cutClass) {
            for (int i = showCardsPosition.childCount - 1; i >= 0; i--) {
                Transform child = showCardsPosition.GetChild(i);
                Tile3D tile3D = child.GetComponent<Tile3D>();
                if (tile3D != null && tile3D.GetTileId() == tileId) {
                    targetCard = child;
                    break;
                }
            }
        } else {
            for (int i = 0; i < showCardsPosition.childCount; i++) {
                Transform child = showCardsPosition.GetChild(i);
                Tile3D tile3D = child.GetComponent<Tile3D>();
                if (tile3D != null && tile3D.GetTileId() == tileId) {
                    targetCard = child;
                    break;
                }
            }
        }

        if (targetCard == null) {
            targetCard = showCardsPosition.GetChild(showCardsPosition.childCount - 1);
        }

        lastRemove3DPosition = targetCard.position;
        MahjongObjectPool.Instance.Return(-1, targetCard.gameObject);
        yield return null;
    }
    
    // 清除3D手牌
    public void Clear3DTile(){
        // 重置组合牌指针
        selfSetCombinationsPoint = selfPosPanel.combinationsPosition.position;
        leftSetCombinationsPoint = leftPosPanel.combinationsPosition.position;
        topSetCombinationsPoint = topPosPanel.combinationsPosition.position;
        rightSetCombinationsPoint = rightPosPanel.combinationsPosition.position;
        pengToJiagangPosDict.Clear();
        
        // 先收集所有要归还的对象，避免在遍历时修改集合导致跳过元素
        List<GameObject> objectsToReturn = new List<GameObject>();
        
        // 收集所有面板的弃牌
        CollectChildren(leftPosPanel.discardsPosition, objectsToReturn);
        CollectChildren(topPosPanel.discardsPosition, objectsToReturn);
        CollectChildren(rightPosPanel.discardsPosition, objectsToReturn);
        CollectChildren(selfPosPanel.discardsPosition, objectsToReturn);

        // 收集所有面板的补花
        CollectChildren(leftPosPanel.buhuaPosition, objectsToReturn);
        CollectChildren(topPosPanel.buhuaPosition, objectsToReturn);
        CollectChildren(rightPosPanel.buhuaPosition, objectsToReturn);
        CollectChildren(selfPosPanel.buhuaPosition, objectsToReturn);

        // 收集所有面板的展开手牌
        CollectChildren(leftPosPanel.ShowCardsPosition, objectsToReturn);
        CollectChildren(topPosPanel.ShowCardsPosition, objectsToReturn);
        CollectChildren(rightPosPanel.ShowCardsPosition, objectsToReturn);
        CollectChildren(selfPosPanel.ShowCardsPosition, objectsToReturn);

        // 收集所有面板的手牌（含上局和牌/听牌摆出的明牌、未被 ClearHandCardsCoroutine 回收的残留）；
        // 由同步的 Clear3DTile 单点回收，避免下一局 ClearHandCardsCoroutine 协程被先行启动的他局摆牌
        // 干扰，从而引发对象池耗尽与重复手牌堆叠。
        CollectChildren(leftPosPanel.cardsPosition, objectsToReturn);
        CollectChildren(topPosPanel.cardsPosition, objectsToReturn);
        CollectChildren(rightPosPanel.cardsPosition, objectsToReturn);
        CollectChildren(selfPosPanel.cardsPosition, objectsToReturn);
        
        // 收集所有面板的组合牌3D对象数组中的子物体
        foreach (PosPanel3D panel in new[] { selfPosPanel, leftPosPanel, topPosPanel, rightPosPanel })
        {
            foreach (Transform combinationObj in panel.combination3DObjects)
            {
                if (combinationObj != null)
                {
                    CollectChildren(combinationObj, objectsToReturn);
                }
            }
        }
        
        // 统一归还所有收集到的对象
        foreach (GameObject obj in objectsToReturn)
        {
            if (obj != null)
            {
                MahjongObjectPool.Instance.Return(-1, obj);
            }
        }

        // 立直点棒与手牌展开 Animator 的清理：避免上一局立直棒/展开姿势残留到下一局。
        ClearAllRiichiTenbous();
        ResetHandRevealAnimators();
    }

    /// <summary>
    /// 清空本对象上所有正在执行的协程（用于牌谱重新推理手牌前，避免旧动画与重建画面冲突）
    /// </summary>
    public void StopAllRunningAnimations() {
        if (_currentDiscardMoveCoroutine != null) {
            StopCoroutine(_currentDiscardMoveCoroutine);
            _currentDiscardMoveCoroutine = null;
        }
        lastCut3DObject = null;
        StopAllCoroutines();
    }
    
    /// <summary>
    /// 收集 Transform 的所有子对象到列表中（避免在遍历时修改集合）
    /// </summary>
    private void CollectChildren(Transform parent, List<GameObject> collection)
    {
        if (parent == null) return;
        
        // 使用倒序遍历，避免索引变化问题
        for (int i = parent.childCount - 1; i >= 0; i--)
        {
            Transform child = parent.GetChild(i);
            if (child != null)
            {
                collection.Add(child.gameObject);
            }
        }
    }

}
