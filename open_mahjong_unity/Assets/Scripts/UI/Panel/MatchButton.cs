using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityEngine.EventSystems;

/// <summary>
/// 匹配按钮组件：在 Inspector 中配置规则、局制、场次，自行管理遮罩与人数显示。
/// </summary>
public class MatchButton : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler, IPointerUpHandler {
    public enum MatchRule { Guobiao, Riichi, Sichuan, Qingque, RiichiSanma, SichuanXueliuExchange }
    public enum MatchGameType { Dongfeng, Banzhuang, Quanzhuang, Xuezhan, Xueliu }
    public enum MatchTier { Beginner, Intermediate, Advanced, MCRPL }

    [Header("匹配配置")]
    [SerializeField] private MatchRule rule = MatchRule.Guobiao;
    [SerializeField] private MatchGameType gameType = MatchGameType.Quanzhuang;
    [SerializeField] private MatchTier tier = MatchTier.Beginner;

    [Header("UI 组件")]
    [SerializeField] private Button button;
    [SerializeField] private Button infoButton;
    [SerializeField] private Button conditionButton;
    [SerializeField] private TMP_Text waitingCountText;
    [SerializeField] private TMP_Text playingCountText;
    [SerializeField] private GameObject mask;
    [SerializeField] private GameObject availableContent;
    [SerializeField] private GameObject restrictedContent;
    [SerializeField] private TMP_Text restrictedWaitingCountText;
    [SerializeField] private TMP_Text restrictedPlayingCountText;
    [SerializeField] private MatchDescribePanel describePanel;
    [SerializeField] private MatchLobbyView lobbyView;
    [SerializeField] private GameObject selectedFrame;
    [SerializeField] private GameObject hoverFrame;
    [SerializeField] private TMP_Text tierLabel;
    [SerializeField] private TMP_Text modeLabel;
    private bool selected, pointerInside, pointerPressed;
    public int TierIndex => (int)tier;
    public MatchRule Rule => rule;
    public string TierTitle => tierLabel != null ? tierLabel.text : tier.ToString();
    public string ModeTitle => modeLabel != null ? modeLabel.text : "";
    public string RuleId => rule == MatchRule.SichuanXueliuExchange ? RankedRules.XueliuExchangeRule : rule == MatchRule.RiichiSanma ? RiichiSanmaRankConfig.Rule : rule.ToString().ToLowerInvariant();
    public bool IsElo => !RankedRules.IsGrade(RuleId);
    public bool IsAvailable => rule==MatchRule.Guobiao || (rule==MatchRule.Riichi || rule==MatchRule.RiichiSanma) && tier!=MatchTier.MCRPL && gameType<=MatchGameType.Banzhuang || (rule==MatchRule.Qingque || rule==MatchRule.Sichuan || rule==MatchRule.SichuanXueliuExchange) && gameType==MatchGameType.Quanzhuang;
    [SerializeField] private RankPolicyPanel policyPanel;
    public void SetSelected(bool value) {
        selected = value;
        RefreshFrame();
    }

    private void RefreshFrame() {
        bool interactive = button != null && button.IsInteractable();
        bool pressed = pointerPressed && pointerInside && interactive;
        if (selectedFrame != null) selectedFrame.SetActive(selected || pressed);
        if (hoverFrame != null) hoverFrame.SetActive(pointerInside && interactive && !selected && !pressed);
    }

    public void OnPointerEnter(PointerEventData eventData) {
        pointerInside = true;
        RefreshFrame();
    }

    public void OnPointerExit(PointerEventData eventData) {
        pointerInside = false;
        RefreshFrame();
    }

    public void OnPointerDown(PointerEventData eventData) {
        if (eventData.button != PointerEventData.InputButton.Left) return;
        pointerPressed = true;
        RefreshFrame();
    }

    public void OnPointerUp(PointerEventData eventData) {
        if (eventData.button != PointerEventData.InputButton.Left) return;
        pointerPressed = false;
        RefreshFrame();
    }

    private static readonly string[] TierKeys = { "beginner", "intermediate", "advanced", "mcrpl" };
    private static readonly string[] GameTypeKeys = { "dongfeng", "banzhuang", "quanzhuang", "xuezhan", "xueliu" };

