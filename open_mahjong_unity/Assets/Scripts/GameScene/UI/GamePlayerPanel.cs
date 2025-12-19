using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class GamePlayerPanel : MonoBehaviour
{
    // 头衔ID到文本的映射字典
    private static Dictionary<int, string> titleDictionary = new Dictionary<int, string>
    {
        { 1, "暂无头衔" }
    };

    [Header("玩家信息UI组件")]
    [SerializeField] private TMP_Text playerNameText;        // 玩家名称文本
    [SerializeField] private TMP_Text playerTitleText;       // 玩家头衔文本
    [SerializeField] private Image playerProfilePicture;     // 玩家头像

    // 设置玩家信息
    public void SetPlayerInfo(PlayerInfo playerInfo)
    {
        if (playerNameText != null)
        {
            playerNameText.text = playerInfo.username;
        }

        // 设置头衔
        if (playerTitleText != null)
        {
            if (playerInfo.title_used > 0 && titleDictionary.ContainsKey(playerInfo.title_used))
            {
                playerTitleText.text = titleDictionary[playerInfo.title_used];
            }
            else
            {
                // 如果找不到对应的头衔，使用默认值
                playerTitleText.text = titleDictionary.ContainsKey(1) ? titleDictionary[1] : "暂无头衔";
            }
        }

        if (playerProfilePicture != null)
        {
            // 加载头像
            Sprite profileSprite = Resources.Load<Sprite>($"image/Profiles/{playerInfo.profile_used}");
            if (profileSprite != null)
            {
                playerProfilePicture.sprite = profileSprite;
            }

            // 设置 ProfileOnClick 的 user_id
            ProfileOnClick profileOnClick = playerProfilePicture.gameObject.GetComponent<ProfileOnClick>();
            if (profileOnClick != null)
            {
                profileOnClick.user_id = playerInfo.user_id;
            }
        }
    }
}
