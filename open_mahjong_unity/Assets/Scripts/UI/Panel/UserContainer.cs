using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class UserContainer : MonoBehaviour {
    public static UserContainer Instance { get; private set; }

    [Header("用户信息UI组件")]
    [SerializeField] private TMP_Text usernameText;
    [SerializeField] private Image profileImage;
    [SerializeField] private TMP_Text titleText;

    [Header("段位信息UI组件")]
    [SerializeField] private TMP_Text rankText;
    [SerializeField] private Slider rankProgressBar;
    [SerializeField] private TMP_Text rankScoreText;

    private void Awake() {
        if (Instance != null && Instance != this) {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    private void OnEnable() {
        RefreshRankDisplay();
    }

    // 设置用户信息（仅负责UI显示，数据由UserDataManager管理）
    public void SetUserInfo(string username, string userkey, int user_id, bool isTourist = false) {
        UserDataManager.Instance.SetUserInfo(username, userkey, user_id, isTourist);
    }

    // 显示用户设置
    public void ShowUserSettings(UserSettings userSettings) {
        usernameText.text = UserDataManager.Instance.Username;
        Sprite profileSprite = Resources.Load<Sprite>($"image/Profiles/{UserDataManager.Instance.ProfileImageId}");
        if (profileSprite != null) {
            profileImage.sprite = profileSprite;
        }

        ProfileOnClick profileOnClick = profileImage.gameObject.GetComponent<ProfileOnClick>();
        if (profileOnClick != null) {
            profileOnClick.user_id = UserDataManager.Instance.UserId;
        }

        titleText.text = ConfigManager.GetTitleText(UserDataManager.Instance.TitleId);
        RefreshRankDisplay();
    }

    /// <summary>
    /// 刷新段位文本和进度条
    /// </summary>
    public void RefreshRankDisplay() {
        string rank = UserDataManager.Instance.GuobiaoRank;
        float score = UserDataManager.Instance.GuobiaoScore;
        int idx = RankConfig.GetRankIndex(rank);
        var (_, _, promoteScore) = RankConfig.RankTable[idx];

        if (rankText != null)
            rankText.text = rank;

        if (rankProgressBar != null) {
            // 进度按 0 → 升段分（与文案 score/promoteScore、Web 一致）
            rankProgressBar.value = promoteScore > 0 ? Mathf.Clamp01(score / promoteScore) : 0;
        }
        if (rankScoreText != null) rankScoreText.text = $"{score:F1}/{promoteScore}";
    }
}
