using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// 匹配排队中面板（与 <see cref="MatchFoundedPanel"/> 区分）。显示/收起使用 <see cref="PanelPopupTransition"/>，取消匹配时渐出后再发离开队列。
/// <para>本面板仅作为视图：排队计时由常驻的 <see cref="MatchStateManager"/> 负责，
/// 因此在排队中切换到其它窗口再切回来，计时不会中断（通过订阅 <see cref="MatchStateManager.OnElapsedTick"/> 刷新）。</para>
/// </summary>
public class MatchQueueingPanel : MonoBehaviour {
    public static MatchQueueingPanel Instance { get; private set; }

    [SerializeField] private PanelPopupTransition panelPopup;
    [SerializeField] private TMP_Text queueingMatchTypeText;
    [SerializeField] private TMP_Text queueingElapsedText;
    [SerializeField] private Button cancelQueueButton;

    private bool subscribed;
    private MatchStateManager subscribedManager;

    private void Awake() {
        if (Instance != null && Instance != this) {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        cancelQueueButton.onClick.AddListener(OnCancelClick);
        gameObject.SetActive(false);
    }

    private void OnDisable() {
        Unsubscribe();
    }

    /// <summary>开始新的一次排队：重置计时并弹出面板。</summary>
    public void Show(string matchTypeName) {
        MatchStateManager.Instance.StartQueueing(matchTypeName);
        queueingMatchTypeText.text = matchTypeName;
        UpdateElapsedText(0f);
        panelPopup.Show(Subscribe);
    }

    /// <summary>
    /// 返回匹配窗口时若仍在排队，恢复面板显示并接管当前计时（不重置已用时间）。
    /// 由 <see cref="MatchPanel.OnEnable"/> 调用。
    /// </summary>
    public void RestoreIfQueueing() {
        MatchStateManager manager = MatchStateManager.Instance;
        if (manager == null || !manager.IsQueueing || manager.IsMatchFound) return;
        queueingMatchTypeText.text = manager.QueueTitle;
        UpdateElapsedText(manager.ElapsedTime);
        panelPopup.Show(Subscribe);
    }

    public void Hide() {
        Unsubscribe();
        panelPopup?.Hide();
    }

    /// <summary>
    /// 立即隐藏排队面板（无渐出动画）。匹配成功、进入对局等场景应使用此方法。
    /// 仅隐藏视图，不结束排队状态；是否结束排队由 <see cref="MatchStateManager"/> 决定。
    /// </summary>
    public void HideImmediately() {
        Unsubscribe();
        if (panelPopup != null) panelPopup.HideImmediate();
        else gameObject.SetActive(false);
    }

    private void UpdateElapsedText(float elapsedTime) {
        int minutes = Mathf.FloorToInt(elapsedTime / 60f);
        int seconds = Mathf.FloorToInt(elapsedTime % 60f);
        queueingElapsedText.text = $"{minutes:00}:{seconds:00}";
    }

    private void Subscribe() {
        if (subscribed) return;
        MatchStateManager manager = MatchStateManager.Instance;
        if (manager == null) return;
        manager.OnElapsedTick += UpdateElapsedText;
        UpdateElapsedText(manager.ElapsedTime);
        subscribedManager = manager;
        subscribed = true;
    }

    private void Unsubscribe() {
        if (!subscribed) return;
        if (subscribedManager != null) subscribedManager.OnElapsedTick -= UpdateElapsedText;
        subscribedManager = null;
        subscribed = false;
    }

    private void OnCancelClick() {
        Unsubscribe();
        MatchStateManager.Instance?.StopQueueing();
        panelPopup.Hide(() => MatchNetworkManager.Instance?.SendLeaveQueue());
    }
}
