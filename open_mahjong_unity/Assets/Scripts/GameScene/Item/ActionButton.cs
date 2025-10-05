using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using System.Linq;


public class ActionButton : MonoBehaviour
{
    [SerializeField] private GameObject ActionBlockPrefab; // 次级按钮选择块
    [SerializeField] private GameObject StaticCardPrefab; // 静态牌预制体(包含在多种结果显示中的图像)
    // 通过 GameCanvas 单例获取 ActionBlockContenter，不再需要 SerializeField
    private Transform ActionBlockContenter => GameCanvas.Instance.ActionBlockContenter;

    [SerializeField] private Text textObject; // 按钮文本
    
    public List<string> actionTypeList = new List<string>(); // 动作类型列表 同一个按钮可能有多个行动 例如 chi_left,chi_right,chi_mid
    
    public Text TextObject
    {
        get
        {
            if (textObject == null)
                textObject = GetComponentInChildren<Text>();
            return textObject;
        }
    }
    
    void Start()
    {
        if (textObject == null)
            textObject = GetComponentInChildren<Text>();
        // 按钮点击事件
        Button button = GetComponent<Button>();
        button.onClick.AddListener(OnClick);
    }

    // 按钮点击事件
    void OnClick(){
        GameSceneManager.Instance.allowActionList.Clear();
        // 如果动作列表大于1 显示子级按钮
        if (actionTypeList.Count > 1){
            int lastCutTile = GameSceneManager.Instance.lastCutCardID;

            // 根据按钮里的多种吃牌情况创建分支块
            foreach (string actionType in actionTypeList){
                List<int> TipsCardsList = new List<int>(); // 为每个case创建独立的列表
                switch (actionType){
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
                        foreach (int tileID in GameSceneManager.Instance.handTiles){
                            if (GameSceneManager.Instance.handTiles.Count(x => x == tileID) == 4){
                                List<int> angangCards = new List<int> { tileID, tileID, tileID, tileID };
                                CreateActionCards(angangCards, actionType,tileID);
                            }
                        }
                        break;
                    case "jiagang":
                        // 遍历手牌 如果组合牌中有符合加杠的组合 则添加到提示牌列表
                        foreach (int tileID in GameSceneManager.Instance.handTiles){
                            if (GameSceneManager.Instance.selfCombinationList.Contains($"k{tileID}")){
                                List<int> jiagangCards = new List<int> { tileID, tileID, tileID, tileID };
                                CreateActionCards(jiagangCards, actionType,tileID);
                            }
                        }
                        break;
                    }
            }
        }
        // 如果动作列表小于等于1 发送行动
        else{
            Debug.Log($"选择了行动 {actionTypeList[0]}");
            GameCanvas.Instance.ChooseAction(actionTypeList[0],0);
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
    }

}

