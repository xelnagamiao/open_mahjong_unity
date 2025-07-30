using System.Threading;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using System.Collections.Generic;
using System.Collections;
using UnityEngine.Rendering;
using System.Threading.Tasks;
using System.Text.RegularExpressions;
using System.Runtime.CompilerServices;
using JetBrains.Annotations;
using System.Linq;


public class GameSceneManager : MonoBehaviour
{
    public static GameSceneManager Instance { get; private set; }

    // 玩家位置信息 int[0,1,2,3] → string[self,left,top,right]
    public Dictionary<int, string> indexToPosition = new Dictionary<int, string>();

    // 房间信息
    public int roomId; // 房间ID
    public int selfIndex; // 自身索引
    public int currentIndex; // 目前进行操作的索引
    public int selfStepTime; // 步时
    public int selfRoundTime; // 局时
    public int selfRemainingTime; // 剩余时间
    public bool tips; // 提示
    public List<string> allowActionList = new List<string>(); // 允许操作列表

    // 玩家信息
    public List<int> selfDiscardslist = new List<int>(); // 弃牌列表
    public List<int> leftDiscardslist = new List<int>();
    public List<int> topDiscardslist = new List<int>();
    public List<int> rightDiscardslist = new List<int>();

    public List<int> selfHuapaiList = new List<int>(); // 花牌列表
    public List<int> leftHuapaiList = new List<int>();
    public List<int> topHuapaiList = new List<int>();
    public List<int> rightHuapaiList = new List<int>();

    public List<string> selfCombinationList = new List<string>(); // 组合牌列表
    public List<string> leftCombinationList = new List<string>();
    public List<string> topCombinationList = new List<string>();
    public List<string> rightCombinationList = new List<string>();

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    public void InitializeGame(bool success, string message, GameInfo gameInfo){
        // 1.存储初始化信息
        InitializeSetInfo(gameInfo);
        // 2.初始化UI
        GameCanvas.Instance.InitializeUIInfo(gameInfo,indexToPosition);
        // 3.初始化面板
        BoardCanvas.Instance.InitializeBoardInfo(gameInfo,indexToPosition);
        // 4.初始化手牌区域 由于手牌信息必定是单独发送的，所以这里直接初始化
        GameCanvas.Instance.InitializeHandCards(gameInfo.self_hand_tiles,gameInfo.current_player_index);
        // 5.初始化他人手牌区域
        Game3DManager.Instance.InitializeOtherCards(gameInfo.players_info);
        // 6.如果当前玩家是自己,说明自己是庄家,进行摸牌
        if (selfIndex == currentIndex){
            GameCanvas.Instance.GetCard(gameInfo.self_hand_tiles[gameInfo.self_hand_tiles.Length - 1]);
            // 在这里可以添加向服务器传递加载完成方法
            // 亲家与闲家完成配牌以后等待服务器传递补花行为
        }
    }

