using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// 匹配按钮组件：在 Inspector 中配置规则、局制、场次，自行管理遮罩与人数显示。
/// </summary>
public class MatchButton : MonoBehaviour {
    public enum MatchRule { Guobiao }
    public enum MatchGameType { Dongfeng, Banzhuang, Quanzhuang }
    public enum MatchTier { Beginner, Intermediate, Advanced, MCRPL }

    [Header("匹配配置")]
    [SerializeField] private MatchRule rule = MatchRule.Guobiao;
    [SerializeField] private MatchGameType gameType = MatchGameType.Quanzhuang;
    [SerializeField] private MatchTier tier = MatchTier.Beginner;

    [Header("UI 组件")]
    [SerializeField] private Button button;
    [SerializeField] private Button infoButton;
    [SerializeField] private TMP_Text waitingCountText;
    [SerializeField] private TMP_Text playingCountText;
    [SerializeField] private GameObject mask;
    [SerializeField] private MatchDescribePanel describePanel;

    private static readonly string[] TierKeys = { "beginner", "intermediate", "advanced", "mcrpl" };
    private static readonly string[] GameTypeKeys = { "dongfeng", "banzhuang", "quanzhuang" };

    /// <summary>
    /// 由配置生成的队列标识符，如 "beginner_dongfeng"
    /// </summary>
    public string QueueType => $"{TierKeys[(int)tier]}_{GameTypeKeys[(int)gameType]}";

    private void Reset() {
        AutoBind();
    }

    private void Awake() {
        AutoBind();
    }

    private void OnEnable() {
        AutoBind();
        if (button != null) {
            button.onClick.RemoveListener(OnClick);
            button.onClick.AddListener(OnClick);
        }
        if (infoButton != null) {
            infoButton.onClick.RemoveListener(OnInfoClick);
            infoButton.onClick.AddListener(OnInfoClick);
        }
    }

    private void OnDisable() {
        if (button != null) {
            button.onClick.RemoveListener(OnClick);
        }
        if (infoButton != null) {
            infoButton.onClick.RemoveListener(OnInfoClick);
        }
    }

    private void AutoBind() {
        if (button == null) button = GetComponent<Button>();
        if (button == null) button = GetComponentInChildren<Button>(true);
    }

    /// <summary>
    /// 根据当前玩家段位刷新遮罩状态
    /// </summary>
    public void RefreshMask() {
        if (mask == null) return;
        if (UserDataManager.Instance == null) {
            mask.SetActive(true);
            return;
        }
        string rankName = UserDataManager.Instance.GuobiaoRank;
        bool isMcrpl = UserDataManager.Instance.IsMcrplQualified;
        int rankLevel = RankConfig.GetRankLevel(rankName);
        bool canPlay = RankConfig.CanPlayTier(rankLevel, TierKeys[(int)tier], isMcrpl);
        mask.SetActive(!canPlay);
    }

    /// <summary>
    /// 更新等待/游戏中人数显示
    /// </summary>
    public void UpdateCounts(int waiting, int playing) {
        if (waitingCountText != null) waitingCountText.text = waiting.ToString();
        if (playingCountText != null) playingCountText.text = playing.ToString();
    }

    private void OnClick() {
        if (mask != null && mask.activeSelf) return;
        Debug.Log($"[MatchButton] 点击匹配按钮，queueType={QueueType}");
        MatchNetworkManager.Instance?.SendJoinQueue(QueueType);
    }

    private void OnInfoClick() {
        describePanel?.ShowForQueue(QueueType);
    }
}
