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
    [SerializeField] private Image playerProfileEdgePicture;   // 玩家头像边框
    [SerializeField] private Image playerIslossconnPicture; // 玩家是否掉线图片
    [SerializeField] private GameObject playerIsPeidaPicture; // 玩家是否陪打图片

    private void Awake() {
        playerIslossconnPicture.gameObject.SetActive(false);
        playerIsPeidaPicture.gameObject.SetActive(false);
    }


    // 设置玩家信息
    public void SetPlayerInfo(PlayerInfo playerInfo) {

        // 设置玩家名称
        playerNameText.text = playerInfo.username;
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

        foreach(var item in playerInfo.tag_list) {
            if (item == "lossconn") {
                playerIslossconnPicture.gameObject.SetActive(true);
            }
            if (item == "peida") {
                playerIsPeidaPicture.gameObject.SetActive(true);
            }
        }
    }
}
