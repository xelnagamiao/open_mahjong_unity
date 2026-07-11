using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// 自动操作面板：主开关 + 牌张设置子面板。
/// 「自动过牌」与牌张设置中的「不吃/不碰/不明杠」联动：
///   - 开启自动过牌 → 级联勾选不吃、不碰、不明杠；
///   - 三者全部勾选时视同自动过牌亮起；
///   - 反选任一不吃/不碰/不明杠 → 自动过牌变暗。
/// 鸣牌过滤只剔除对应鸣牌项；「不点和」额外剔除荣和（与未过滤鸣牌并存时保留）。全部可操作项被筛除时才自动 pass。
/// </summary>
public class AutoAction : MonoBehaviour{
    public static AutoAction Instance { get; private set; }
    [Header("自动操作文本")]
    [SerializeField] private TMP_Text arrangeHandCardsText; // 自动排列手牌文本
    [SerializeField] private TMP_Text autoHepaiText; // 自动胡牌文本
    [SerializeField] private TMP_Text autoCutCardText; // 自动出牌文本
    [SerializeField] private TMP_Text autoPassText; // 自动过牌（级联不吃/不碰/不明杠；全部可操作项被筛除才自动 pass）
    [SerializeField] private TMP_Text autoBuhuaText; // 自动补花文本

    [Header("鸣牌展开")]
    [SerializeField] private GameObject otherActionPanel; // 鸣牌展开及子选项（观战时隐藏整块）
    [SerializeField] private TMP_Text expandButtonText; // 展开按钮（附加按钮的文本）
    [SerializeField] private GameObject mingPaiPanel; // 鸣牌选项面板
    [SerializeField] private TilePassSettingPanel tilePassSettingPanel; // 牌张设置面板（可运行时生成）

    [Header("颜色配置")]
    [SerializeField] private Color falseColor = Color.white; // false时的颜色（白色）
    [SerializeField] private Color trueColor = new Color(1f, 0.5f, 0f); // true时的颜色（橙色）

    [Header("自动操作配置")]
    private bool isAutoArrangeHandCards = true; // 是否自动排列手牌
    private bool isAutoBuhua = true; // 是否自动补花
    private bool isAutoHepai = false; // 是否自动胡牌
    private bool isAutoCut = false; // 是否自动出牌
    private bool isAutoPass = false; // 自动过牌：与「不吃+不碰+不明杠」全选联动；筛光鸣牌后无剩余可操作项才自动 pass
    private bool isMingPaiPanelExpanded = false; // 鸣牌面板是否展开
    private bool isAutoCutLocked = false; // 立直后自动摸切锁定

    // 公共属性，供外部访问
    public bool IsAutoArrangeHandCards { get => isAutoArrangeHandCards; }
    public bool IsAutoBuhua { get => isAutoBuhua; }
    public bool IsAutoHepai { get => isAutoHepai; }
    public bool IsAutoCut { get => isAutoCut; }
    public bool IsAutoPass { get => isAutoPass; }
    public bool IsAutoCutLocked { get => isAutoCutLocked; }

    // 牌张设置 · 不吃/不碰/不明杠（与「自动过牌」联动；仅过滤对应鸣牌，不过和牌）
    public bool IsPassChi => GetTilePassPanel()?.PassChi ?? false;
    public bool IsPassPeng => GetTilePassPanel()?.PassPeng ?? false;
    public bool IsPassMingGang => GetTilePassPanel()?.PassMingGang ?? false;
    // 牌张设置 · 和牌约束（不点和还参与鸣牌 pass 判定；不自摸/不抢杠仅在「自动胡牌」开启时生效）
    public bool IsNoRon => GetTilePassPanel()?.NoRon ?? false;
    public bool IsNoTsumo => GetTilePassPanel()?.NoTsumo ?? false;
    public bool IsNoRobKong => GetTilePassPanel()?.NoRobKong ?? false;

    // 是否应当自动荣和：自动胡牌开启，且牌张设置未勾选「不点和」
    public bool ShouldAutoWinRon() {
        if (!isAutoHepai) return false;
        return !IsNoRon;
    }

    // 是否应当自动抢杠和：自动胡牌开启，且牌张设置未勾选「不抢杠」
    public bool ShouldAutoWinRobKong() {
        if (!isAutoHepai) return false;
        return !IsNoRobKong;
    }

    // 是否应当自动自摸：自动胡牌开启，且牌张设置未勾选「不自摸」
    public bool ShouldAutoWinTsumo() {
        if (!isAutoHepai) return false;
        return !IsNoTsumo;
    }

