using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class GamePlayerPanel : MonoBehaviour {
    [Header("玩家信息UI组件")]
    [SerializeField] private TMP_Text playerNameText;        // 玩家名称文本
    [SerializeField] private TMP_Text playerTitleText;       // 玩家头衔文本
    [SerializeField] private Image playerProfilePicture;     // 玩家头像
    [SerializeField] private Image playerProfileEdgePicture;   // 玩家头像边框
    [SerializeField] private Image playerIslossconnPicture; // 玩家是否掉线图片
    [SerializeField] private GameObject playerIsPeidaPicture; // 玩家是否陪打图片
    [SerializeField] private GameObject playerLangyongBadge; // 浪涌麻将：鸣牌次数标记（tag: langyong_N）
    [SerializeField] private TMP_Text playerLangyongCountText; // 浪涌鸣牌次数文字（可选）
    [SerializeField] private Button GoToRecordSelectButton; // 牌谱模式下切换到该玩家视角

    private void Awake() {
        playerIslossconnPicture.gameObject.SetActive(false);
        playerIsPeidaPicture.gameObject.SetActive(false);
        if (playerLangyongBadge != null) playerLangyongBadge.SetActive(false);
    }


    public void SetPlayerInfo(PlayerInfo playerInfo, string state, string position = null) {
        if (state == "gamestate") {
            playerNameText.text = StreamerModeHelper.FormatGamestatePlayerName(
                playerInfo.username, position, playerInfo.user_id);
        } else {
            playerNameText.text = playerInfo.username;
        }
        // 设置头衔
        playerTitleText.text = ConfigManager.GetTitleText(playerInfo.title_used);


        if (playerProfilePicture != null) {
            // 加载头像
            Sprite profileSprite = Resources.Load<Sprite>($"image/Profiles/{playerInfo.profile_used}");
            if (profileSprite != null) {
                playerProfilePicture.sprite = profileSprite;
            }

            // 设置 ProfileOnClick 的 user_id
            ProfileOnClick profileOnClick = playerProfilePicture.gameObject.GetComponent<ProfileOnClick>();
            if (profileOnClick != null) {
                profileOnClick.user_id = playerInfo.user_id;
            }
        }

        UpdateTagList(playerInfo.tag_list);

        // 牌谱模式下启用“切换视角”按钮；对局模式隐藏
        if (GoToRecordSelectButton != null) {
            bool isRecordState = state == "record";
            GoToRecordSelectButton.gameObject.SetActive(isRecordState);
            GoToRecordSelectButton.onClick.RemoveAllListeners();
            if (isRecordState) {
                int userId = playerInfo.user_id;
                GoToRecordSelectButton.onClick.AddListener(() => {
                    if (GameRecordManager.Instance != null) {
                        GameRecordManager.Instance.SwitchRecordPerspectiveToUser(userId);
                    }
                });
            }
        }
    }

    // 更新标签列表显示（立直/振听由对局内其他 UI 表现，此处处理掉线、陪打、浪涌鸣牌次数等）
    public void UpdateTagList(string[] tag_list) {
        playerIslossconnPicture.gameObject.SetActive(false);
        playerIsPeidaPicture.gameObject.SetActive(false);
        if (playerLangyongBadge != null) playerLangyongBadge.SetActive(false);
        if (playerLangyongCountText != null) playerLangyongCountText.text = "";

        if (tag_list != null) {
            int langyongCount = -1;
            foreach(var item in tag_list) {
                if (item == "offline") {
                    playerIslossconnPicture.gameObject.SetActive(true);
                }
                if (item == "peida") {
                    playerIsPeidaPicture.gameObject.SetActive(true);
                }
                // langyong_wave 由 GameCanvas 全局显示；此处仅显示该玩家个人鸣牌次数 langyong_N
                if (item != null && item.StartsWith("langyong_") && item != "langyong_wave") {
                    if (int.TryParse(item.Substring("langyong_".Length), out int count)) {
                        langyongCount = count;
                    }
                }
            }
            if (langyongCount >= 0 && playerLangyongBadge != null) {
                playerLangyongBadge.SetActive(true);
                if (playerLangyongCountText != null) {
                    playerLangyongCountText.text = $"浪涌点数*{langyongCount}";
                }
            }
        }
    }

    public void Clear() {
        playerNameText.text = "";
        playerTitleText.text = "";
        if (playerProfilePicture != null) {
            Sprite profileSprite = Resources.Load<Sprite>("image/Profiles/1");
            if (profileSprite != null) playerProfilePicture.sprite = profileSprite;
            ProfileOnClick profileOnClick = playerProfilePicture.GetComponent<ProfileOnClick>();
            if (profileOnClick != null) profileOnClick.user_id = 0;
        }
        UpdateTagList(null);
        if (GoToRecordSelectButton != null) GoToRecordSelectButton.gameObject.SetActive(false);
    }
}
