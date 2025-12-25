using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class UserContainer : MonoBehaviour
{
    public static UserContainer Instance { get; private set; }

    [Header("用户信息UI组件")]
    [SerializeField] private TMP_Text usernameText;        // 用户名称文本
    [SerializeField] private Image profileImage;           // 用户头像
    [SerializeField] private TMP_Text titleText;           // 用户头衔文本

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    // 设置用户信息（仅负责UI显示，数据由UserDataManager管理）
    public void SetUserInfo(string username, string userkey, int user_id)
    {
        // 数据管理由UserDataManager负责
        UserDataManager.Instance.SetUserInfo(username, userkey, user_id);
        // 这里可以添加UI相关的显示逻辑，比如更新用户信息的UI元素
    }

    // 显示用户设置
    public void ShowUserSettings(UserSettings userSettings)
    {

        usernameText.text = UserDataManager.Instance.Username;
        Sprite profileSprite = Resources.Load<Sprite>($"image/Profiles/{UserDataManager.Instance.ProfileImageId}");
            if (profileSprite != null)
            {
                profileImage.sprite = profileSprite;
            }

            // 设置 ProfileOnClick 的 user_id
            ProfileOnClick profileOnClick = profileImage.gameObject.GetComponent<ProfileOnClick>();
            if (profileOnClick != null)
            {
                profileOnClick.user_id = UserDataManager.Instance.UserId;
            }

        //设置头衔
        titleText.text = ConfigManager.GetTitleText(UserDataManager.Instance.TitleId);
    }
}
