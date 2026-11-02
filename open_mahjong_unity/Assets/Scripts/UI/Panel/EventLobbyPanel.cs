using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// 赛事/基地阅览层：绑定场景里的比赛/基地标签与列表，点条目打开覆盖详情。
/// </summary>
public class EventLobbyPanel : MonoBehaviour {
    public static EventLobbyPanel Instance { get; private set; }

    [SerializeField] private Button eventTab;
    [SerializeField] private Button baseTab;
    [SerializeField] private Image eventTabImage;
    [SerializeField] private Image baseTabImage;
    [SerializeField] private GameObject listRoot;
    [SerializeField] private EventDetailPanel detailPanel;

    [Header("侧栏颜色")]
    [InspectorName("选中底色")]
    [SerializeField] private Color tabActiveColor = new Color(1f, 0.62f, 0.08f, 1f);
    [InspectorName("未选底色")]
    [SerializeField] private Color tabIdleColor = new Color(0.08f, 0.11f, 0.18f, 1f);
    [InspectorName("选中文字")]
    [SerializeField] private Color tabActiveLabelColor = new Color(0.12f, 0.06f, 0.02f, 1f);
    [InspectorName("未选文字")]
    [SerializeField] private Color tabIdleLabelColor = new Color(1f, 0.9f, 0.55f, 1f);

    [SerializeField] private Salasasa.Venues.VenueLobbyPresentation presentation;
    [SerializeField] private string initialKind = "event";
    private string _kind = "event";
    private Coroutine _fadeRoutine;
    private bool _detailOpen;

    public string CurrentKind => _kind;

    private void Awake() {
        if (Instance != null && Instance != this) {
            Destroy(this);
            return;
        }
        Instance = this;
        _kind = initialKind == "base" ? "base" : "event";
        if (detailPanel != null) detailPanel.gameObject.SetActive(false);
        WireTabs();
    }

    private void OnEnable() {
        if (EventNetworkManager.Instance != null) {
            EventNetworkManager.Instance.OnPublicEventsUpdated += RefreshList;
            EventNetworkManager.Instance.OnOperationCompleted += OnListOperation;
        }
        if (ShouldRestoreDetail()) {
            RestoreDetailChrome();
            detailPanel.RestoreVisiblePage();
        } else {
            ShowLobby(true);
        }
    }

    private void OnDisable() {
        StopFadeRoutine();
        if (detailPanel != null) {
            detailPanel.HideVenueCreate();
            detailPanel.gameObject.SetActive(false);
        }
        if (EventNetworkManager.Instance != null) {
            EventNetworkManager.Instance.OnPublicEventsUpdated -= RefreshList;
            EventNetworkManager.Instance.OnOperationCompleted -= OnListOperation;
        }
    }

    private void WireTabs() {
        if (eventTab != null) {
            eventTab.onClick.RemoveAllListeners();
            eventTab.onClick.AddListener(() => SwitchKind("event"));
        }
        if (baseTab != null) {
            baseTab.onClick.RemoveAllListeners();
            baseTab.onClick.AddListener(() => SwitchKind("base"));
        }
        ApplyTabVisual();
    }

    private void SwitchKind(string kind) {
        string next = kind == "base" ? "base" : "event";
        _kind = next;
        ApplyTabVisual();
        AbortToList();
        RefreshList();
        RequestList();
    }

    private void ApplyTabVisual() {
        if (eventTabImage != null) eventTabImage.color = _kind == "event" ? tabActiveColor : tabIdleColor;
        if (baseTabImage != null) baseTabImage.color = _kind == "base" ? tabActiveColor : tabIdleColor;
        SetTabLabel(eventTab, _kind == "event");
        SetTabLabel(baseTab, _kind == "base");
    }

    private void SetTabLabel(Button tab, bool active) {
        if (tab == null) return;
        TMP_Text label = tab.GetComponentInChildren<TMP_Text>(true);
        if (label == null) return;
        label.color = active ? tabActiveLabelColor : tabIdleLabelColor;
    }

#if UNITY_EDITOR
    private void OnValidate() {
        ApplyTabVisual();
    }
#endif

    public void ShowLobby() {
        ShowLobby(false);
    }

    /// <summary>从对局/牌谱回到赛事页：详情仍开着则保持子页，否则刷新列表。</summary>
    public void OnReturnedFromGame() {
        if (!isActiveAndEnabled) return;
        if (ShouldRestoreDetail()) {
            RestoreDetailChrome();
            detailPanel.RestoreVisiblePage();
            return;
        }
        RefreshList();
        RequestList();
    }

