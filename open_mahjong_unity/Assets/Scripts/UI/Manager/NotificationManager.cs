using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TMPro;
using UnityEngine.UI;

public class NotificationManager : MonoBehaviour {
    public static NotificationManager Instance { get; private set; }
    
    [Header("Tips 配置")]

    // 显示面板
    [SerializeField] private GameObject tipsPosition;
    [SerializeField] private TipsPrefab tipsPrefab;              // Tips 预制体（包含 TipsPrefab 脚本）
    [SerializeField] private float defaultTipDuration = 3f;      // Tips 默认显示时长

    [Header("PlayerInfo 配置")]
    [SerializeField] private GameObject playerInfoPosition;
    [SerializeField] private GameObject playerInfoPanelPrefab; // 玩家信息面板预制体

    [Header("Message 配置")]
    [SerializeField] private GameObject messagePosition;
    [SerializeField] private MessagePrefab messagePrefab;        // Message 预制体
    
    private void Awake() {
        if (Instance != null && Instance != this) {
            Debug.Log($"Destroying duplicate NotificationManager. Existing: {Instance}, New: {this}");
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    /// <summary>
    /// 显示 Tips 弹窗
    /// </summary>
    /// <param name="type">提示类型（例如：System/Network）</param>
    /// <param name="isSuccess">是否成功（决定颜色）</param>
    /// <param name="message">显示内容</param>
    /// <param name="duration">自定义显示时长，传入 &lt;=0 则使用默认值</param>
    public void ShowTip(string type, bool isSuccess, string message, float duration = -1f) {

        Transform parent = tipsPosition.transform;
        TipsPrefab tipInstance = Instantiate(tipsPrefab, parent.position, parent.rotation, parent);
        tipInstance.ShowMessage(type, isSuccess, message);

        float lifeTime = duration > 0f ? duration : defaultTipDuration;
        StartCoroutine(DestroyTipAfterDelay(tipInstance.gameObject, lifeTime));
    }

    /// <summary>
    /// 显示 Message 弹窗
    /// </summary>
    /// <param name="header">标题</param>
    /// <param name="content">内容</param>
    /// <returns>实例化的 MessagePrefab</returns>
    public MessagePrefab ShowMessage(string header, string content) {
        if (messagePrefab == null) {
            Debug.LogError("NotificationManager: MessagePrefab 未设置！");
            return null;
        }

        Transform parent = messagePosition != null ? messagePosition.transform : transform;
        MessagePrefab messageInstance = Instantiate(messagePrefab, parent);
        messageInstance.ShowMessage(header, content);
        return messageInstance;
    }

    /// <summary>
    /// 打开玩家信息面板
    /// </summary>
    /// <param name="success">是否成功获取玩家信息</param>
    /// <param name="message">消息内容</param>
    /// <param name="playerInfo">玩家信息响应数据</param>
    public void OpenPlayerInfoPanel(bool success, string message, PlayerInfoResponse playerInfo) {
        if (playerInfoPanelPrefab == null) {
            Debug.LogError("NotificationManager: PlayerInfoPanelPrefab 未设置！");
            return;
        }

        if (success && playerInfo != null) {
            // 在 playerInfoPosition 下创建玩家信息面板
            Transform parent = playerInfoPosition != null ? playerInfoPosition.transform : transform;
            GameObject playerInfoPanelObject = Instantiate(playerInfoPanelPrefab, parent);
            PlayerInfoPanel playerInfoPanel = playerInfoPanelObject.GetComponent<PlayerInfoPanel>();
            if (playerInfoPanel != null) {
                playerInfoPanel.ShowPlayerInfo(playerInfo);
            } else {
                Debug.LogError("NotificationManager: PlayerInfoPanel 组件未找到！");
            }
        } else {
            Debug.LogError($"获取玩家信息失败: {message}");
            ShowTip("错误", false, $"获取玩家信息失败: {message}");
        }
    }
    
    // tips销毁协程
    private IEnumerator DestroyTipAfterDelay(GameObject tipInstance, float delay) {
        yield return new WaitForSeconds(delay);
        if (tipInstance != null) {
            Destroy(tipInstance);
        }
    }

}