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

    [SerializeField] private Button rankSwitchButton;
    public string CurrentRule { get; private set; } = "guobiao";
    public void CycleRule(){CurrentRule=RankedRules.Next(CurrentRule);RefreshRankDisplay();}
    private void Awake() {
        if (Instance != null && Instance != this) {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        rankSwitchButton?.onClick.AddListener(CycleRule);
    }

    private void OnEnable() {
        if(UserDataManager.Instance!=null)UserDataManager.Instance.RankDataChanged+=RefreshRankDisplay;
        GameSettings.TitleChanged += RefreshTitle;
        GameSettings.AppearanceChanged += RefreshAppearance;
        RefreshAppearance(null);
        RefreshTitle(0, 1);
        RefreshRankDisplay();
    }

    private void OnDisable() {
        if(UserDataManager.Instance!=null)UserDataManager.Instance.RankDataChanged-=RefreshRankDisplay;
        GameSettings.TitleChanged -= RefreshTitle;
        GameSettings.AppearanceChanged -= RefreshAppearance;
    }

    private void RefreshAppearance(InventoryAppearance appearance) {
        if (UserDataManager.Instance == null || profileImage == null) return;
        profileImage.sprite = ConfigManager.GetProfileSprite(UserDataManager.Instance.ProfileImageId);
        AvatarFrameGraphic.Apply(profileImage, UserDataManager.Instance.AvatarFrameId);
    }

    private void RefreshTitle(int userId, int titleId) {
        if (UserDataManager.Instance == null || titleText == null) return;
        titleText.richText = false;
        titleText.text = ConfigManager.GetTitleText(UserDataManager.Instance.TitleId);
    }

    // 设置用户信息（仅负责UI显示，数据由UserDataManager管理）
    public void SetUserInfo(string username, string userkey, int user_id, bool isTourist = false) {
        UserDataManager.Instance.SetUserInfo(username, userkey, user_id, isTourist);
    }

    // 显示用户设置
    public void ShowUserSettings(UserSettings userSettings) {
        usernameText.text = UserDataManager.Instance.Username;
        RefreshAppearance(null);

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
        if(UserDataManager.Instance==null)return;
        var rating=UserDataManager.Instance.GetRating(CurrentRule);
        rankText.text=RankedRules.RankCaption(rating);
        rankScoreText.text=RankedRules.ScoreCaption(rating).Replace("  ·  ","\n");
        if(profileImage&&profileImage.TryGetComponent<ProfileOnClick>(out var click))click.ratingRule=CurrentRule;
        rankProgressBar.gameObject.SetActive(RankedRules.IsGrade(CurrentRule));
        rankProgressBar.value=RankedRules.Progress(rating);
    }
}