    /// <summary>
    /// 由配置生成的队列标识符，如 "beginner_dongfeng"
    /// </summary>
    // Sichuan retains its original transport ID; its authored game type is full-length.
    public string QueueType => rule==MatchRule.Sichuan && gameType==MatchGameType.Quanzhuang
        ? "sichuan_elo_xuezhan"
        : (rule==MatchRule.Guobiao ? "" : RuleId+"_")+(IsElo ? "elo" : TierKeys[(int)tier])+"_"+GameTypeKeys[(int)gameType];

    private void Reset() {
        AutoBind();
    }

    private void Awake() {
        AutoBind();
    }

    private void OnEnable() {
        AutoBind();
        RefreshFrame();
        if (button != null) {
            button.onClick.RemoveListener(OnClick);
            button.onClick.AddListener(OnClick);
        }
        if (infoButton != null) {
            infoButton.onClick.RemoveListener(OnInfoClick);
            infoButton.onClick.AddListener(OnInfoClick);
        }
        if (conditionButton != null) {
            conditionButton.onClick.RemoveListener(OnInfoClick);
            conditionButton.onClick.AddListener(OnInfoClick);
        }
    }

    private void OnDisable() {
        pointerInside = pointerPressed = false;
        RefreshFrame();
        if (button != null) {
            button.onClick.RemoveListener(OnClick);
        }
        if (infoButton != null) {
            infoButton.onClick.RemoveListener(OnInfoClick);
        }
        if (conditionButton != null) conditionButton.onClick.RemoveListener(OnInfoClick);
    }

    private void AutoBind() {
        if (button == null) button = GetComponent<Button>();
        if (button == null) button = GetComponentInChildren<Button>(true);
    }

    /// <summary>
    /// 根据当前玩家段位刷新遮罩状态
    /// </summary>
    public void RefreshMask() {
        bool canEnter = CanEnter();
        if (mask != null) mask.SetActive(!canEnter);
        if (availableContent != null) availableContent.SetActive(canEnter);
        if (restrictedContent != null) restrictedContent.SetActive(!canEnter);
    }

    public bool CanEnter() {
        if (!IsAvailable || UserDataManager.Instance == null) return false;
        if(IsElo)return true;
        string rankName = UserDataManager.Instance.GetRating(RuleId).rank_name;
        int rankLevel = RankConfig.GetRankLevel(rankName);
        bool canPlay = RankConfig.CanPlayTier(
            rankLevel,
            TierKeys[(int)tier],
            UserDataManager.Instance.IsBeginnerQualified,
            UserDataManager.Instance.IsIntermediateQualified,
            UserDataManager.Instance.IsAdvancedQualified,
            UserDataManager.Instance.IsMcrplQualified
        );
        return canPlay;
    }

    /// <summary>
    /// 更新等待/游戏中人数显示
    /// </summary>
    public void UpdateCounts(int waiting, int playing) {
        string waitingText = IsAvailable ? waiting.ToString() : "—";
        string playingText = IsAvailable ? playing.ToString() : "—";
        if (waitingCountText != null) waitingCountText.text = waitingText;
        if (playingCountText != null) playingCountText.text = playingText;
        if (restrictedWaitingCountText != null) restrictedWaitingCountText.text = waitingText;
        if (restrictedPlayingCountText != null) restrictedPlayingCountText.text = playingText;
    }

    private void OnClick() {
        if(lobbyView != null) { lobbyView.ToggleSelection(this); return; }
        if (UserDataManager.Instance.IsTourist) {
            NotificationManager.Instance.ShowTip("匹配", false, "游客无法进行排位匹配，请先注册账号");
            return;
        }
        if (mask != null && mask.activeSelf) {
            NotificationManager.Instance.ShowTip("匹配", false, "当前段位无法加入该队列");
            return;
        }
        Debug.Log($"[MatchButton] 点击匹配按钮，queueType={QueueType}");
        MatchNetworkManager.Instance.SendJoinQueue(QueueType);
    }

    private void OnInfoClick() {
        if(!IsAvailable) { NotificationManager.Instance?.ShowTip("匹配",false,"本规则的段位匹配暂未开放"); return; }
        if(IsElo)policyPanel?.ShowElo();
        else describePanel?.ShowForQueue(QueueType);
    }
}