    // 询问手牌操作 手牌操作包括 切牌 补花 胡 暗杠 加杠
    public void AskHandAction(int remaining_time,int tileId,int playerIndex,int remain_tiles,string[] action_list){
        foreach (string action in action_list){
            if (action == "cut"){
                allowActionList.Add("cut");
            }
            else if (action == "buhua"){
                allowActionList.Add("buhua");
            }
            else if (action == "hu"){
                allowActionList.Add("hu");
            }
            else if (action == "angang"){
                allowActionList.Add("angang");
            }
            else if (action == "jiagang"){
                allowActionList.Add("jiagang");
            }
        }







        float cardWidth = tile3DPrefab.GetComponent<Renderer>().bounds.size.y;
        float spacing = cardWidth * 1f; // 间距为卡片宽度的1倍

        Debug.Log($"获取牌 {tileId},{playerIndex}");
        RefreshPlayerAnimation(playerIndex,action_list);
        remiansTilesText.text = $"余: {remain_tiles}";

        string GetCardPlayer = player_local_position[playerIndex];
        // 如果玩家是自己，则将牌添加到手牌中
        if (GetCardPlayer == "self"){
            GameObject cardObj = Instantiate(tileCardPrefab, GetCardsContainer);
            TileCard tileCard = cardObj.GetComponent<TileCard>();
            tileCard.SetTile(tileId, true);
            // 询问手牌操作 开启倒计时
            AskOtherCutAction(remaining_time,action_list,tileId);
        }
        // 如果玩家是其他玩家，则将牌添加到他人手牌中
        else if (GetCardPlayer == "left"){
            Quaternion rotation = Quaternion.Euler(90, 0, 0);
            Vector3 SetPosition = leftCardsPosition.position + (leftCardsPosition.childCount+2) * spacing * new Vector3(0,0,-1);
            GameObject cardObj = Instantiate(tile3DPrefab, SetPosition, rotation);
            cardObj.transform.SetParent(leftCardsPosition, worldPositionStays: true);
            cardObj.name = $"Card_Current";
        }
        else if (GetCardPlayer == "top"){
            Quaternion rotation = Quaternion.Euler(90, 0, -90);
            Vector3 SetPosition = topCardsPosition.position + (topCardsPosition.childCount+2) * spacing * new Vector3(-1,0,0);
            GameObject cardObj = Instantiate(tile3DPrefab, SetPosition, rotation);
            cardObj.transform.SetParent(topCardsPosition, worldPositionStays: true);
            cardObj.name = $"Card_Current";
        }
        else if (GetCardPlayer == "right"){
            Quaternion rotation = Quaternion.Euler(90, 0, 180);
            Vector3 SetPosition = rightCardsPosition.position + (rightCardsPosition.childCount+2) * spacing * new Vector3(0,0,1);
            GameObject cardObj = Instantiate(tile3DPrefab, SetPosition, rotation);
            cardObj.transform.SetParent(rightCardsPosition, worldPositionStays: true);
            cardObj.name = $"Card_Current";
        }
    }

    // 出牌后他家反馈操作
    public void AskOtherHandAction(int remaining_time,string[] action_list,int cut_tile){
        lastCutTile = cut_tile;
        // 如果列表中有服务器提供的可用操作，则显示倒计时
        if (action_list.Length > 0){
            loadingRemianTime(remaining_time, currentCutTime);
        }
    }


