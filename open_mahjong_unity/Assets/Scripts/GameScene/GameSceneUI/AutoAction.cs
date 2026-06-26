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

    [Header("鸣牌展开")]
    [SerializeField] private GameObject otherActionPanel; // 鸣牌展开及子选项（观战时隐藏整块）
    [SerializeField] private TMP_Text expandButtonText; // 展开按钮（附加按钮的文本）
    [SerializeField] private GameObject mingPaiPanel; // 鸣牌选项面板
    [SerializeField] private TilePassSettingPanel tilePassSettingPanel; // 牌张设置面板（可运行时生成）
    [SerializeField] private TMP_Text autoPassChiText; // 不吃文本
    [SerializeField] private TMP_Text autoPassPengText; // 不碰文本
    [SerializeField] private TMP_Text autoPassGangText; // 不杠文本
    [SerializeField] private TMP_Text autoOnlyRonText; // 只和荣和文本
    [SerializeField] private TMP_Text autoOnlyTsumoText; // 只和自摸文本

    [Header("颜色配置")]
    [SerializeField] private Color falseColor = Color.white; // false时的颜色（白色）
    [SerializeField] private Color trueColor = new Color(1f, 0.5f, 0f); // true时的颜色（橙色）

    [Header("自动操作配置")]
    private bool isAutoArrangeHandCards = true; // 是否自动排列手牌
    private bool isAutoBuhua = true; // 是否自动补花
    private bool isAutoHepai = false; // 是否自动胡牌
    private bool isAutoCut = false; // 是否自动出牌
    private bool isAutoPass = false; // 是否自动过牌
    private bool isAutoPassChi = false; // 是否不吃
    private bool isAutoPassPeng = false; // 是否不碰
    private bool isAutoPassGang = false; // 是否不杠
    private bool isOnlyRon = false; // 是否自动荣和（点炮）
    private bool isOnlyTsumo = false; // 是否自动自摸
    private bool isMingPaiPanelExpanded = false; // 鸣牌面板是否展开
    private bool isAutoCutLocked = false; // 立直后自动摸切锁定

    // 公共属性，供外部访问
    public bool IsAutoArrangeHandCards { get => isAutoArrangeHandCards; }
    public bool IsAutoBuhua { get => isAutoBuhua; }
    public bool IsAutoHepai { get => isAutoHepai; }
    public bool IsAutoCut { get => isAutoCut; }
    public bool IsAutoPass { get => isAutoPass; }
    public bool IsAutoPassChi { get => isAutoPassChi; }
    public bool IsAutoPassPeng { get => isAutoPassPeng; }
    public bool IsAutoPassGang { get => isAutoPassGang; }
    public bool IsOnlyRon { get => isOnlyRon; }
    public bool IsOnlyTsumo { get => isOnlyTsumo; }
    public bool IsAutoCutLocked { get => isAutoCutLocked; }

    // 是否应当自动荣和：跟随「只和荣和」子选项
    public bool ShouldAutoWinRon() {
        return isOnlyRon;
    }

    // 是否应当自动自摸：跟随「只和自摸」子选项
    public bool ShouldAutoWinTsumo() {
        return isOnlyTsumo;
    }

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
        isAutoPassChi = false;
        isAutoPassPeng = false;
        isAutoPassGang = false;
        isOnlyRon = false;
        isOnlyTsumo = false;
        isAutoCutLocked = false;
        // 保留 isAutoBuhua 和 isAutoArrangeHandCards 的 current值

        // 鸣牌/牌张面板初始隐藏
        if (mingPaiPanel != null) mingPaiPanel.SetActive(false);
        EnsureTilePassSettingPanel();
        if (tilePassSettingPanel != null) {
            tilePassSettingPanel.Initialize();
            tilePassSettingPanel.SetPanelVisible(false);
        }
        isMingPaiPanelExpanded = false;

        SetSpectatorOnlyLayout(false);

        // 更新显示
        UpdateAllTextColors();
        // 为每个文本添加点击功能
        AddClickListeners();
    }

    /// <summary>实时观战：仅保留自动排列手牌，其余自动操作与鸣牌展开隐藏且不起效。</summary>
    public void InitializeForSpectator() {
        gameObject.SetActive(true);

        isAutoHepai = false;
        isAutoPass = false;
        isAutoCut = false;
        isAutoPassChi = false;
        isAutoPassPeng = false;
        isAutoPassGang = false;
        isOnlyRon = false;
        isOnlyTsumo = false;
        isAutoCutLocked = false;
        isAutoBuhua = false;

        if (mingPaiPanel != null) mingPaiPanel.SetActive(false);
        if (tilePassSettingPanel != null) tilePassSettingPanel.SetPanelVisible(false);
        isMingPaiPanelExpanded = false;

        SetSpectatorOnlyLayout(true);

        UpdateAllTextColors();
        AddClickListeners();
    }

    private void SetSpectatorOnlyLayout(bool spectatorOnly) {
        SetGameplayAutoActionsVisible(!spectatorOnly);
        SetOtherActionPanelVisible(!spectatorOnly);
        if (arrangeHandCardsText != null) {
            arrangeHandCardsText.gameObject.SetActive(true);
        }
    }

    private void SetGameplayAutoActionsVisible(bool visible) {
        SetTextActive(autoHepaiText, visible);
        SetTextActive(autoCutCardText, visible);
        SetTextActive(autoPassText, visible);
        SetTextActive(autoBuhuaText, visible);
    }

    private void SetOtherActionPanelVisible(bool visible) {
        if (otherActionPanel != null) {
            otherActionPanel.SetActive(visible);
            if (!visible) {
                if (mingPaiPanel != null) mingPaiPanel.SetActive(false);
                if (tilePassSettingPanel != null) tilePassSettingPanel.SetPanelVisible(false);
                isMingPaiPanelExpanded = false;
            }
            return;
        }

        SetTextActive(expandButtonText, visible);
        if (!visible) {
            if (mingPaiPanel != null) mingPaiPanel.SetActive(false);
            if (tilePassSettingPanel != null) tilePassSettingPanel.SetPanelVisible(false);
            isMingPaiPanelExpanded = false;
        }
        SetTextActive(autoPassChiText, visible);
        SetTextActive(autoPassPengText, visible);
        SetTextActive(autoPassGangText, visible);
        SetTextActive(autoOnlyRonText, visible);
        SetTextActive(autoOnlyTsumoText, visible);
    }

    private static void SetTextActive(TMP_Text text, bool visible) {
        if (text != null) {
            text.gameObject.SetActive(visible);
        }
    }

    // 为每个文本添加点击监听器
    private void AddClickListeners(){
        AddClickListener(arrangeHandCardsText, ToggleArrangeHandCards);
        AddClickListener(autoHepaiText, ToggleAutoHepai);
        AddClickListener(autoCutCardText, ToggleAutoCutCard);
        AddClickListener(autoPassText, ToggleAutoPass);
        AddClickListener(autoBuhuaText, ToggleAutoBuhua);
        AddClickListener(expandButtonText, ToggleMingPaiPanel);
        AddClickListener(autoPassChiText, ToggleAutoPassChi);
        AddClickListener(autoPassPengText, ToggleAutoPassPeng);
        AddClickListener(autoPassGangText, ToggleAutoPassGang);
        AddClickListener(autoOnlyRonText, ToggleOnlyRon);
        AddClickListener(autoOnlyTsumoText, ToggleOnlyTsumo);
    }

    // 为TMP_Text添加点击监听器
    private void AddClickListener(TMP_Text text, System.Action action){
        if (text == null) return;
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
        bool enabling = !isAutoArrangeHandCards;
        ToggleAutoOption(ref isAutoArrangeHandCards, arrangeHandCardsText);
        if (enabling && isAutoArrangeHandCards && GameCanvas.Instance != null) {
            GameCanvas.Instance.SortMainHandByTileIdIfNeeded();
        }
    }

    public void SetAutoArrangeHandCards(bool value) {
        isAutoArrangeHandCards = value;
        UpdateTextColor(arrangeHandCardsText, isAutoArrangeHandCards);
    }

    // 切换自动胡牌（与子选项只和荣和、只和自摸联动）
    private void ToggleAutoHepai(){
        isAutoHepai = !isAutoHepai;
        if (isAutoHepai) {
            isOnlyRon = true;
            isOnlyTsumo = true;
        } else {
            isOnlyRon = false;
            isOnlyTsumo = false;
        }
        UpdateHepaiGroupColors();
    }

    // 切换自动出牌
    private void ToggleAutoCutCard(){
        if (isAutoCutLocked) return;
        ToggleAutoOption(ref isAutoCut, autoCutCardText);
    }

    public void SetAutoCutLocked(bool locked){
        isAutoCutLocked = locked;
        if (locked) {
            isAutoCut = true;
        }
        UpdateTextColor(autoCutCardText, isAutoCut);
    }

    // 切换自动过牌（与子选项不吃、不碰、不杠联动）
    private void ToggleAutoPass(){
        isAutoPass = !isAutoPass;
        if (isAutoPass) {
            isAutoPassChi = true;
            isAutoPassPeng = true;
            isAutoPassGang = true;
        } else {
            isAutoPassChi = false;
            isAutoPassPeng = false;
            isAutoPassGang = false;
        }
        UpdatePassGroupColors();
    }

    // 切换自动补花
    private void ToggleAutoBuhua(){
        ToggleAutoOption(ref isAutoBuhua, autoBuhuaText);
    }

    // 切换鸣牌/牌张面板展开/收起
    private void ToggleMingPaiPanel(){
        isMingPaiPanelExpanded = !isMingPaiPanelExpanded;
        if (mingPaiPanel != null) mingPaiPanel.SetActive(isMingPaiPanelExpanded);
        if (tilePassSettingPanel != null) tilePassSettingPanel.SetPanelVisible(isMingPaiPanelExpanded);
    }

    /// <summary>当前河牌/加杠牌是否在牌张设置的自动过列表中。</summary>
    public bool ShouldAutoPassForCurrentDiscard() {
        return tilePassSettingPanel != null && tilePassSettingPanel.ShouldAutoPassForCurrentDiscard();
    }

    private void EnsureTilePassSettingPanel() {
        if (tilePassSettingPanel == null) {
            tilePassSettingPanel = GetComponentInChildren<TilePassSettingPanel>(true);
        }
    }

    // 切换不吃
    private void ToggleAutoPassChi(){
        isAutoPassChi = !isAutoPassChi;
        SyncAutoPassFromChildren();
        UpdatePassGroupColors();
    }

    // 切换不碰
    private void ToggleAutoPassPeng(){
        isAutoPassPeng = !isAutoPassPeng;
        SyncAutoPassFromChildren();
        UpdatePassGroupColors();
    }

    // 切换不杠
    private void ToggleAutoPassGang(){
        isAutoPassGang = !isAutoPassGang;
        SyncAutoPassFromChildren();
        UpdatePassGroupColors();
    }

    // 切换只和荣和
    private void ToggleOnlyRon(){
        isOnlyRon = !isOnlyRon;
        SyncAutoHepaiFromChildren();
        UpdateHepaiGroupColors();
    }

    // 切换只和自摸
    private void ToggleOnlyTsumo(){
        isOnlyTsumo = !isOnlyTsumo;
        SyncAutoHepaiFromChildren();
        UpdateHepaiGroupColors();
    }

    private void SyncAutoPassFromChildren() {
        isAutoPass = isAutoPassChi && isAutoPassPeng && isAutoPassGang;
    }

    private void SyncAutoHepaiFromChildren() {
        isAutoHepai = isOnlyRon && isOnlyTsumo;
    }

    private void UpdatePassGroupColors() {
        UpdateTextColor(autoPassText, isAutoPass);
        UpdateTextColor(autoPassChiText, isAutoPassChi);
        UpdateTextColor(autoPassPengText, isAutoPassPeng);
        UpdateTextColor(autoPassGangText, isAutoPassGang);
    }

    private void UpdateHepaiGroupColors() {
        UpdateTextColor(autoHepaiText, isAutoHepai);
        UpdateTextColor(autoOnlyRonText, isOnlyRon);
        UpdateTextColor(autoOnlyTsumoText, isOnlyTsumo);
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
        UpdateTextColor(autoPassChiText, isAutoPassChi);
        UpdateTextColor(autoPassPengText, isAutoPassPeng);
        UpdateTextColor(autoPassGangText, isAutoPassGang);
        UpdateTextColor(autoOnlyRonText, isOnlyRon);
        UpdateTextColor(autoOnlyTsumoText, isOnlyTsumo);
    }
}
