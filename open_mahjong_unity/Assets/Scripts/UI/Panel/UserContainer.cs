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
    public void SetUserInfo(string username, string userkey, int user_id) {
        UserDataManager.Instance.SetUserInfo(username, userkey, user_id);
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
        int score = UserDataManager.Instance.GuobiaoScore;
        int idx = RankConfig.GetRankIndex(rank);
        var (_, startScore, promoteScore) = RankConfig.RankTable[idx];

        if (rankText != null)
            rankText.text = rank;

        if (rankProgressBar != null) {
            float range = promoteScore - startScore;
            rankProgressBar.value = range > 0 ? (float)(score - startScore) / range : 0;
        }
    }
}