    // 执行操作
    public void DoAction(string actionType,int tileId,int playerIndex,bool cut_class){
        switch (actionType){
            case "cut":
                CutCards(tileId,playerIndex,cut_class);
                break;
            case "buhua":
                break;
            case "hu":
                break;
            case "angang":
                break;
            case "jiagang":
                break;
        }



    public void CutCards(int tileId,int playerIndex,bool cut_class){
        Debug.Log($"{playerIndex},{selfCurrentIndex}");
        // 如果出牌玩家和当前玩家相同，并且没有进行过操作
        if (playerIndex == selfCurrentIndex && CutAction == true){
            CutAction=false;
            if (cut_class){ // 摸切有两种可能 手动则卡牌自动消除 超时则需要验证消除
                if (GetCardsContainer.childCount > 0) {
                    GameObject GetCardObj = GetCardsContainer.GetChild(0).gameObject;
                    Destroy(GetCardObj);
                }
            }
            else{ // 手切则手动找到摸到的牌加入手牌
                if (GetCardsContainer.childCount > 0) {
                GameObject GetCardObj = GetCardsContainer.GetChild(0).gameObject;
                    int GetTileId = GetCardObj.GetComponent<TileCard>().tileId;
                Destroy(GetCardObj);
                GameObject cardObj = Instantiate(tileCardPrefab, handCardsContainer);
                TileCard tileCard = cardObj.GetComponent<TileCard>();
                    tileCard.SetTile(GetTileId, false);
                }
            }
            // 添加null检查，防止_countdownCoroutine为null时抛出异常
            if (_countdownCoroutine != null) {
                StopCoroutine(_countdownCoroutine);
                _countdownCoroutine = null; // 设置为null以避免重复停止
            }
            remianTimeText.text = $""; // 隐藏倒计时文本
            DisCardAnimation(tileId,selfDiscardsPosition,new Vector3(1,0,0),new Vector3(0,0,-1),"self",selfDiscardslist.Count);
            selfDiscardslist.Add(tileId);
            ArrangeHandCards();
        }
        if (selfCurrentIndex == 0){
            if (playerIndex == 1){
                DisCardAnimation(tileId,rightDiscardsPosition,new Vector3(0,0,1),new Vector3(1,0,0),"right",rightDiscardslist.Count);
                removeOtherHandCards(rightCardsPosition,cut_class);
                rightDiscardslist.Add(tileId);
            }
            else if (playerIndex == 2){
                DisCardAnimation(tileId,topDiscardsPosition,new Vector3(-1,0,0),new Vector3(0,0,1),"top",topDiscardslist.Count);
                removeOtherHandCards(topCardsPosition,cut_class);
                topDiscardslist.Add(tileId);
            }
            else if (playerIndex == 3){
                DisCardAnimation(tileId,leftDiscardsPosition,new Vector3(0,0,-1),new Vector3(-1,0,0),"left",leftDiscardslist.Count);
                removeOtherHandCards(leftCardsPosition,cut_class);
                leftDiscardslist.Add(tileId);
            }
        }
        else if (selfCurrentIndex == 1){
            if (playerIndex == 0){
                DisCardAnimation(tileId,leftDiscardsPosition,new Vector3(0,0,-1),new Vector3(-1,0,0),"left",leftDiscardslist.Count);
                removeOtherHandCards(leftCardsPosition,cut_class);
                leftDiscardslist.Add(tileId);
            }
            else if (playerIndex == 2){
                DisCardAnimation(tileId,rightDiscardsPosition,new Vector3(0,0,1),new Vector3(1,0,0),"right",rightDiscardslist.Count);
                removeOtherHandCards(rightCardsPosition,cut_class);
                rightDiscardslist.Add(tileId);
            }
            else if (playerIndex == 3){
                DisCardAnimation(tileId,topDiscardsPosition,new Vector3(-1,0,0),new Vector3(0,0,1),"top",topDiscardslist.Count);
                removeOtherHandCards(topCardsPosition,cut_class);
                topDiscardslist.Add(tileId);
            }
        }
        else if (selfCurrentIndex == 2){
            if (playerIndex == 0){
                DisCardAnimation(tileId,topDiscardsPosition,new Vector3(-1,0,0),new Vector3(0,0,1),"top",topDiscardslist.Count);
                removeOtherHandCards(topCardsPosition,cut_class);
                topDiscardslist.Add(tileId);
            }
            else if (playerIndex == 1){
                DisCardAnimation(tileId,leftDiscardsPosition,new Vector3(0,0,-1),new Vector3(-1,0,0),"left",leftDiscardslist.Count);
                removeOtherHandCards(leftCardsPosition,cut_class);
                leftDiscardslist.Add(tileId);
            }
            else if (playerIndex == 3){
                DisCardAnimation(tileId,rightDiscardsPosition,new Vector3(0,0,1),new Vector3(1,0,0),"right",rightDiscardslist.Count);
                removeOtherHandCards(rightCardsPosition,cut_class);
                rightDiscardslist.Add(tileId);
            }
        }
        else if (selfCurrentIndex == 3){
            if (playerIndex == 0){
                DisCardAnimation(tileId,rightDiscardsPosition,new Vector3(0,0,1),new Vector3(1,0,0),"right",rightDiscardslist.Count);
                removeOtherHandCards(rightCardsPosition,cut_class);
                rightDiscardslist.Add(tileId);
            }
            else if (playerIndex == 1){
                DisCardAnimation(tileId,topDiscardsPosition,new Vector3(-1,0,0),new Vector3(0,0,1),"top",topDiscardslist.Count);
                removeOtherHandCards(topCardsPosition,cut_class);
                topDiscardslist.Add(tileId);
            }
            else if (playerIndex == 2){
                DisCardAnimation(tileId,leftDiscardsPosition,new Vector3(0,0,-1),new Vector3(-1,0,0),"left",leftDiscardslist.Count);
                removeOtherHandCards(leftCardsPosition,cut_class);
                leftDiscardslist.Add(tileId);
            }
        }
    }


    
    private async void removeOtherHandCards(Transform cardPosition,bool cut_class){
        Debug.Log($"移除其他玩家手牌 {cardPosition},{cut_class}");

        float cardWidth = tile3DPrefab.GetComponent<Renderer>().bounds.size.y;
        float cardHeight = tile3DPrefab.GetComponent<Renderer>().bounds.size.z;

        float spacing = cardWidth * 1f; // 间距为卡片宽度的1.0倍
        // 如果摸切就直接删除摸到的牌
        if (cut_class){
            if (cardPosition == rightCardsPosition){
                Transform cardTransform = rightCardsPosition.Find("Card_Current");
                if (cardTransform != null) {
                    Destroy(cardTransform.gameObject);
                }
            }
            else if (cardPosition == leftCardsPosition){
                Transform cardTransform = leftCardsPosition.Find("Card_Current");
                if (cardTransform != null) {
                    Destroy(cardTransform.gameObject);
                }
            }
            else if (cardPosition == topCardsPosition){
                Transform cardTransform = topCardsPosition.Find("Card_Current");
                if (cardTransform != null) {
                    Destroy(cardTransform.gameObject);
                }
            }
        }
        // 如果摸牌则随机删除一张牌，将摸牌加入手牌
        else{
            // 计算子物体数量，获取随机索引，随机删除选中的子物体
            int childCount = cardPosition.childCount;
                int randomIndex = UnityEngine.Random.Range(0, childCount);
                Transform randomChild = cardPosition.GetChild(randomIndex);
                    Debug.Log($"随机删除了索引为 {randomIndex} 的牌");
                    Destroy(randomChild.gameObject);
            // 等待1秒
            await Task.Delay(1000);
                    
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
                Vector3 newPosition = startPosition + direction * spacing * (i + 0);
                        remainingCards[i].position = newPosition;
                remainingCards[i].name = $"ReSeTCard_{i}";
            }
        }
    }


