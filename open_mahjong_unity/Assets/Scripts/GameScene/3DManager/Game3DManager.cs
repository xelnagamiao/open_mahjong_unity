using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using System.Threading.Tasks;

public partial class Game3DManager : MonoBehaviour {
    [SerializeField] private GameObject tile3DPrefab;    // 3D预制体
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

    // 3D手牌处理队列
    private Queue<System.Func<Coroutine>> change3DTileQueue = new Queue<System.Func<Coroutine>>();
    private bool isChange3DTileProcessing = false;

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
    }



    // 3D手牌处理入口 为了保证摸牌和打牌在未来添加动画以后不同时处理 所以使用队列管理 
    // 本方法管理所有来自GameSceneManager的3D手牌处理请求 除了Clear3DTile清空面板 ActionAnimation和牌谱直接调用子方法
    public void Change3DTile(string actionType,int tileId,int removeCount,string PlayerPosition,bool cut_class,int[] combination_mask){
        // 将3D手牌处理任务加入队列
        change3DTileQueue.Enqueue(() => {
            return StartCoroutine(Change3DTileCoroutine(actionType,tileId,removeCount,PlayerPosition,cut_class,combination_mask));
        });
        // 未启动执行队列则启动
        if (!isChange3DTileProcessing){
            StartCoroutine(ProcessChange3DTileQueue());
        }
    }
    
    // 处理3D手牌队列
    private IEnumerator ProcessChange3DTileQueue(){
        // 3D手牌处理运行
        isChange3DTileProcessing = true;
        while (change3DTileQueue.Count > 0){
            // 拿取下一个任务
            System.Func<Coroutine> change3DTileAction = change3DTileQueue.Dequeue();
            // 执行任务
            Coroutine change3DTileCoroutine = change3DTileAction.Invoke();
            // 等待任务完成
            yield return change3DTileCoroutine;
        }
        // 3D手牌处理结束
        isChange3DTileProcessing = false;
    }

    // Change3DTile 管理3D手牌组的变更行为
    public IEnumerator Change3DTileCoroutine(string actionType,int tileId,int removeCount,string PlayerPosition,bool cut_class,int[] combination_mask){
        // Change3DTile 所有类型：
        // InitHandCards 初始化手牌 => ClearHandCardsCoroutine => Get3DTile
        // GetCard 摸牌 => Get3DTile
        // Discard 弃牌 => Set3DTile(discard) => RemoveOtherHandCardsCoroutine
        // Buhua 补花 => Set3DTile(buhua) => RemoveOtherHandCardsCoroutine // 其实补花也有摸打补花的说法，后续需要优化这种情况
        // 吃碰杠 => ActionAnimation(chi_left) => if(PlayerPosition != "self") RemoveOtherHandCardsCoroutine

        // 初始化手牌 
        if (actionType == "InitHandCards"){
            // 先清除所有手牌，确保 childCount 正确
            yield return StartCoroutine(ClearHandCardsCoroutine());
            
            // 然后初始化手牌
            for (int i = 0; i < NormalGameStateManager.Instance.player_to_info["left"].hand_tiles_count; i++){
                Get3DTile("left","init");
            }
            for (int i = 0; i < NormalGameStateManager.Instance.player_to_info["top"].hand_tiles_count; i++){
                Get3DTile("top","init");
            }
            for (int i = 0; i < NormalGameStateManager.Instance.player_to_info["right"].hand_tiles_count; i++){
                Get3DTile("right","init");
            }
        }

        // 摸牌 
        else if (actionType == "GetCard"){
            yield return StartCoroutine(Get3DTileCoroutine(PlayerPosition,"get"));
        }

        // 仅设置弃牌但不删除玩家手牌且无动画的牌谱转移和重连方法
        else if (actionType == "SetDiscardWithoutAnimation"){
            PosPanel3D panel = GetPosPanel(PlayerPosition);
            Set3DTile(tileId, panel.discardsPosition, "Discard", PlayerPosition); // 弃牌区增加弃牌
        }

        // 弃牌
        else if (actionType == "Discard"){
            PosPanel3D panel = GetPosPanel(PlayerPosition);
            if (PlayerPosition != "self"){
                // 等待删除手牌完成，避免同时执行造成帧抖动
                yield return StartCoroutine(RemoveOtherHandCardsCoroutine(panel.cardsPosition, 1, cut_class));
            }
            // 删除完成后再创建弃牌
            yield return StartCoroutine(Set3DTileCoroutine(tileId, panel.discardsPosition, "Discard", PlayerPosition));
        }

        // 仅设置补花但不删除玩家手牌且无动画的牌谱转移和重连方法
        else if (actionType == "SetBuhuacardWithoutAnimation"){
            PosPanel3D panel = GetPosPanel(PlayerPosition);
            yield return StartCoroutine(Set3DTileCoroutine(tileId, panel.buhuaPosition, "Buhua", PlayerPosition)); // 补花区增加补花
        }

        // 补花
        else if (actionType == "Buhua"){
            PosPanel3D panel = GetPosPanel(PlayerPosition);
            if (PlayerPosition != "self"){
                // 等待删除手牌完成，避免同时执行造成帧抖动
                yield return StartCoroutine(RemoveOtherHandCardsCoroutine(panel.cardsPosition, 1, false));
            }
            // 删除完成后再创建补花
            yield return StartCoroutine(Set3DTileCoroutine(tileId, panel.buhuaPosition, "Buhua", PlayerPosition));
        }

        // 吃碰杠
        else if (actionType == "chi_left" || actionType == "chi_mid" || actionType == "chi_right" ||
                 actionType == "peng" || actionType == "gang" || actionType == "angang" || actionType == "jiagang"){   
            // 删除上一张3D卡牌，归还到对象池
            if (lastCut3DObject != null){
                // 加杠和暗杠不需要删除上一张3D卡牌
                if (actionType != "jiagang" || actionType != "angang"){
                    MahjongObjectPool.Instance.Return(-1, lastCut3DObject);
                }
            }
            else{
                Debug.LogWarning("上一张3D卡牌为空");
            }
            PosPanel3D panel = GetPosPanel(PlayerPosition);
            // 放置组合牌
            if (PlayerPosition != "self"){
                // 等待删除手牌完成，避免同时执行造成帧抖动
                yield return StartCoroutine(RemoveOtherHandCardsCoroutine(panel.cardsPosition, removeCount, false));
            }
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
