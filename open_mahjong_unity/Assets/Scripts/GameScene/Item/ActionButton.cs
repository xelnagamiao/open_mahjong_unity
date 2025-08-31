using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;


public class ActionButton : MonoBehaviour
{
    [SerializeField] private GameObject ActionBlockPrefab; // 次级按钮选择块
    [SerializeField] private GameObject StaticCardPrefab; // 静态牌预制体(包含在多种结果显示中的图像)
    [SerializeField] private Transform ActionBlockContenter;  // 询问操作内容提示(显示吃,碰,杠,胡,补花,抢杠等按钮的多种结果)

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
    
    // Start is called before the first frame update
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
        // 如果动作列表大于1 显示子级按钮
        if (actionTypeList.Count > 1){
            List<int> TipsCardsList = new List<int>();
            int lastCutTile = GameSceneManager.Instance.lastCutCardID;

            // 根据按钮里的多种吃牌情况创建分支块
            foreach (string actionType in actionTypeList){
                switch (actionType){
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
                    }
            }
        }
        // 如果动作列表小于等于1 发送行动
        else{
            Debug.Log($"选择了行动 {actionTypeList[0]}");
            GameCanvas.Instance.ChooseAction(actionTypeList[0]);
        }
    }

    private void CreateActionCards(List<int> TipsCardsList,string actionType) {
        // 清空现有提示牌
        foreach (Transform child in ActionBlockContenter)
        {
            Destroy(child.gameObject);
        }
        // 在提示牌容器中创建提示牌块
        GameObject containerBlockObj = Instantiate(ActionBlockPrefab, ActionBlockContenter);

        // 设置提示牌块的行动类型为actionType
        ActionBlock blockClick = containerBlockObj.GetComponent<ActionBlock>();
        blockClick.actionType = actionType;

        // 在提示牌块中根据TipsCardsList创建提示牌
        foreach (int tile in TipsCardsList){
            GameObject cardObj = Instantiate(StaticCardPrefab, containerBlockObj.transform);
            cardObj.GetComponent<StaticCard>().SetTileOnlyImage(tile);
        }
    }

}