    public void AskBuhuaAction(int remaining_time,int tileId,int playerIndex,int remain_tiles,string[] action_list){

        Debug.Log($"获取补花信息 {remaining_time},{tileId},{playerIndex},{remain_tiles},{action_list}");
        RefreshPlayerAnimation(playerIndex,action_list);
        remiansTilesText.text = $"余: {remain_tiles}";
        string GetCardPlayer = player_local_position[playerIndex];
        if (GetCardPlayer == "self"){
            SetActionButton(action_list);
        }
    }


    private void SetActionButton(string[] action_list){
        for (int i = 0; i < action_list.Length; i++){
            // 用于跟踪吃牌按钮
            ActionButton chiButton = null;
            Debug.Log($"询问操作: {action_list[i]}");
            if (action_list[i] == "chi_left" || action_list[i] == "chi_right" || action_list[i] == "chi_mid"){
                if (chiButton == null)
                {
                    // 第一次遇到吃牌选项时创建按钮
                    chiButton = Instantiate(ActionButtonPrefab, ActionButtonContainer);
                    Text buttonText = chiButton.TextObject;
                    buttonText.text = "吃";
                    Debug.Log($"创建吃牌按钮: {chiButton}");
                }
                // 将当前的吃牌选项添加到已存在的吃牌按钮中
                chiButton.actionTypeList.Add(action_list[i]);
                Debug.Log($"添加吃牌选项: {action_list[i]}");
            }
            else if (action_list[i] == "peng"){
                Debug.Log($"碰牌");
                // 实例化按钮
                ActionButton ActionButtonObj = Instantiate(ActionButtonPrefab, ActionButtonContainer);
                // 设置按钮文本
                Text buttonText = ActionButtonObj.TextObject;
                buttonText.text = "碰";
                // 设置按钮行动列表
                Debug.Log($"碰牌按钮: {ActionButtonObj}");
                ActionButtonObj.actionTypeList.Add(action_list[i]);
            }
            else if (action_list[i] == "gang"){
                Debug.Log($"杠牌");
                // 实例化按钮
                ActionButton ActionButtonObj = Instantiate(ActionButtonPrefab, ActionButtonContainer);
                // 设置按钮文本
                Text buttonText = ActionButtonObj.TextObject;
                buttonText.text = "杠";
                // 设置按钮行动列表
                Debug.Log($"杠牌按钮: {ActionButtonObj}");
                ActionButtonObj.actionTypeList.Add(action_list[i]);
            }
            else if (action_list[i] == "hu"){
                Debug.Log($"胡牌");
                // 实例化按钮
                ActionButton ActionButtonObj = Instantiate(ActionButtonPrefab, ActionButtonContainer);
                // 设置按钮文本
                Text buttonText = ActionButtonObj.TextObject;
                buttonText.text = "胡";
                // 设置按钮行动列表
                Debug.Log($"胡牌按钮: {ActionButtonObj}");
                ActionButtonObj.actionTypeList.Add(action_list[i]);
            }
            else if (action_list[i] == "buhua"){
                Debug.Log($"补花");
                // 实例化按钮
                ActionButton ActionButtonObj = Instantiate(ActionButtonPrefab, ActionButtonContainer);
                // 设置按钮文本
                Text buttonText = ActionButtonObj.TextObject;
                buttonText.text = "补花";
                // 设置按钮行动列表
                Debug.Log($"补花按钮: {ActionButtonObj}");
                ActionButtonObj.actionTypeList.Add(action_list[i]);
            } 
            else if (action_list[i] == "angang"){
                Debug.Log($"暗杠");
                // 实例化按钮
                ActionButton ActionButtonObj = Instantiate(ActionButtonPrefab, ActionButtonContainer);
                // 设置按钮文本
                Text buttonText = ActionButtonObj.TextObject;
                buttonText.text = "暗杠";
                // 设置按钮行动列表
                Debug.Log($"暗杠按钮: {ActionButtonObj}");
                ActionButtonObj.actionTypeList.Add(action_list[i]);
            }
            else if (action_list[i] == "jiagang"){
                Debug.Log($"加杠");
                // 实例化按钮
                ActionButton ActionButtonObj = Instantiate(ActionButtonPrefab, ActionButtonContainer);
                // 设置按钮文本
                Text buttonText = ActionButtonObj.TextObject;
                buttonText.text = "加杠";
                // 设置按钮行动列表
                Debug.Log($"加杠按钮: {ActionButtonObj}");
                ActionButtonObj.actionTypeList.Add(action_list[i]);
            }
        }
        if (action_list.Length > 0){
            Debug.Log($"取消");
            // 实例化按钮
            ActionButton ActionButtonObj = Instantiate(ActionButtonPrefab, ActionButtonContainer);
            // 设置按钮文本
            Text buttonText = ActionButtonObj.TextObject;
            buttonText.text = "取消";
            // 设置按钮行动列表
            Debug.Log($"取消按钮: {ActionButtonObj}");
            ActionButtonObj.actionTypeList.Add("pass");
        }
    }





