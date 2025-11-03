using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using System.Threading.Tasks;

public class Game3DManager : MonoBehaviour
{
    [SerializeField] private GameObject tile3DPrefab;    // 3D预制体
    [Header("3D对象生成位置")]
    [SerializeField] private Transform leftCardsPosition; // 左家手牌位置
    [SerializeField] private Transform leftDiscardsPosition; // 左家弃牌位置
    [SerializeField] private Transform leftBuhuaPosition; // 左家补花位置
    [SerializeField] private Transform leftCombinationsPosition; // 左家组合位置
    
    [SerializeField] private Transform topCardsPosition; // 对家手牌位置
    [SerializeField] private Transform topDiscardsPosition; // 对家弃牌位置
    [SerializeField] private Transform topBuhuaPosition; // 对家补花位置
    [SerializeField] private Transform topCombinationsPosition; // 对家组合位置
    
    [SerializeField] private Transform rightCardsPosition; // 右家手牌位置
    [SerializeField] private Transform rightDiscardsPosition; // 右家弃牌位置
    [SerializeField] private Transform rightBuhuaPosition; // 右家补花位置
    [SerializeField] private Transform rightCombinationsPosition; // 右家组合位置

    [SerializeField] private Transform selfCardsPosition; // 自家手牌位置
    [SerializeField] private Transform selfDiscardsPosition; // 自家弃牌位置
    [SerializeField] private Transform selfBuhuaPosition; // 自家补花位置
    [SerializeField] private Transform selfCombinationsPosition; // 自家组合位置

    [SerializeField] private Transform[] selfCombination3DObjects = new Transform[4]; // 自家组合牌3D对象数组
    [SerializeField] private Transform[] leftCombination3DObjects = new Transform[4]; // 左家组合牌3D对象数组
    [SerializeField] private Transform[] topCombination3DObjects = new Transform[4]; // 对家组合牌3D对象数组
    [SerializeField] private Transform[] rightCombination3DObjects = new Transform[4]; // 右家组合牌3D对象数组

    private Vector3 selfSetCombinationsPoint; // 组合指针用于存储各家组合牌生成位置
    private Vector3 leftSetCombinationsPoint;
    private Vector3 topSetCombinationsPoint;
    private Vector3 rightSetCombinationsPoint;

    private GameObject lastCut3DObject; // 最后一张弃牌的3D对象
    private Dictionary<int,Vector3> pengToJiagangPosDict = new Dictionary<int,Vector3>(); // 碰牌的加杠预留指针

