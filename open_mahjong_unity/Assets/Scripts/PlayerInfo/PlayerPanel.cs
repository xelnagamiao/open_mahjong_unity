using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class PlayerPanel : MonoBehaviour
{
    // Start is called before the first frame update
    [SerializeField] private Transform SetPlayerInfoPos;
    [SerializeField] private TMP_InputField useridInputField;
    [SerializeField] private Button searchUseridButton;
    [SerializeField] private Button backButton;
    [SerializeField] private GameObject PlayerInfoPanelPrefab;
    

    public static PlayerPanel Instance;
    private void Awake()
    {
        Instance = this;
    }
    void Start()
    {
        searchUseridButton.onClick.AddListener(OnSearchUseridButtonClick);
        backButton.onClick.AddListener(WindowsManager.Instance.ClosePlayerInfoPanel);
    }

    // 查询玩家信息
    private void OnSearchUseridButtonClick()
    {
        string userid = useridInputField.text;
        if (string.IsNullOrEmpty(userid))
        {
            return;
        }
        // 发送查询id请求
        NetworkManager.Instance.GetPlayerInfo(userid);
    }

    // 获取玩家信息响应
    public void GetPlayerInfoResponse(bool success, string message, PlayerInfoResponse playerInfo)
    {
        if (success && playerInfo != null)
        {
            // 创建玩家信息面板
            GameObject playerInfoPanelObject = Instantiate(PlayerInfoPanelPrefab, SetPlayerInfoPos);
            PlayerInfoPanel playerInfoPanel = playerInfoPanelObject.GetComponent<PlayerInfoPanel>();
            playerInfoPanel.ShowPlayerInfo(playerInfo);
        }
        else
        {
            Debug.LogError($"获取玩家信息失败: {message}");
        }
    }
}