    public void AskOtherCutAction(int remaining_time,string[] action_list,int cut_tile){
        lastCutTile = cut_tile;
        // 如果列表中有服务器提供的可用操作，则显示倒计时
        if (action_list.Length > 0){
            loadingRemianTime(remaining_time, currentCutTime);
        }
        Debug.Log($"询问操作列表: {action_list}");
        SetActionButton(action_list); 
    }

    private void CreateActionCards(List<int> tiles,string actionType) {
        // 清空现有提示牌
        foreach (Transform child in ActionBlockContenter)
        {
            Destroy(child.gameObject);
        }
        GameObject containerBlockObj = Instantiate(ActionBlockPrefab, ActionBlockContenter);
        ActionBlock blockClick = containerBlockObj.GetComponent<ActionBlock>();
        blockClick.actionType = actionType;
        foreach (int tile in tiles){
            GameObject cardObj = Instantiate(StaticCardPrefab, containerBlockObj.transform);
            cardObj.GetComponent<StaticCard>().SetTileOnlyImage(tile);
        }
    }

    public void ChooseAction(List<string> actionTypeList){
        List<int> TipsCardsList = new List<int>();
        // 根据行动类型设置提示牌
        foreach (string actionType in actionTypeList){
            switch (actionType){
                // 吃碰杠显示吃碰杠列表
                case "chi_left": 
                    TipsCardsList.Add(lastCutTile-2);
                    TipsCardsList.Add(lastCutTile-1);
                    CreateActionCards(TipsCardsList, actionType);
                    break;
                case "chi_mid":
                    TipsCardsList.Clear();
                    TipsCardsList.Add(lastCutTile-1);
                    TipsCardsList.Add(lastCutTile+1);
                    CreateActionCards(TipsCardsList, actionType);
                    break;
                case "chi_right":
                    TipsCardsList.Clear();
                    TipsCardsList.Add(lastCutTile+1);
                    TipsCardsList.Add(lastCutTile+2);
                    CreateActionCards(TipsCardsList, actionType);
                    break;
                case "peng":
                    TipsCardsList.Clear();
                    TipsCardsList.Add(lastCutTile);
                    TipsCardsList.Add(lastCutTile);
                    CreateActionCards(TipsCardsList, actionType);
                    break;
                case "gang":
                    TipsCardsList.Clear();
                    TipsCardsList.Add(lastCutTile);
                    TipsCardsList.Add(lastCutTile);
                    TipsCardsList.Add(lastCutTile);
                    CreateActionCards(TipsCardsList, actionType);
                    break;
                case "pass": // 如果点击取消则停止计时 发送pass请求
                    StopTimeRunning();
                    NetworkManager.Instance.SendAction("pass");
                    break;
                case "hu":
                    StopTimeRunning();
                    NetworkManager.Instance.SendAction("hu");
                    break;
                case "buhua":
                    StopTimeRunning();
                    NetworkManager.Instance.SendAction("buhua");
                    break;
                case "jiagang":
                    StopTimeRunning();
                    NetworkManager.Instance.SendAction("jiagang");
                    break;
                case "angang":
                    StopTimeRunning();
                    NetworkManager.Instance.SendAction("angang");
                    break;
            }
        }
    }

