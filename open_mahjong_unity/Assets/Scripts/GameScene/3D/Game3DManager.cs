using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using System.Threading.Tasks;

public class Game3DManager : MonoBehaviour {
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
    private void Awake() {
        if (Instance != null && Instance != this) {
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
            for (int i = 0; i < GameSceneManager.Instance.player_to_info["left"].hand_tiles_count; i++){
                Get3DTile("left","init");
            }
            for (int i = 0; i < GameSceneManager.Instance.player_to_info["top"].hand_tiles_count; i++){
                Get3DTile("top","init");
            }
            for (int i = 0; i < GameSceneManager.Instance.player_to_info["right"].hand_tiles_count; i++){
                Get3DTile("right","init");
            }
        }

        // 摸牌 
        else if (actionType == "GetCard"){
            Get3DTile(PlayerPosition,"get");
        }

        // 弃牌
        else if (actionType == "Discard"){
            PosPanel3D panel = GetPosPanel(PlayerPosition);
            Set3DTile(tileId, panel.discardsPosition, "Discard", PlayerPosition); // 弃牌区增加弃牌
            if (PlayerPosition != "self"){
                StartCoroutine(RemoveOtherHandCardsCoroutine(panel.cardsPosition, 1, cut_class)); // 手牌区删除手牌
            }
        }

        // 补花
        else if (actionType == "Buhua"){
            PosPanel3D panel = GetPosPanel(PlayerPosition);
            Set3DTile(tileId, panel.buhuaPosition, "Buhua", PlayerPosition); // 补花区增加补花
            if (PlayerPosition != "self"){
                StartCoroutine(RemoveOtherHandCardsCoroutine(panel.cardsPosition, 1, false)); // 手牌区删除手牌
            }
        }

        // 吃碰杠
        else if (actionType == "chi_left" || actionType == "chi_mid" || actionType == "chi_right" ||
                 actionType == "peng" || actionType == "gang" || actionType == "angang" || actionType == "jiagang"){    
            PosPanel3D panel = GetPosPanel(PlayerPosition);
            ActionAnimation(PlayerPosition, actionType, combination_mask,true); // 放置组合牌
            if (PlayerPosition != "self"){
                StartCoroutine(RemoveOtherHandCardsCoroutine(panel.cardsPosition, removeCount, false)); // 手牌区删除手牌
            }
        }
        
        yield break;
    }