    private float cardWidth; // 卡片宽度 组合牌 3D手牌使用
    private float cardHeight; // 卡片高度
    private float cardScale; // 卡片缩放
    private float widthSpacing; // 间距为卡片宽度的1.05倍 弃牌 补花 使用
    private float heightSpacing; // 间距为卡片高度的1.05倍
    // private Quaternion baseRotation = Quaternion.Euler(0, 90, -90); // 卡牌默认平躺转角
    // private Quaternion leftRotation = Quaternion.Euler(0, 90, 0); // 左侧玩家转角
    // private Quaternion topRotation = Quaternion.Euler(0, 180, 0); // 上方玩家转角
    // private Quaternion rightRotation = Quaternion.Euler(0, 270, 0); // 右侧玩家转角
    // private Quaternion erectRotation = Quaternion.Euler(-90, 0, 0); // 竖立卡牌转角
    // private Quaternion TurnWidthRotation = Quaternion.Euler(0,90,0); // 平躺时横置转角

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
    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        // 初始化配置
        this.cardScale = tile3DPrefab.transform.localScale.z; // 卡片缩放比例
        this.cardWidth = tile3DPrefab.GetComponent<Renderer>().bounds.size.y; // 卡片宽度（绿色轴）
        this.cardHeight = tile3DPrefab.GetComponent<Renderer>().bounds.size.z; // 卡片高度（蓝色轴）
        this.widthSpacing = cardWidth * 1.05f; // 间距为卡片宽度的1.05倍
        this.heightSpacing = cardHeight * 1.05f; // 间距为卡片高度的1.05倍
        // 初始化放置组合牌指针
        selfSetCombinationsPoint = selfCombinationsPosition.position;
        leftSetCombinationsPoint = leftCombinationsPosition.position;
        topSetCombinationsPoint = topCombinationsPosition.position;
        rightSetCombinationsPoint = rightCombinationsPosition.position;
        // 初始化世界位置
        RightDirection = new Vector3(1,0,0);
        LeftDirection = new Vector3(-1,0,0);
        FrontDirection = new Vector3(0,0,1);
        BackDirection = new Vector3(0,0,-1);
        UpDirection = new Vector3(0,1,0);
        DownDirection = new Vector3(0,-1,0);
    }



    // 3D手牌处理入口
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
        // 初始化手牌
        if (actionType == "InitHandCards"){
            for (int i = 0; i < GameSceneManager.Instance.player_to_info["left"].hand_tiles_count; i++){
                Get3DTile("left","InitHandCards");
            }
            for (int i = 0; i < GameSceneManager.Instance.player_to_info["top"].hand_tiles_count; i++){
                Get3DTile("top","InitHandCards");
            }
            for (int i = 0; i < GameSceneManager.Instance.player_to_info["right"].hand_tiles_count; i++){
                Get3DTile("right","InitHandCards");
            }
        }
        // 摸牌
        else if (actionType == "GetCard"){
            Get3DTile(PlayerPosition,"GetCard");
        }
        // 弃牌
        else if (actionType == "Discard"){
            if (PlayerPosition == "self"){
                Set3DTile(tileId,selfDiscardsPosition,"Discard","self");
            }
            else if (PlayerPosition == "left"){
                Set3DTile(tileId,leftDiscardsPosition,"Discard","left");
                StartCoroutine(RemoveOtherHandCardsCoroutine(leftCardsPosition,1,cut_class));
            }
            else if (PlayerPosition == "top"){
                Set3DTile(tileId,topDiscardsPosition,"Discard","top");
                StartCoroutine(RemoveOtherHandCardsCoroutine(topCardsPosition,1,cut_class));
            }
            else if (PlayerPosition == "right"){
                Set3DTile(tileId,rightDiscardsPosition,"Discard","right");
                StartCoroutine(RemoveOtherHandCardsCoroutine(rightCardsPosition,1,cut_class));
            }
        }
        // 补花
        else if (actionType == "Buhua"){
            if (PlayerPosition == "self"){
                Set3DTile(tileId,selfBuhuaPosition,"Buhua","self");
            }
            else if (PlayerPosition == "left"){
                Set3DTile(tileId,leftBuhuaPosition,"Buhua","left");
                StartCoroutine(RemoveOtherHandCardsCoroutine(leftCardsPosition,1,false));
            }
            else if (PlayerPosition == "top"){
                Set3DTile(tileId,topBuhuaPosition,"Buhua","top");
                StartCoroutine(RemoveOtherHandCardsCoroutine(topCardsPosition,1,false));
            }
            else if (PlayerPosition == "right"){
                Set3DTile(tileId,rightBuhuaPosition,"Buhua","right");
                StartCoroutine(RemoveOtherHandCardsCoroutine(rightCardsPosition,1,false));
            }
        }
        // 吃碰杠
        else if (actionType == "chi_left" || actionType == "chi_mid" || actionType == "chi_right" || actionType == "peng" || actionType == "gang" || actionType == "angang"){         
            if (PlayerPosition == "self"){
                ActionAnimation(PlayerPosition,actionType,combination_mask);
            }
            else if (PlayerPosition == "left"){
                ActionAnimation(PlayerPosition,actionType,combination_mask);
                StartCoroutine(RemoveOtherHandCardsCoroutine(leftCardsPosition,removeCount,false));
            }
            else if (PlayerPosition == "top"){
                ActionAnimation(PlayerPosition,actionType,combination_mask);
                StartCoroutine(RemoveOtherHandCardsCoroutine(topCardsPosition,removeCount,false));
            }
            else if (PlayerPosition == "right"){
                ActionAnimation(PlayerPosition,actionType,combination_mask);
                StartCoroutine(RemoveOtherHandCardsCoroutine(rightCardsPosition,removeCount,false));
            }
        }
        else if (actionType == "jiagang"){
            if (PlayerPosition == "self"){
                ActionAnimation(PlayerPosition,actionType,combination_mask);
            }
            else if (PlayerPosition == "left"){
                ActionAnimation(PlayerPosition,actionType,combination_mask);
                StartCoroutine(RemoveOtherHandCardsCoroutine(leftCardsPosition,removeCount,false));
            }
            else if (PlayerPosition == "top"){
                ActionAnimation(PlayerPosition,actionType,combination_mask);
                StartCoroutine(RemoveOtherHandCardsCoroutine(topCardsPosition,removeCount,false));
            }
            else if (PlayerPosition == "right"){
                ActionAnimation(PlayerPosition,actionType,combination_mask);
                StartCoroutine(RemoveOtherHandCardsCoroutine(rightCardsPosition,removeCount,false));
            }
        }
        else if (actionType == "angang"){
            if (PlayerPosition == "self"){
                ActionAnimation(PlayerPosition,actionType,combination_mask);
            }
            else if (PlayerPosition == "left"){
                ActionAnimation(PlayerPosition,actionType,combination_mask);
                StartCoroutine(RemoveOtherHandCardsCoroutine(leftCardsPosition,removeCount,false));
            }
            else if (PlayerPosition == "top"){
                ActionAnimation(PlayerPosition,actionType,combination_mask);
                StartCoroutine(RemoveOtherHandCardsCoroutine(topCardsPosition,removeCount,false));
            }
            else if (PlayerPosition == "right"){
                ActionAnimation(PlayerPosition,actionType,combination_mask);
                StartCoroutine(RemoveOtherHandCardsCoroutine(rightCardsPosition,removeCount,false));
            }
        }
        
        yield break;
    }

    // 出牌3D显示
    private void Set3DTile(int tileId,Transform SetPosition,string SetType ,string PlayerPosition){
        Debug.Log($"Set3DTileCoroutine:{tileId} {SetType}, {PlayerPosition}");

        // 获取放置位置
        Vector3 currentPosition = SetPosition.position;
        // 获取放置角度
        Quaternion rotation = Quaternion.identity;

        // 获取不同玩家的相对右侧和相对下侧
        Vector3 widthdirection = Vector3.zero;
        Vector3 heightdirection = Vector3.zero;
        if (PlayerPosition == "self"){
            widthdirection = RightDirection;
            heightdirection = BackDirection;
            rotation = Quaternion.Euler(0, 0, -90);
        }
        else if (PlayerPosition == "left"){
            widthdirection = BackDirection;
            heightdirection = LeftDirection;
            rotation = Quaternion.Euler(0, 90, -90);
        }
        else if (PlayerPosition == "top"){
            widthdirection = LeftDirection;
            heightdirection = FrontDirection;
            rotation = Quaternion.Euler(0, 180, -90);
        }
        else if (PlayerPosition == "right"){
            widthdirection = FrontDirection;
            heightdirection = RightDirection;
            rotation = Quaternion.Euler(0, 270, -90);
        }

        // 获取每行最多放多少张牌
        int cardsPerRow;
        // 弃牌每行最多放 6 张牌
        if (SetType == "Discard"){
            cardsPerRow = 6;
        }
        // 补花每行最多放 8 张牌
        else if (SetType == "Buhua"){
            cardsPerRow = 8;
        }
        else{
            Debug.LogError($"SetType {SetType} 错误");
            cardsPerRow = 10; // 默认值
        }
    
        // 将 discardCount 映射到从 0 开始的索引（第1张是0，第6张是5，第7张是6 → 行1列0）
        int index = SetPosition.childCount;
        // 计算当前是第几行、第几列
        int row = index / cardsPerRow;     // 整除：行号
        int col = index % cardsPerRow;     // 取余：列号

        currentPosition += widthdirection.normalized * widthSpacing * col;
        currentPosition += heightdirection.normalized * heightSpacing * row;


        Debug.Log($"创建卡片 {SetPosition.childCount}, 牌ID: {tileId}");
        // 创建麻将牌预制体
        GameObject cardObj = Instantiate(tile3DPrefab, currentPosition, rotation);
        lastCut3DObject = cardObj;
        // 设置父对象
        cardObj.transform.SetParent(SetPosition, worldPositionStays: true);
        cardObj.name = $"Card_{SetPosition.childCount}";
        
        // 加载并应用材质
        ApplyCardTexture(cardObj, tileId);
    }

    // 摸牌3D显示
    private void Get3DTile(string playerIndex,string ActionType){
        // 如果玩家是其他玩家，则将牌添加到他人手牌中
        string SetName = "None";
        if (ActionType == "InitHandCards"){
            if (playerIndex == "left"){
                SetName = $"Card_{leftCardsPosition.childCount}";
            }
            else if (playerIndex == "top"){
                SetName = $"Card_{topCardsPosition.childCount}";
            }
            else if (playerIndex == "right"){
                SetName = $"Card_{rightCardsPosition.childCount}";
            }
        }
        else if (ActionType == "GetCard"){
            SetName = $"Card_Current";
        }

        if (playerIndex == "left"){
            Quaternion rotation = Quaternion.Euler(-90,0,0); // 面朝左侧
            // 摸牌生成位置 = 玩家手牌起始点 + (3D卡牌数量+1)*宽度间距*后方向
            Vector3 SetPosition = leftCardsPosition.position + (leftCardsPosition.childCount+1) * cardWidth * BackDirection;
            GameObject cardObj = Instantiate(tile3DPrefab, SetPosition, rotation);
            cardObj.transform.SetParent(leftCardsPosition, worldPositionStays: true);
            cardObj.name = SetName;
        }
        else if (playerIndex == "top"){
            Quaternion rotation = Quaternion.Euler(-90,0,90); // 面朝前侧
            Vector3 SetPosition = topCardsPosition.position + (topCardsPosition.childCount+1) * cardWidth * LeftDirection;
            GameObject cardObj = Instantiate(tile3DPrefab, SetPosition, rotation);
            cardObj.transform.SetParent(topCardsPosition, worldPositionStays: true);
            cardObj.name = SetName;
        }
        else if (playerIndex == "right"){
            Quaternion rotation = Quaternion.Euler(-90,0,180); // 面朝右侧
            Vector3 SetPosition = rightCardsPosition.position + (rightCardsPosition.childCount+1) * cardWidth * FrontDirection;
            GameObject cardObj = Instantiate(tile3DPrefab, SetPosition, rotation);
            cardObj.transform.SetParent(rightCardsPosition, worldPositionStays: true);
            cardObj.name = SetName;
        }
    }

    // 鸣牌3D显示
    public void ActionAnimation(string playerIndex,string actionType,int[]combination_mask){
        // 根据actionType执行动画
        Quaternion rotation = Quaternion.identity; // 卡牌旋转角度
        Vector3 SetDirection = Vector3.zero; // 放置方向
        Vector3 SetPositionpoint = Vector3.zero; // 放置位置
        Vector3 JiagangDirection = Vector3.zero; // 加杠方向
        Transform SetParent = null; // 设置父对象
        if (playerIndex == "self"){
            rotation = Quaternion.Euler(0, 0, -90); // 获取卡牌旋转角度 
            SetDirection = LeftDirection; // 获取放置方向 向左
            JiagangDirection = FrontDirection; // 自家加杠指针是向前
            SetPositionpoint = selfSetCombinationsPoint; // 获取放置指针
            // 获取父对象 父对象 = 玩家组合数 => 玩家组合父对象列表
            SetParent = selfCombination3DObjects[GameSceneManager.Instance.player_to_info["self"].combination_tiles.Count];
        }
        else if (playerIndex == "left"){
            rotation =  Quaternion.Euler(0, 90, -90); // 左侧玩家
            SetDirection = FrontDirection; // 向前
            JiagangDirection = RightDirection; // 左侧玩家加杠指针是向右
            SetPositionpoint = leftSetCombinationsPoint;
            SetParent = leftCombination3DObjects[GameSceneManager.Instance.player_to_info["left"].combination_tiles.Count];
        }
        else if (playerIndex == "top"){
            rotation =  Quaternion.Euler(0, 180, -90); // 上方玩家
            SetDirection = RightDirection; // 向右
            JiagangDirection = BackDirection; // 上方玩家加杠指针是向后
            SetPositionpoint = topSetCombinationsPoint;
            SetParent = topCombination3DObjects[GameSceneManager.Instance.player_to_info["top"].combination_tiles.Count];
        }
        else if (playerIndex == "right"){
            rotation =  Quaternion.Euler(0, 270, -90); // 右侧玩家
            SetDirection = BackDirection; // 向后
            JiagangDirection = LeftDirection; // 右侧玩家加杠指针是向左
            SetPositionpoint = rightSetCombinationsPoint;
            SetParent = rightCombination3DObjects[GameSceneManager.Instance.player_to_info["right"].combination_tiles.Count];
        }
        // 获取了rotation(卡牌旋转角度) SetDirection(放置方向) 以及公共变量 $SetCombinationsPoint
        List<int> SetTileList = new List<int>();
        List<int> SignDirectionList = new List<int>();

        // 解码combination_mask 获得需要放置的卡牌列表和卡牌朝向列表
        foreach (int tileId in combination_mask){
            if (tileId >= 10){
                SetTileList.Add(tileId);
            }
            else if (tileId < 5){
                SignDirectionList.Add(tileId);
            }
        }
        // 倒转SetTileList和SignDirectionList 因为卡牌的逻辑顺序是从左到右，但我们需要从右到左放置
        SetTileList.Reverse();
        SignDirectionList.Reverse();
        Debug.Log($"SetTileList: {SetTileList}");
        Debug.Log($"SignDirectionList: {SignDirectionList}");

        // 执行动画
        // 加杠
        if (actionType == "jiagang"){
             for (int i = 0; i < SetTileList.Count; i++) {
                if (SignDirectionList[i] == 3){
                    GameObject cardObj;
                    Vector3 TempPositionpoint = pengToJiagangPosDict[SetTileList[i]]; // 获取加杠位置
                    Quaternion TempRotation = Quaternion.Euler(0,90,0) * rotation; // 横
                    TempPositionpoint += JiagangDirection * cardWidth; // 加杠向上一个宽度单位
                    cardObj = Instantiate(tile3DPrefab, TempPositionpoint, TempRotation);
                    ApplyCardTexture(cardObj, SetTileList[i]);
                    // 设置父对象
                    cardObj.transform.SetParent(SetParent, worldPositionStays: true);
                }
            }
        }
        // 正常放置卡牌
        else{
            for (int i = 0; i < SetTileList.Count; i++) {
                GameObject cardObj;
                Quaternion TempRotation = Quaternion.identity;
                Vector3 TempPositionpoint = SetPositionpoint;
                // 0代表竖 1代表横 2代表暗面 3代表上侧(加杠) 4代表空
                // 卡牌竖置 指针增加一个宽度单位
                if (SignDirectionList[i] == 0){
                    TempRotation = rotation; // 竖
                    TempPositionpoint += SetDirection * cardWidth;
                    SetPositionpoint += SetDirection * cardWidth; // 吃碰的竖置牌每张牌向左一个宽度单位
                }
                // 卡牌横置,放置角度叠加横置 指针增加一个高度单位
                else if (SignDirectionList[i] == 1){
                    TempRotation = Quaternion.Euler(0,90,0) * rotation; // 横
                    SetPositionpoint += SetDirection * cardHeight;
                    TempPositionpoint += SetDirection * (cardHeight + ((cardHeight - cardWidth) * 0.5f)); // 吃碰的横置牌向左一个高度单位
                    TempPositionpoint += JiagangDirection * cardWidth * 0.4f; // 横置牌向上0.4个宽度单位
                    if (actionType == "peng"){
                        pengToJiagangPosDict.Add(SetTileList[i],TempPositionpoint); // 碰牌的加杠预留指针 保存在碰牌int id的横置位置
                    }
                }
                // 卡牌暗面 指针增加一个宽度单位
                else if (SignDirectionList[i] == 2){
                    TempRotation = Quaternion.Euler(0,0,-180) * rotation; // 暗面
                    SetPositionpoint += SetDirection * cardWidth;
                    TempPositionpoint += SetDirection * cardWidth; // 暗杠每张牌向左一个宽度单位
                }
                // 卡牌加杠
                else if (SignDirectionList[i] == 3){
                    TempRotation = Quaternion.Euler(0,90,0) * rotation; // 横
                    TempPositionpoint += JiagangDirection * cardWidth * 1.4f; // 加杠向上1.4个宽度单位
                    SetPositionpoint += SetDirection * cardWidth; // 更新指针为下一张牌的位置
                }

                // 创建卡牌
                cardObj = Instantiate(tile3DPrefab, TempPositionpoint, TempRotation);
                // 设置卡牌纹理
                ApplyCardTexture(cardObj, SetTileList[i]);
                // 设置父对象
                cardObj.transform.SetParent(SetParent, worldPositionStays: true);
            }

            // 将更新后的指针位置赋值给公共变量
            if (playerIndex == "self"){
                selfSetCombinationsPoint = SetPositionpoint;
            }
            else if (playerIndex == "left"){
                leftSetCombinationsPoint = SetPositionpoint;
            }
            else if (playerIndex == "top"){
                topSetCombinationsPoint = SetPositionpoint;
            }
            else if (playerIndex == "right"){
                rightSetCombinationsPoint = SetPositionpoint;
            }
        }
    }

    // 移除3D手牌显示
    private IEnumerator RemoveOtherHandCardsCoroutine(Transform cardPosition, int removeCount, bool cut_class){
        Debug.Log($"移除其他玩家手牌 {cardPosition}, 删除数量: {removeCount}, 摸切: {cut_class}");

        // 如果removeCount > 1，使用组合删除方法
        if (removeCount > 1){
            // 计算当前卡牌总数
            int totalCardCount = cardPosition.childCount;
            
            // 检查是否有足够的卡牌可以删除
            if (totalCardCount < removeCount){
                Debug.LogWarning($"卡牌数量不足: 需要删除{removeCount}张，但只有{totalCardCount}张");
                yield break;
            }
            
            // 计算可以开始删除的随机起始位置
            int maxStartIndex = totalCardCount - removeCount;
            int startIndex = Random.Range(0, maxStartIndex + 1);
            
            Debug.Log($"开始组合删除卡牌: 总数={totalCardCount}, 删除数量={removeCount}, 起始索引={startIndex}");
            
            // 从起始位置开始连续删除指定数量的卡牌
            for (int i = 0; i < removeCount; i++){
                int cardIndex = startIndex + i;
                
                // 检查索引是否有效
                if (cardIndex < cardPosition.childCount){
                    Transform cardToRemove = cardPosition.GetChild(cardIndex);
                    Debug.Log($"删除卡牌: {cardToRemove.name} (索引: {cardIndex})");
                    
                    // 销毁卡牌对象
                    Destroy(cardToRemove.gameObject);
                }
            }
            Debug.Log($"组合删除完成: 已删除{removeCount}张卡牌");
        }
        // 如果removeCount <= 1 或为null，使用原始方法
        else{
            // 如果摸切就直接删除摸到的牌
            if (cut_class){
                Transform cardTransform = cardPosition.Find("Card_Current");
                if (cardTransform != null) {
                    Debug.Log($"删除摸牌区卡牌: {cardTransform.name}");
                    Destroy(cardTransform.gameObject);
                }
            }
            // 如果摸牌则随机删除一张主牌区的牌，将摸牌区的卡牌加入手牌区
            else{
                // 计算子物体数量，获取随机索引，随机删除选中的子物体
                int childCount = cardPosition.childCount;
                int randomIndex = UnityEngine.Random.Range(0, childCount);
                Transform randomChild = cardPosition.GetChild(randomIndex);
                Debug.Log($"随机删除了索引为 {randomIndex} 的牌");
                Destroy(randomChild.gameObject);

                // 等待1秒 模拟玩家将摸牌区卡牌放入手牌区
                yield return new WaitForSeconds(1f);
                        
                // 确定方向向量 方向向量乘以spacing等于目标位置
                Vector3 direction = Vector3.zero;
                if (cardPosition == rightCardsPosition)
                    direction = new Vector3(0, 0, 1); // 向前
                else if (cardPosition == leftCardsPosition)
                    direction = new Vector3(0, 0, -1); // 向后
                else if (cardPosition == topCardsPosition)
                    direction = new Vector3(-1, 0, 0); // 向左
                
                // 获取所有剩余子物体并按照名称索引排序[0,1,2,3,4,5,6,7,8,9,10,11,12]
                List<Transform> remainingCards = new List<Transform>();
                int cardCount = cardPosition.childCount;
                for (int i = 0; i < cardCount; i++) {
                    remainingCards.Add(cardPosition.GetChild(i));
                }
                // 拿取手牌初始位置，遍历卡牌乘以spacing移动到目标位置
                Vector3 startPosition = cardPosition.position;
                for (int i = 0; i < cardCount; i++) {
                    Vector3 newPosition = startPosition + direction * cardWidth * (i + 0);
                    remainingCards[i].position = newPosition;
                    remainingCards[i].name = $"ReSeTCard_{i}";
                }
            }
        }
    }

    // 应用牌面纹理
    private void ApplyCardTexture(GameObject cardObj, int tileId) {

        // 从Resources加载对应ID的图片
        Texture2D texture = Resources.Load<Texture2D>($"image/CardFaceMaterial_xuefun/{tileId}");
        if (texture == null) {
            Debug.LogError($"无法加载纹理: image/CardFaceMaterial_xuefun/{tileId}");
            return;
        }
        
        // 获取预制体的渲染器
        Renderer renderer = cardObj.GetComponent<Renderer>();
        
        // 通过材质名称找到SetImage材质
        int setImageMaterialIndex = -1;
        for (int i = 0; i < renderer.materials.Length; i++) {
            if (renderer.materials[i].name.Contains("SetImage")) {
                setImageMaterialIndex = i;
                break;
            }
        }
        
        if (setImageMaterialIndex != -1) {
            renderer.materials[setImageMaterialIndex].mainTexture = texture;
            Debug.Log($"应用纹理到卡片 {tileId} 的SetImage材质完成");
        } else {
            Debug.LogError($"未找到SetImage材质，材质列表: {string.Join(", ", System.Array.ConvertAll(renderer.materials, m => m.name))}");
        }
    }

    // 清除3D手牌
    public void Clear3DTile(){
        // 重置组合牌指针
        selfSetCombinationsPoint = selfCombinationsPosition.position;
        leftSetCombinationsPoint = leftCombinationsPosition.position;
        topSetCombinationsPoint = topCombinationsPosition.position;
        rightSetCombinationsPoint = rightCombinationsPosition.position;
        pengToJiagangPosDict.Clear();
        foreach (Transform child in leftCardsPosition){
            Destroy(child.gameObject);
        }
        foreach (Transform child in topCardsPosition){
            Destroy(child.gameObject);
        }
        foreach (Transform child in rightCardsPosition){
            Destroy(child.gameObject);
        }
        foreach (Transform child in selfCardsPosition){
            Destroy(child.gameObject);
        }
        foreach (Transform child in leftDiscardsPosition){
            Destroy(child.gameObject);
        }
        foreach (Transform child in topDiscardsPosition){
            Destroy(child.gameObject);
        }
        foreach (Transform child in rightDiscardsPosition){
            Destroy(child.gameObject);
        }
        foreach (Transform child in selfDiscardsPosition){
            Destroy(child.gameObject);
        }
        foreach (Transform child in leftBuhuaPosition){
            Destroy(child.gameObject);
        }
        foreach (Transform child in topBuhuaPosition){
            Destroy(child.gameObject);
        }
        foreach (Transform child in rightBuhuaPosition){
            Destroy(child.gameObject);
        }
        foreach (Transform child in selfBuhuaPosition){
            Destroy(child.gameObject);
        }
        foreach (Transform child in leftCombinationsPosition){
            Destroy(child.gameObject);
        }
        foreach (Transform child in topCombinationsPosition){
            Destroy(child.gameObject);
        }
        foreach (Transform child in rightCombinationsPosition){
            Destroy(child.gameObject);
        }
        foreach (Transform child in selfCombinationsPosition){
            Destroy(child.gameObject);
        }
    }

}