    public void ClearActionContenter(){
        foreach (Transform child in ActionBlockContenter){
            Destroy(child.gameObject);
        }
        foreach (Transform child in ActionButtonContainer){
            Destroy(child.gameObject);
        }
    }

    public void DoAction2(string doActionType, int remianTime,int playerIndex,int tileId){
        Debug.Log($"执行DoAction操作: {doActionType}, 时间={remianTime}, 玩家={playerIndex}, 牌={tileId}");
        if (doActionType == "pass"){
            return; // 如果接收到pass信息则不执行任何操作
        }
        // 获取玩家位置
        string playerPosition = player_local_position[playerIndex];
        // 更新玩家位置
        RefreshPlayerAnimation(playerIndex,new string[]{});
        // 删除最后一张切牌
        Destroy(LastCutCard);
        // 删除上一次操作玩家最后一张弃牌
        string DoActionFrom = player_local_position[LastDoActionPlayer];
        if (DoActionFrom == "self"){
            selfDiscardslist.RemoveAt(selfDiscardslist.Count - 1);
        }
        else if (DoActionFrom == "left"){
            leftDiscardslist.RemoveAt(leftDiscardslist.Count - 1);
        }
        else if (DoActionFrom == "top"){
            topDiscardslist.RemoveAt(topDiscardslist.Count - 1);
        }
        else if (DoActionFrom == "right"){
            rightDiscardslist.RemoveAt(rightDiscardslist.Count - 1);
        }

        // 如果操作者是他家 删除他家手牌 如果操作者是自己 删除自己手牌
        if (playerPosition == "self"){
            if (doActionType == "chi_left"){
                // 从handcardscontainer中删除
                foreach (Transform child in handCardsContainer){
                    if (child.GetComponent<TileCard>().tileId == tileId - 1){
                        Destroy(child.gameObject);
                    }
                }
                foreach (Transform child in handCardsContainer){
                    if (child.GetComponent<TileCard>().tileId == tileId - 2){
                        Destroy(child.gameObject);
                    }
                }
            }
            else if (doActionType == "chi_mid"){
                // 从handcardscontainer中删除
                foreach (Transform child in handCardsContainer){
                    if (child.GetComponent<TileCard>().tileId == tileId - 1){
                        Destroy(child.gameObject);
                    }
                }
                foreach (Transform child in handCardsContainer){
                    if (child.GetComponent<TileCard>().tileId == tileId + 1){
                        Destroy(child.gameObject);
                    }
                }
            }
            else if (doActionType == "chi_right"){
                // 从handcardscontainer中删除
                foreach (Transform child in handCardsContainer){
                    if (child.GetComponent<TileCard>().tileId == tileId + 1){
                        Destroy(child.gameObject);
                    }
                }
                foreach (Transform child in handCardsContainer){
                    if (child.GetComponent<TileCard>().tileId == tileId + 2){
                        Destroy(child.gameObject);
                    }
                }
            }
            else if (doActionType == "peng"){
                // 从handcardscontainer中删除
                foreach (Transform child in handCardsContainer){
                    if (child.GetComponent<TileCard>().tileId == tileId){
                        Destroy(child.gameObject);
                    }
                }
                foreach (Transform child in handCardsContainer){
                    if (child.GetComponent<TileCard>().tileId == tileId){
                        Destroy(child.gameObject);
                    }
                }
            }
            else if (doActionType == "gang"){
                // 从handcardscontainer中删除
                foreach (Transform child in handCardsContainer){
                    if (child.GetComponent<TileCard>().tileId == tileId){
                        Destroy(child.gameObject);
                    }
                }
                foreach (Transform child in handCardsContainer){
                    if (child.GetComponent<TileCard>().tileId == tileId){
                        Destroy(child.gameObject);
                    }
                }
                foreach (Transform child in handCardsContainer){
                    if (child.GetComponent<TileCard>().tileId == tileId){
                        Destroy(child.gameObject);
                    }
                }
            }
            // 执行动画
            ActionAnimation(doActionType,tileId,"self",DoActionFrom);
            // 更新剩余时间
            loadingRemianTime(remianTime,currentCutTime);
        }
        else if (playerPosition == "left"){
            removeOtherHandCards(leftCardsPosition,false);
            removeOtherHandCards(leftCardsPosition,false);
            ActionAnimation(doActionType,tileId,"left",DoActionFrom);
        }
        else if (playerPosition == "top"){
            removeOtherHandCards(topCardsPosition,false);
            removeOtherHandCards(topCardsPosition,false);
            ActionAnimation(doActionType,tileId,"top",DoActionFrom);
        }
        else if (playerPosition == "right"){
            removeOtherHandCards(rightCardsPosition,false);
            removeOtherHandCards(rightCardsPosition,false);
            ActionAnimation(doActionType,tileId,"right",DoActionFrom);
        }
    }