    // 放置3D卡牌 id-位置-类型-玩家位置
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
    private void Get3DTile(string playerIndex,string actionType){

        PosPanel3D panel = GetPosPanel(playerIndex);
        Transform cardsPosition = panel.cardsPosition; 
        
        // 根据玩家位置设置对应的旋转角度和方向
        Quaternion rotation = Quaternion.identity;
        Vector3 direction = Vector3.zero;
        
        if (playerIndex == "left"){
            rotation = Quaternion.Euler(-90, 0, 0); // 面朝左侧
            direction = BackDirection; // 向后
        }
        else if (playerIndex == "top"){
            rotation = Quaternion.Euler(-90, 0, 90); // 面朝前侧
            direction = LeftDirection; // 向左
        }
        else if (playerIndex == "right"){
            rotation = Quaternion.Euler(-90, 0, 180); // 面朝右侧
            direction = FrontDirection; // 向前
        }
        else{
            Debug.LogWarning($"未知的玩家位置: {playerIndex}");
            return;
        }
        
        // 初始化牌生成位置 = 玩家手牌起始点 + (3D卡牌数量)*宽度间距*方向
        Vector3 spawnPosition = Vector3.zero;
        if (actionType == "init"){
            spawnPosition = cardsPosition.position + (cardsPosition.childCount) * cardWidth * direction;
        }
        // 摸牌生成位置 = 玩家手牌起始点 + (3D卡牌数量+1)*宽度间距*方向
        else if (actionType == "get"){
            spawnPosition = cardsPosition.position + (cardsPosition.childCount + 1) * cardWidth * direction;
        }
        GameObject cardObj = Instantiate(tile3DPrefab, spawnPosition, rotation);
        cardObj.transform.SetParent(cardsPosition, worldPositionStays: true);
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

    // 鸣牌3D显示
    public void ActionAnimation(string playerIndex,string actionType,int[]combination_mask,bool doAnimation = false){
        // 根据actionType执行动画
        Quaternion rotation = Quaternion.identity; // 卡牌旋转角度
        Vector3 SetDirection = Vector3.zero; // 放置方向
        Vector3 SetPositionpoint = Vector3.zero; // 放置位置
        Vector3 JiagangDirection = Vector3.zero; // 加杠方向
        Transform SetParent = null; // 设置父对象
        PosPanel3D panel = GetPosPanel(playerIndex);
        if (panel == null) return;
        
        if (playerIndex == "self"){
            rotation = Quaternion.Euler(0, 0, -90); // 获取卡牌旋转角度 
            SetDirection = LeftDirection; // 获取放置方向 向左
            JiagangDirection = FrontDirection; // 自家加杠指针是向前
            SetPositionpoint = selfSetCombinationsPoint; // 获取放置指针
            // 获取父对象 父对象 = 玩家组合数 => 玩家组合父对象列表
            SetParent = panel.combination3DObjects[GameSceneManager.Instance.player_to_info["self"].combination_tiles.Count - 1];
        }
        else if (playerIndex == "left"){
            rotation =  Quaternion.Euler(0, 90, -90); // 左侧玩家
            SetDirection = FrontDirection; // 向前
            JiagangDirection = RightDirection; // 左侧玩家加杠指针是向右
            SetPositionpoint = leftSetCombinationsPoint;
            SetParent = panel.combination3DObjects[GameSceneManager.Instance.player_to_info["left"].combination_tiles.Count - 1];
        }
        else if (playerIndex == "top"){
            rotation =  Quaternion.Euler(0, 180, -90); // 上方玩家
            SetDirection = RightDirection; // 向右
            JiagangDirection = BackDirection; // 上方玩家加杠指针是向后
            SetPositionpoint = topSetCombinationsPoint;
            SetParent = panel.combination3DObjects[GameSceneManager.Instance.player_to_info["top"].combination_tiles.Count - 1];
        }
        else if (playerIndex == "right"){
            rotation =  Quaternion.Euler(0, 270, -90); // 右侧玩家
            SetDirection = BackDirection; // 向后
            JiagangDirection = LeftDirection; // 右侧玩家加杠指针是向左
            SetPositionpoint = rightSetCombinationsPoint;
            SetParent = panel.combination3DObjects[GameSceneManager.Instance.player_to_info["right"].combination_tiles.Count - 1];
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
        Debug.Log($"actionType: {actionType}, combination_mask: {combination_mask}, SetTileList: {SetTileList}, SignDirectionList: {SignDirectionList}");

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
                    // 加杠动画：将加杠牌移动到3个卡牌宽度以左，然后移回原位
                    if (doAnimation){
                        StartCoroutine(MoveCardAnimation(cardObj, SetDirection, cardWidth));
                    }
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
                    // 加杠牌在加杠中单独处理，如果生成加杠牌，则调用一次peng一次jiagang即可，掩码操作会自动互相屏蔽
                    continue;
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
            
            // 组合牌动画：将父物体移动到3个卡牌宽度以左，然后移回原位
            if (doAnimation){
                StartCoroutine(MoveCardAnimation(SetParent.gameObject, SetDirection, cardWidth));
            }
        }

    }

    // 卡牌移动动画：将物体移动鸣牌预备位左侧，然后线性移回原位
    private IEnumerator MoveCardAnimation(GameObject targetObj, Vector3 direction, float cardWidth){
        if (targetObj == null) yield break;
        
        // 左方向
        Vector3 leftDirection = direction;
        
        // 计算目标位置（3个卡牌宽度以左）
        Vector3 originalPosition = targetObj.transform.position;
        Vector3 targetPosition = originalPosition + leftDirection * (cardWidth * 3f);
        
        // 先移动到左侧位置
        targetObj.transform.position = targetPosition;
        
        // 等待一帧，确保卡牌已创建
        yield return null;
        
        // 在原地停顿0.1秒
        yield return new WaitForSeconds(0.1f);
        
        // 在0.15秒内线性移回原位
        float duration = 0.15f;
        float elapsed = 0f;
        
        while (elapsed < duration){
            elapsed += Time.deltaTime;
            float t = elapsed / duration;
            targetObj.transform.position = Vector3.Lerp(targetPosition, originalPosition, t);
            yield return null;
        }
        
        // 确保最终位置准确
        targetObj.transform.position = originalPosition;
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
            // 如果摸切就删除最后一张牌（倒数第一张）
            if (cut_class){
                int childCount = cardPosition.childCount;
                if (childCount > 0){
                    int lastIndex = childCount - 1;
                    Transform lastCard = cardPosition.GetChild(lastIndex);
                    Debug.Log($"删除最后一张卡牌: {lastCard.name} (索引: {lastIndex})");
                    Destroy(lastCard.gameObject);
                }
                else{
                    Debug.LogWarning($"摸切：无法删除最后一张牌");
                }
            }
            // 如果手切则随机删除一张主牌区的牌，将摸牌区的卡牌加入手牌区
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
                if (cardPosition == rightPosPanel.cardsPosition)
                    direction = new Vector3(0, 0, 1); // 向前
                else if (cardPosition == leftPosPanel.cardsPosition)
                    direction = new Vector3(0, 0, -1); // 向后
                else if (cardPosition == topPosPanel.cardsPosition)
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

    // 清除手牌协程（用于在初始化前清除，确保 childCount 正确）
    private IEnumerator ClearHandCardsCoroutine(){
        // 使用倒序遍历，立即销毁所有手牌
        // 左家手牌
        for (int i = leftPosPanel.cardsPosition.childCount - 1; i >= 0; i--){
            Transform child = leftPosPanel.cardsPosition.GetChild(i);
            if (child != null){
                Destroy(child.gameObject);
            }
        }
        // 对家手牌
        for (int i = topPosPanel.cardsPosition.childCount - 1; i >= 0; i--){
            Transform child = topPosPanel.cardsPosition.GetChild(i);
            if (child != null){
                Destroy(child.gameObject);
            }
        }
        // 右家手牌
        for (int i = rightPosPanel.cardsPosition.childCount - 1; i >= 0; i--){
            Transform child = rightPosPanel.cardsPosition.GetChild(i);
            if (child != null){
                Destroy(child.gameObject);
            }
        }
        // 自家手牌
        for (int i = selfPosPanel.cardsPosition.childCount - 1; i >= 0; i--){
            Transform child = selfPosPanel.cardsPosition.GetChild(i);
            if (child != null){
                Destroy(child.gameObject);
            }
        }
        
        // 等待一帧，确保所有对象都被销毁，childCount 更新
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
        
        // 清除所有面板的弃牌
        foreach (Transform child in leftPosPanel.discardsPosition){
            Destroy(child.gameObject);
        }
        foreach (Transform child in topPosPanel.discardsPosition){
            Destroy(child.gameObject);
        }
        foreach (Transform child in rightPosPanel.discardsPosition){
            Destroy(child.gameObject);
        }
        foreach (Transform child in selfPosPanel.discardsPosition){
            Destroy(child.gameObject);
        }
        
        // 清除所有面板的补花
        foreach (Transform child in leftPosPanel.buhuaPosition){
            Destroy(child.gameObject);
        }
        foreach (Transform child in topPosPanel.buhuaPosition){
            Destroy(child.gameObject);
        }
        foreach (Transform child in rightPosPanel.buhuaPosition){
            Destroy(child.gameObject);
        }
        foreach (Transform child in selfPosPanel.buhuaPosition){
            Destroy(child.gameObject);
        }
        
        // 删除所有面板的组合牌3D对象数组中的子物体
        foreach (PosPanel3D panel in new[] { selfPosPanel, leftPosPanel, topPosPanel, rightPosPanel })
        {
            foreach (Transform combinationObj in panel.combination3DObjects)
            {
                if (combinationObj != null)
                {
                    foreach (Transform child in combinationObj)
                    {
                        Destroy(child.gameObject);
                    }
                }
            }
        }
    }

}
