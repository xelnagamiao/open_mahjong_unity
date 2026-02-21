using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class AutoAction : MonoBehaviour{
    public static AutoAction Instance { get; private set; }
    [Header("自动操作文本")]
    [SerializeField] private TMP_Text arrangeHandCardsText; // 自动排列手牌文本
    [SerializeField] private TMP_Text autoHepaiText; // 自动胡牌文本
    [SerializeField] private TMP_Text autoCutCardText; // 自动出牌文本
    [SerializeField] private TMP_Text autoPassText; // 自动过牌文本
    [SerializeField] private TMP_Text autoBuhuaText; // 自动补花文本

    [Header("颜色配置")]
    [SerializeField] private Color falseColor = Color.white; // false时的颜色（白色）
    [SerializeField] private Color trueColor = new Color(1f, 0.5f, 0f); // true时的颜色（橙色）

    [Header("自动操作配置")]
    private bool isAutoArrangeHandCards = true; // 是否自动排列手牌
    private bool isAutoBuhua = true; // 是否自动补花
    private bool isAutoHepai = false; // 是否自动胡牌
    private bool isAutoCut = false; // 是否自动出牌
    private bool isAutoPass = false; // 是否自动过牌

    // 公共属性，供外部访问
    public bool IsAutoArrangeHandCards { get => isAutoArrangeHandCards; }
    public bool IsAutoBuhua { get => isAutoBuhua; }
    public bool IsAutoHepai { get => isAutoHepai; }
    public bool IsAutoCut { get => isAutoCut; }
    public bool IsAutoPass { get => isAutoPass; }

    private void Awake(){
        if (Instance == null){
            Instance = this;
        } else {
            Destroy(gameObject);
            return;
        }
    }

    // 初始化自动行为配置（由 GameSceneUIManager 调用）
    public void Initialize() {
        gameObject.SetActive(true); // 显示自动行为组件

        // 重置除了自动排列手牌和自动补花以外的选项为false
        isAutoHepai = false;
        isAutoPass = false;
        isAutoCut = false;
        // 保留 isAutoBuhua 和 isAutoArrangeHandCards 的当前值

        // 更新显示
        UpdateAllTextColors();
        // 为每个文本添加点击功能
        AddClickListeners();
    }

    // 为每个文本添加点击监听器
    private void AddClickListeners(){
        AddClickListener(arrangeHandCardsText, ToggleArrangeHandCards);
        AddClickListener(autoHepaiText, ToggleAutoHepai);
        AddClickListener(autoCutCardText, ToggleAutoCutCard);
        AddClickListener(autoPassText, ToggleAutoPass);
        AddClickListener(autoBuhuaText, ToggleAutoBuhua);
    }

    // 为TMP_Text添加点击监听器
    private void AddClickListener(TMP_Text text, System.Action action){
        // 检查是否已有Button组件
        Button button = text.GetComponent<Button>();
        if (button == null){
            // 如果没有Button组件，添加一个
            button = text.gameObject.AddComponent<Button>();
        }
        // 移除旧的监听器，添加新的
        button.onClick.RemoveAllListeners();
        button.onClick.AddListener(() => action?.Invoke());
    }

    // 通用切换方法
    private void ToggleAutoOption(ref bool option, TMP_Text text){
        option = !option;
        UpdateTextColor(text, option);
    }

    // 切换自动排列手牌
    private void ToggleArrangeHandCards(){
        ToggleAutoOption(ref isAutoArrangeHandCards, arrangeHandCardsText);
    }

    // 切换自动胡牌
    private void ToggleAutoHepai(){
        ToggleAutoOption(ref isAutoHepai, autoHepaiText);
    }

    // 切换自动出牌
    private void ToggleAutoCutCard(){
        ToggleAutoOption(ref isAutoCut, autoCutCardText);
    }

    // 切换自动过牌
    private void ToggleAutoPass(){
        ToggleAutoOption(ref isAutoPass, autoPassText);
    }

    // 切换自动补花
    private void ToggleAutoBuhua(){
        ToggleAutoOption(ref isAutoBuhua, autoBuhuaText);
    }

    // 更新单个文本颜色
    private void UpdateTextColor(TMP_Text text, bool value){
        if (text != null){
            text.color = value ? trueColor : falseColor;
        }
    }

    // 更新所有文本颜色
    private void UpdateAllTextColors(){
        UpdateTextColor(arrangeHandCardsText, isAutoArrangeHandCards);
        UpdateTextColor(autoHepaiText, isAutoHepai);
        UpdateTextColor(autoCutCardText, isAutoCut);
        UpdateTextColor(autoPassText, isAutoPass);
        UpdateTextColor(autoBuhuaText, isAutoBuhua);
    }
}
