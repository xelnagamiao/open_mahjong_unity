using UnityEngine;

public class NotificationManager : MonoBehaviour {

    [Header("Tips 配置")]
    [SerializeField] private GameObject tipsPosition;
    [SerializeField] private TipItem tipItemPrefab;                // Tips 预制体（挂 TipItem + CanvasGroup）
    [SerializeField] private float defaultTipDuration = 5f;  // Tips 默认显示时长
    private TipStackController _tipStack;

    [Header("PlayerInfo 配置")]
    [SerializeField] private GameObject playerInfoPosition;
    [SerializeField] private GameObject playerInfoPanelPrefab; // 玩家信息面板预制体

    [Header("Message 配置")]
    [SerializeField] private GameObject messagePosition;
    [SerializeField] private MessagePrefab messagePrefab;        // Message 预制体

    public static NotificationManager Instance { get; private set; }

    private void Awake() {
        if (Instance != null && Instance != this) {
            Debug.Log($"Destroying duplicate NotificationManager. Existing: {Instance}, New: {this}");
            Destroy(gameObject);
            return;
        }
        Instance = this;
        if (tipsPosition != null) {
            _tipStack = tipsPosition.GetComponent<TipStackController>();
            if (_tipStack == null) _tipStack = tipsPosition.AddComponent<TipStackController>();
        }
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
        TipItem tipItem = Instantiate(tipItemPrefab, parent.position, parent.rotation, parent);
        tipItem.ShowMessage(type, isSuccess, message);
        float lifeTime = duration > 0f ? duration : defaultTipDuration;
        _tipStack.Push(tipItem);
        tipItem.Init(lifeTime);
    }

    /// <summary>
    /// 显示 Message 弹窗
    /// </summary>
    /// <param name="header">标题</param>
    /// <param name="content">内容</param>
    /// <param name="type">消息类型</param>
    /// <returns>实例化的 MessagePrefab</returns>
    public MessagePrefab ShowMessage(string header, string content, string type = "") {
        if (messagePrefab == null) {
            Debug.LogError("NotificationManager: MessagePrefab 未设置！");
            return null;
        }
        Transform parent = messagePosition != null ? messagePosition.transform : transform;
        MessagePrefab messageInstance = Instantiate(messagePrefab, parent);
        messageInstance.ShowMessage(header, content, type);
        return messageInstance;
    }

    /// <summary>Reusable modal confirmation. Uploaded resources and settings are untouched until confirmed.</summary>
    public MessagePrefab ShowConfirmation(string header, string content, System.Action onConfirm,
        string confirmText = "确定", string cancelText = "取消")
    {
        if (messagePrefab == null) return null;
        Transform parent = messagePosition != null ? messagePosition.transform : transform;
        Canvas canvas = parent.GetComponentInParent<Canvas>();
        if (canvas != null) parent = canvas.transform;
        var modal = new GameObject("ConfirmationModal", typeof(RectTransform), typeof(UnityEngine.UI.Image));
        modal.layer = parent.gameObject.layer;
        var rect = (RectTransform)modal.transform;
        rect.SetParent(parent, false);
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = rect.offsetMax = Vector2.zero;
        var blocker = modal.GetComponent<UnityEngine.UI.Image>();
        blocker.color = new Color(.04f, .06f, .09f, .55f);
        blocker.raycastTarget = true;
        MessagePrefab message = Instantiate(messagePrefab, modal.transform);
        message.SetModalOwner(modal);
        message.ShowConfirmation(header, content, onConfirm, confirmText, cancelText);
        return message;
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
            Transform parent = playerInfoPosition != null ? playerInfoPosition.transform : transform;
            PlayerInfoPanel playerInfoPanel = PlayerInfoPanel.Instance;
            if (playerInfoPanel == null) {
                playerInfoPanel = parent.GetComponentInChildren<PlayerInfoPanel>(true);
            }
            if (playerInfoPanel == null) {
                GameObject playerInfoPanelObject = Instantiate(playerInfoPanelPrefab, parent);
                playerInfoPanelObject.SetActive(false);
                playerInfoPanel = playerInfoPanelObject.GetComponent<PlayerInfoPanel>();
            }
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
}
