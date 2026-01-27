using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Linq;


public class ActionButton : MonoBehaviour {
    [SerializeField] private GameObject ActionBlockPrefab; // 次级按钮选择块
    [SerializeField] private GameObject StaticCardPrefab; // 静态牌预制体(包含在多种结果显示中的图像)
    // 通过 GameCanvas 单例获取 ActionBlockContenter，不再需要 SerializeField
    private Transform ActionBlockContenter => GameCanvas.Instance.ActionBlockContenter;

    [SerializeField] private TMP_Text textObject; // 按钮文本

    public List<string> actionTypeList = new List<string>(); // 动作类型列表 同一个按钮可能有多个行动 例如 chi_left,chi_right,chi_mid

    public TMP_Text TextObject {
        get {
            return textObject;
        }
    }
    
    void Start() {
        // 验证TMP_Text组件是否已赋值
        if (textObject == null)
        {
            Debug.LogError("ActionButton: TMP_Text component is not assigned in Inspector!");
        }

        // 按钮点击事件
        Button button = GetComponent<Button>();
        if (button == null)
        {
            Debug.LogError("ActionButton: Button component not found!");
            return;
        }
        button.onClick.AddListener(OnClick);
    }

    // 按钮点击事件
    void OnClick(){

        // 如果动作列表大于1 显示子级按钮
        if (actionTypeList.Count > 1){
            int lastCutTile = GameSceneManager.Instance.lastCutCardID;

            // 确定当前按钮类型
            string currentButtonType = "None";
            if (actionTypeList.Contains("chi_left") || actionTypeList.Contains("chi_right") || actionTypeList.Contains("chi_mid")){
                currentButtonType = "chi";
            } else if (actionTypeList.Contains("angang")){
                currentButtonType = "angang";
            } else if (actionTypeList.Contains("jiagang")){
                currentButtonType = "jiagang";
            }
            
            // 如果ActionBlockContenter的状态为空,点击则创建子级按钮
            if (GameCanvas.Instance.ActionBlockContainerState == "None"){
                GameCanvas.Instance.ActionBlockContainerState = currentButtonType;
            }
            // 如果ActionBlockContenter的状态不为空
            else if (GameCanvas.Instance.ActionBlockContainerState != "None"){
                // 清空容器
                foreach (Transform child in ActionBlockContenter){
                    Destroy(child.gameObject);
                }
                // 如果点击的是相同类型的按钮，则清空后直接返回
                if (GameCanvas.Instance.ActionBlockContainerState == currentButtonType){
                    GameCanvas.Instance.ActionBlockContainerState = "None";
                    return;
                }
                // 如果点击的是不同类型的按钮，则切换状态继续执行
                else {
                    GameCanvas.Instance.ActionBlockContainerState = currentButtonType;
                }
            }

            // 根据按钮里的多种吃牌情况创建分支块
            foreach (string actionType in actionTypeList){
                List<int> TipsCardsList = new List<int>(); // 为每个case创建独立的列表
                switch (actionType) {
                    case "chi_left": 
                        TipsCardsList.Add(lastCutTile-2);
                        TipsCardsList.Add(lastCutTile-1);
                        CreateActionCards(TipsCardsList, actionType,0);
                        break;
                    case "chi_mid":
                        TipsCardsList.Add(lastCutTile-1);
                        TipsCardsList.Add(lastCutTile+1);
                        CreateActionCards(TipsCardsList, actionType,0);
                        break;
                    case "chi_right":
                        TipsCardsList.Add(lastCutTile+1);
                        TipsCardsList.Add(lastCutTile+2);
                        CreateActionCards(TipsCardsList, actionType,0);
                        break;
                    case "angang":
                        // 遍历手牌 如果手牌有4张相同的牌 则添加到提示牌列表
                        // 使用 HashSet 确保每个 tileID 只处理一次
                        HashSet<int> processedTileIDs = new HashSet<int>();
                        foreach (int tileID in GameSceneManager.Instance.selfHandTiles){
                            if (!processedTileIDs.Contains(tileID) && GameSceneManager.Instance.selfHandTiles.Count(x => x == tileID) == 4){
                                processedTileIDs.Add(tileID);
                                List<int> angangCards = new List<int> { tileID, tileID, tileID, tileID };
                                CreateActionCards(angangCards, actionType,tileID);
                            }
                        }
                        break;
                    case "jiagang":
                        // 遍历手牌 如果组合牌中有符合加杠的组合 则添加到提示牌列表
                        foreach (int tileID in GameSceneManager.Instance.selfHandTiles){
                            if (GameSceneManager.Instance.player_to_info["self"].combination_tiles.Contains($"k{tileID}")){
                                List<int> jiagangCards = new List<int> { tileID, tileID, tileID, tileID };
                                CreateActionCards(jiagangCards, actionType,tileID);
                            }
                        }
                        break;
                    }
            }
        }
        // 如果动作列表小于等于1 发送行动
        else {
            if (actionTypeList[0] == "jiagang"){
                Debug.Log($"选择了行动 {actionTypeList[0]}");
                int targetTile = 0;
                foreach (int tileID in GameSceneManager.Instance.selfHandTiles){
                    if (GameSceneManager.Instance.player_to_info["self"].combination_tiles.Contains($"k{tileID}")){
                        targetTile = tileID;
                        break;
                    }
                }
                GameCanvas.Instance.ChooseAction(actionTypeList[0],targetTile);
            } else if (actionTypeList[0] == "angang"){
                Debug.Log($"选择了行动 {actionTypeList[0]}");
                int targetTile = 0;
                foreach (int tileID in GameSceneManager.Instance.selfHandTiles){
                    if (GameSceneManager.Instance.selfHandTiles.Count(x => x == tileID) == 4){
                        targetTile = tileID;
                        break;
                    }
                }
                GameCanvas.Instance.ChooseAction(actionTypeList[0],targetTile);
            } else {
                Debug.Log($"选择了行动 {actionTypeList[0]}");
                GameCanvas.Instance.ChooseAction(actionTypeList[0],0);
            }
        }
    }

    private void CreateActionCards(List<int> TipsCardsList,string actionType,int targetTile) {
        // 在提示牌容器中创建提示牌块
        GameObject containerBlockObj = Instantiate(ActionBlockPrefab, ActionBlockContenter);

        // 设置提示牌块的行动类型为actionType
        ActionBlock blockClick = containerBlockObj.GetComponent<ActionBlock>();
        blockClick.actionType = actionType;
        if (targetTile != 0){
            blockClick.targetTile = targetTile;
        }

        // 在提示牌块中根据TipsCardsList创建提示牌
        foreach (int tile in TipsCardsList){
            GameObject cardObj = Instantiate(StaticCardPrefab, containerBlockObj.transform);
            cardObj.GetComponent<StaticCard>().SetTileOnlyImage(tile);
        }

        // 强制刷新布局，使 ActionBlockContenter 根据子块重新计算大小
        Canvas.ForceUpdateCanvases();
        LayoutRebuilder.ForceRebuildLayoutImmediate(ActionBlockContenter as RectTransform);
    }

}