    private bool ShouldRestoreDetail() {
        return detailPanel != null
            && _detailOpen
            && detailPanel.HasOpenVenue;
    }

    public void EnsureVisiblePage() {
        if (!isActiveAndEnabled) return;
        // A reverse navigation during the fade can keep this owner enabled while its detached page was closed.
        if (ShouldRestoreDetail()) {
            if (!detailPanel.gameObject.activeSelf) {
                RestoreDetailChrome();
                detailPanel.RestoreVisiblePage();
            }
        } else if (listRoot != null && !listRoot.activeSelf) {
            SetListChrome(true);
        }
    }

    private void RestoreDetailChrome() {
        StopFadeRoutine();
        SetListChrome(false);
        GameObject detailGo = detailPanel.gameObject;
        detailGo.transform.SetAsLastSibling();
        if (!detailGo.activeSelf) detailGo.SetActive(true);
        WindowFadeTransition.Normalize(detailGo);
        GameObject[] chrome = ListChromeRoots();
        for (int i = 0; i < chrome.Length; i++) {
            WindowFadeTransition.Normalize(chrome[i]);
        }
    }

    private void ShowLobby(bool instant) {
        _detailOpen = false;
        if (detailPanel != null) detailPanel.HideVenueCreate();
        if (instant) {
            AbortToList();
            RefreshList();
            RequestList();
            return;
        }
        StopFadeRoutine();
        GameObject detailGo = detailPanel != null ? detailPanel.gameObject : null;
        if (detailGo == null || !detailGo.activeSelf) {
            AbortToList();
            RefreshList();
            RequestList();
            return;
        }
        _fadeRoutine = StartCoroutine(ShowLobbyRoutine());
    }

    private IEnumerator ShowLobbyRoutine() {
        GameObject detailGo = detailPanel != null ? detailPanel.gameObject : null;
        if (detailGo == null || !detailGo.activeSelf) {
            SetListChrome(true);
            RequestList();
            _fadeRoutine = null;
            yield break;
        }
        yield return WindowFadeTransition.CrossFade(
            new[] { detailGo },
            ListChromeRoots(),
            WindowFadeTransition.DurationSeconds);
        SetListChrome(true);
        RequestList();
        _fadeRoutine = null;
    }

    public void OnVenueCreateClosed() {
        if (detailPanel != null) {
            _detailOpen = true;
            detailPanel.transform.SetAsLastSibling();
            if (!detailPanel.gameObject.activeSelf) detailPanel.gameObject.SetActive(true);
            detailPanel.ShowRoomsAfterCreate();
        }
    }

    public void OpenDetail(string eventId) {
        if (string.IsNullOrEmpty(eventId) || detailPanel == null) return;
        StopFadeRoutine();
        _detailOpen = true;
        detailPanel.transform.SetAsLastSibling();
        _fadeRoutine = StartCoroutine(OpenDetailRoutine(eventId));
    }

    private IEnumerator OpenDetailRoutine(string eventId) {
        yield return WindowFadeTransition.CrossFade(
            ListChromeRoots(),
            new[] { detailPanel.gameObject },
            WindowFadeTransition.DurationSeconds,
            () => detailPanel.Open(eventId, _kind));
        SetListChrome(false);
        _fadeRoutine = null;
    }

    private GameObject[] ListChromeRoots() => new[] { listRoot };

    private void SetListChrome(bool show) {
        listRoot.SetActive(show);
        WindowFadeTransition.Normalize(listRoot);
    }

    private void AbortToList() {
        _detailOpen = false;
        StopFadeRoutine();
        if (detailPanel != null) detailPanel.HideVenueCreate();
        GameObject detailGo = detailPanel != null ? detailPanel.gameObject : null;
        WindowFadeTransition.Snap(detailGo, ListChromeRoots());
    }

    private void StopFadeRoutine() {
        if (_fadeRoutine != null) {
            StopCoroutine(_fadeRoutine);
            _fadeRoutine = null;
        }
    }

    private void OnListOperation(string operation, string id, bool success, string message) {
        if (operation == "event/list_public" && !success && presentation != null) presentation.SetListError(message);
    }

    private void RequestList() {
        if (presentation != null) presentation.BeginLoading();
        EventNetworkManager.Instance?.ListPublicEvents(_kind);
    }

    private void RefreshList() {
        presentation.RefreshFromHost(EventNetworkManager.Instance != null ? EventNetworkManager.Instance.PublicEvents : null, _kind);
    }
}