    /// <summary>牌张设置中是否启用了任意鸣牌过滤（勾选牌张 / 不吃 / 不碰 / 不明杠）。</summary>
    public bool HasAnyTilePassMingPaiOption() {
        TilePassSettingPanel panel = GetTilePassPanel();
        return panel != null && panel.HasAnyMingPaiPassOption;
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
        isAutoCutLocked = false;
        // 保留 isAutoBuhua 和 isAutoArrangeHandCards 的 current值

        // 鸣牌/牌张面板初始隐藏
        if (mingPaiPanel != null) mingPaiPanel.SetActive(false);
        EnsureTilePassSettingPanel();
        if (tilePassSettingPanel != null) {
            tilePassSettingPanel.Initialize();
            tilePassSettingPanel.SetPanelVisible(false);
            tilePassSettingPanel.OnMeldPassOptionsChanged = SyncAutoPassFromMeldFilters;
        }
        isMingPaiPanelExpanded = false;

        SetSpectatorOnlyLayout(false);
        ApplyCompactButtonLabels();
        ApplyBuhuaButtonVisibility();

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
        isAutoCutLocked = false;
        isAutoBuhua = false;

        if (mingPaiPanel != null) mingPaiPanel.SetActive(false);
        if (tilePassSettingPanel != null) tilePassSettingPanel.SetPanelVisible(false);
        isMingPaiPanelExpanded = false;

        SetSpectatorOnlyLayout(true);
        ApplyCompactButtonLabels();

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
        SetTextActive(autoBuhuaText, visible && ShouldShowBuhuaAutoActionButton());
    }

    private void ApplyBuhuaButtonVisibility() {
        SetTextActive(autoBuhuaText, ShouldShowBuhuaAutoActionButton());
    }

    /// <summary>无补花流程的规则（长沙/四川等）隐藏自动补花按钮。</summary>
    private static bool ShouldShowBuhuaAutoActionButton() {
        NormalGameStateManager gsm = NormalGameStateManager.Instance;
        if (gsm == null || string.IsNullOrEmpty(gsm.roomRule)) {
            return true;
        }
        return gsm.roomRule != "changsha" && gsm.roomRule != "sichuan";
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
    }

    private void ApplyCompactButtonLabels() {
        if (expandButtonText != null) expandButtonText.text = "展";
        if (arrangeHandCardsText != null) arrangeHandCardsText.text = "理";
        if (autoHepaiText != null) autoHepaiText.text = "和";
        if (autoBuhuaText != null) autoBuhuaText.text = "补";
        if (autoPassText != null) autoPassText.text = "鸣";
        if (autoCutCardText != null) autoCutCardText.text = "切";
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

    // 切换自动胡牌
    private void ToggleAutoHepai(){
        ToggleAutoOption(ref isAutoHepai, autoHepaiText);
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

    // 切换自动过牌：级联同步「不吃/不碰/不明杠」三选项
    private void ToggleAutoPass(){
        bool enabling = !isAutoPass;
        isAutoPass = enabling;
        UpdateTextColor(autoPassText, isAutoPass);
        TilePassSettingPanel panel = GetTilePassPanel();
        if (panel != null) {
            panel.SetMeldPassOptions(enabling, enabling, enabling);
        }
    }

    /// <summary>根据牌张设置中「不吃/不碰/不明杠」是否全选，同步主面板「自动过牌」显示。</summary>
    private void SyncAutoPassFromMeldFilters() {
        bool allMeldPassOn = IsPassChi && IsPassPeng && IsPassMingGang;
        if (isAutoPass == allMeldPassOn) {
            return;
        }
        isAutoPass = allMeldPassOn;
        UpdateTextColor(autoPassText, isAutoPass);
    }

    // 切换自动补花
    private void ToggleAutoBuhua(){
        ToggleAutoOption(ref isAutoBuhua, autoBuhuaText);
    }

    // 切换鸣牌/牌张面板展开/收起
    private void ToggleMingPaiPanel(){
        isMingPaiPanelExpanded = !isMingPaiPanelExpanded;
        if (tilePassSettingPanel != null) tilePassSettingPanel.SetPanelVisible(isMingPaiPanelExpanded);
    }

    /// <summary>
    /// 当前河牌/加杠牌是否命中牌张设置的跳过列表。
    /// 命中时不询问该牌的任何操作（含荣和/抢杠），与「自动过牌」不同。
    /// </summary>
    public bool ShouldAutoPassForCurrentDiscard() {
        return tilePassSettingPanel != null && tilePassSettingPanel.ShouldAutoPassForCurrentDiscard();
    }

    /// <summary>
    /// 当前摸入牌是否命中牌张设置的跳过列表。
    /// 命中时跳过自动自摸（仍可手动和牌），与「不自摸」不同（后者阻止一切自动自摸）。
    /// </summary>
    public bool ShouldAutoPassForCurrentDraw() {
        NormalGameStateManager gsm = NormalGameStateManager.Instance;
        if (gsm == null || tilePassSettingPanel == null) return false;
        int drawnTileId = gsm.GetCurrentDrawTileId();
        return tilePassSettingPanel.ShouldAutoPassForDrawnTile(drawnTileId);
    }

    private TilePassSettingPanel GetTilePassPanel() {
        EnsureTilePassSettingPanel();
        return tilePassSettingPanel;
    }

    private void EnsureTilePassSettingPanel() {
        if (tilePassSettingPanel == null) {
            tilePassSettingPanel = GetComponentInChildren<TilePassSettingPanel>(true);
        }
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
