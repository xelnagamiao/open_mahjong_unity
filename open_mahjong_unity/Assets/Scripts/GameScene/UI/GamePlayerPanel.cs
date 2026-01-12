using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class GamePlayerPanel : MonoBehaviour {
    [Header("玩家信息UI组件")]
    [SerializeField] private TMP_Text playerNameText;        // 玩家名称文本
    [SerializeField] private TMP_Text playerTitleText;       // 玩家头衔文本
    [SerializeField] private Image playerProfilePicture;     // 玩家头像

    // 设置玩家信息
    public void SetPlayerInfo(PlayerInfo playerInfo) {
        if (playerNameText != null) {
            playerNameText.text = playerInfo.username;
        }

        // 设置头衔
        if (playerTitleText != null) {
            playerTitleText.text = ConfigManager.GetTitleText(playerInfo.title_used);
        }

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
    }
}
