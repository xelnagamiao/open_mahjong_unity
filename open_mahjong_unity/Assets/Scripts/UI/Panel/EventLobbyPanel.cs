using System.Collections;
using System.Collections.Generic;
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
    [SerializeField] private Transform listContent;
    [SerializeField] private GameObject itemTemplate;
    [SerializeField] private GameObject listRoot;
    [SerializeField] private EventDetailPanel detailPanel;
    [SerializeField] private TMP_Text emptyHint;

    private string _kind = "event";
    private Coroutine _fadeRoutine;
    private GameObject _listGhost;
    private readonly List<GameObject> _spawned = new List<GameObject>();
    private static readonly Color TabActive = new Color(1f, 0.62f, 0.08f, 1f);
    private static readonly Color TabIdle = new Color(0.08f, 0.11f, 0.18f, 1f);
    private static readonly Color TabLabelOnGold = new Color(0.12f, 0.06f, 0.02f, 1f);
    private static readonly Color TabLabelOnDark = new Color(1f, 0.9f, 0.55f, 1f);

    public string CurrentKind => _kind;

    private GameObject SideNav {
        get {
            if (eventTab != null && eventTab.transform.parent != null
                && eventTab.transform.parent.name == "EventSideNav") {
                return eventTab.transform.parent.gameObject;
            }
            return null;
        }
    }

    private void Awake() {
        if (Instance != null && Instance != this) {
            Destroy(this);
            return;
        }
        Instance = this;
        if (detailPanel != null) detailPanel.gameObject.SetActive(false);
        WireTabs();
    }

    private void OnEnable() {
        if (EventNetworkManager.Instance != null) {
            EventNetworkManager.Instance.OnPublicEventsUpdated += RefreshList;
        }
        ShowLobby(true);
    }

    private void OnDisable() {
        StopFade();
        if (detailPanel != null) detailPanel.HideVenueCreate();
        if (EventNetworkManager.Instance != null) {
            EventNetworkManager.Instance.OnPublicEventsUpdated -= RefreshList;
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
        bool same = kind == _kind;
        _kind = kind;
        ApplyTabVisual();
        if (same) {
            RequestList();
            return;
        }
        if (listRoot == null || !listRoot.activeSelf) {
            RequestList();
            return;
        }
        StopFade();
        _fadeRoutine = StartCoroutine(SwitchKindRoutine());
    }

    private IEnumerator SwitchKindRoutine() {
        _listGhost = Instantiate(listRoot, listRoot.transform.parent);
        _listGhost.name = "EventListFadeGhost";
        _listGhost.transform.SetSiblingIndex(listRoot.transform.GetSiblingIndex());
        CanvasGroup ghostCg = WindowFadeTransition.GetOrAddCanvasGroup(_listGhost);
        ghostCg.interactable = false;
        ghostCg.blocksRaycasts = false;
        ClearListImmediate();
        listRoot.SetActive(false);
        yield return WindowFadeTransition.CrossFade(_listGhost, listRoot, WindowFadeTransition.DurationSeconds);
        DestroyListGhost();
        RequestList();
        _fadeRoutine = null;
    }

    private void ClearListImmediate() {
        foreach (var go in _spawned) {
            if (go != null) Destroy(go);
        }
        _spawned.Clear();
        HideEditorExamples();
        if (emptyHint != null) {
            emptyHint.gameObject.SetActive(true);
            emptyHint.text = _kind == "base" ? "暂无基地" : "暂无赛事";
        }
    }

    private void DestroyListGhost() {
        if (_listGhost == null) return;
        Destroy(_listGhost);
        _listGhost = null;
    }

    private void ApplyTabVisual() {
        if (eventTabImage != null) eventTabImage.color = _kind == "event" ? TabActive : TabIdle;
        if (baseTabImage != null) baseTabImage.color = _kind == "base" ? TabActive : TabIdle;
        SetTabLabel(eventTab, _kind == "event");
        SetTabLabel(baseTab, _kind == "base");
    }

    private static void SetTabLabel(Button tab, bool active) {
        if (tab == null) return;
        TMP_Text label = tab.GetComponentInChildren<TMP_Text>(true);
        if (label == null) return;
        label.color = active ? TabLabelOnGold : TabLabelOnDark;
    }

    public void ShowLobby() {
        ShowLobby(false);
    }

    private void ShowLobby(bool instant) {
        if (detailPanel != null) detailPanel.HideVenueCreate();
        if (instant) {
            StopFade();
            if (detailPanel != null) detailPanel.gameObject.SetActive(false);
            SetListChrome(true);
            RequestList();
            return;
        }
        StopFade();
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
            if (!detailPanel.gameObject.activeSelf) detailPanel.gameObject.SetActive(true);
            detailPanel.ShowRoomsAfterCreate();
        }
    }

    public void OpenDetail(string eventId) {
        if (string.IsNullOrEmpty(eventId) || detailPanel == null) return;
        StopFade();
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

    private GameObject[] ListChromeRoots() {
        var roots = new List<GameObject>();
        if (listRoot != null) roots.Add(listRoot);
        GameObject side = SideNav;
        if (side != null) {
            roots.Add(side);
        } else {
            if (eventTab != null) roots.Add(eventTab.gameObject);
            if (baseTab != null) roots.Add(baseTab.gameObject);
        }
        return roots.ToArray();
    }

    private void SetListChrome(bool show) {
        if (listRoot != null) listRoot.SetActive(show);
        WindowFadeTransition.Normalize(listRoot);
        GameObject side = SideNav;
        if (side != null) {
            side.SetActive(show);
            WindowFadeTransition.Normalize(side);
        }
        if (eventTab != null) eventTab.gameObject.SetActive(show);
        if (baseTab != null) baseTab.gameObject.SetActive(show);
    }

    private void StopFade() {
        if (_fadeRoutine != null) {
            StopCoroutine(_fadeRoutine);
            _fadeRoutine = null;
        }
        DestroyListGhost();
        WindowFadeTransition.Normalize(listRoot);
        WindowFadeTransition.Normalize(SideNav);
        if (detailPanel != null) WindowFadeTransition.Normalize(detailPanel.gameObject);
    }

    private void RequestList() {
        EventNetworkManager.Instance?.ListPublicEvents(_kind);
    }

    private void RefreshList() {
        foreach (var go in _spawned) {
            if (go != null) Destroy(go);
        }
        _spawned.Clear();
        if (listContent == null || itemTemplate == null) return;

        HideEditorExamples();

        var list = EventNetworkManager.Instance != null ? EventNetworkManager.Instance.PublicEvents : null;
        int count = 0;
        if (list != null) {
            foreach (var entry in list) {
                if (entry == null) continue;
                string entryKind = entry.kind == "base" ? "base" : "event";
                if (entryKind != _kind) continue;
                var item = Instantiate(itemTemplate, listContent);
                item.SetActive(true);
                var binder = item.GetComponent<EventListItem>() ?? item.GetComponentInChildren<EventListItem>(true);
                if (binder != null) binder.Bind(entry, OpenDetail);
                _spawned.Add(item);
                count++;
            }
        }
        if (emptyHint != null) {
            emptyHint.gameObject.SetActive(count == 0);
            emptyHint.text = _kind == "base" ? "暂无基地" : "暂无赛事";
        }
    }

    private void HideEditorExamples() {
        if (listContent == null) return;
        for (int i = 0; i < listContent.childCount; i++) {
            Transform child = listContent.GetChild(i);
            if (emptyHint != null && child.gameObject == emptyHint.gameObject) continue;
            if (_spawned.Contains(child.gameObject)) continue;
            child.gameObject.SetActive(false);
        }
    }
}