    private void InitializeSetInfo(GameInfo gameInfo){
        // 如果gameinfo.username等于自己的username，则设置自身索引为gameinfo.player_index
        foreach (var player in gameInfo.players_info){
            if (player.username == Administrator.Instance.Username){
                selfIndex = player.player_index; // 存储自身索引
                break;
            }
        }
        roomId = gameInfo.room_id; // 存储房间ID
        currentIndex = gameInfo.current_player_index; // 存储当前玩家索引
        selfStepTime = gameInfo.step_time; // 存储步时
        selfRoundTime = gameInfo.round_time; // 存储局时
        tips = gameInfo.tips; // 存储是否提示
        // 根据自身索引确定其他玩家位置
        if (selfIndex == 0)
        {
            indexToPosition[0] = "self";
            indexToPosition[1] = "left";
            indexToPosition[2] = "top";
            indexToPosition[3] = "right";
        }
        else if (selfIndex == 1)
        {
            indexToPosition[1] = "self";
            indexToPosition[2] = "left";
            indexToPosition[3] = "top";
            indexToPosition[0] = "right";
        }
        else if (selfIndex == 2)
        {
            indexToPosition[2] = "self";
            indexToPosition[3] = "left";
            indexToPosition[0] = "top";
            indexToPosition[1] = "right";
            
        }
        else if (selfIndex == 3)
        {
            indexToPosition[3] = "self";
            indexToPosition[0] = "left";
            indexToPosition[1] = "top";
            indexToPosition[2] = "right";
        }
        foreach (var player in gameInfo.players_info){
            if (indexToPosition[player.player_index] == "self"){ // 通过player_index确定玩家位置
                // 存储剩余时间
                selfRemainingTime = player.remaining_time;
                selfDiscardslist = player.discard_tiles.ToList(); // 存储弃牌列表
                selfCombinationList = player.combination_tiles.ToList(); // 存储组合牌列表
                selfHuapaiList = player.huapai_list.ToList(); // 存储花牌列表
            }
            else if (indexToPosition[player.player_index] == "left"){
                leftDiscardslist = player.discard_tiles.ToList(); // 存储弃牌列表
                leftCombinationList = player.combination_tiles.ToList(); // 存储组合牌列表
                leftHuapaiList = player.huapai_list.ToList(); // 存储花牌列表
            }
            else if (indexToPosition[player.player_index] == "top"){
                topDiscardslist = player.discard_tiles.ToList(); // 存储弃牌列表
                topCombinationList = player.combination_tiles.ToList(); // 存储组合牌列表
                topHuapaiList = player.huapai_list.ToList(); // 存储花牌列表
            }
            else if (indexToPosition[player.player_index] == "right"){
                rightDiscardslist = player.discard_tiles.ToList(); // 存储弃牌列表
                rightCombinationList = player.combination_tiles.ToList(); // 存储组合牌列表
                rightHuapaiList = player.huapai_list.ToList(); // 存储花牌列表
            }
        }
    }





    private void RefreshPlayerAnimation(int playerIndex,string[] actionType){
        // 记录上一次操作玩家
        LastDoActionPlayer = NowCurrentIndex;
        // 更新当前玩家
        NowCurrentIndex = playerIndex;
        // 如果玩家是自己 则selfDoAction为false 代表可以操作
        if (player_local_position[playerIndex] == "self"){
            foreach (string action in actionType){
                if (action == "cut"){
                    CutAction = true;
                }
            }
        }
        else{
            CutAction = false;
        }
        if (player_local_position[playerIndex] == "self"){
            player_self_current_image.enabled = true;
            player_left_current_image.enabled = false;
            player_top_current_image.enabled = false;
            player_right_current_image.enabled = false;
        }
        else if (player_local_position[playerIndex] == "left"){
            player_self_current_image.enabled = false;
            player_left_current_image.enabled = true;
            player_top_current_image.enabled = false;
            player_right_current_image.enabled = false;
        }
        else if (player_local_position[playerIndex] == "top"){
            player_self_current_image.enabled = false;
            player_left_current_image.enabled = false;
            player_top_current_image.enabled = true;
            player_right_current_image.enabled = false;
        }
        else if (player_local_position[playerIndex] == "right"){
            player_self_current_image.enabled = false;
            player_left_current_image.enabled = false;
            player_top_current_image.enabled = false;
            player_right_current_image.enabled = true;
        }
    }

}
