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
    private readonly List<GameObject> _spawned = new List<GameObject>();
    private static readonly Color TabActive = new Color(1f, 0.62f, 0.08f, 1f);
    private static readonly Color TabIdle = new Color(0.08f, 0.11f, 0.18f, 1f);
    private static readonly Color TabLabelOnGold = new Color(0.12f, 0.06f, 0.02f, 1f);
    private static readonly Color TabLabelOnDark = new Color(1f, 0.9f, 0.55f, 1f);

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
        ShowLobby();
    }

    private void OnDisable() {
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
        _kind = kind;
        ApplyTabVisual();
        RequestList();
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
        if (detailPanel != null) {
            detailPanel.HideVenueCreate();
            detailPanel.gameObject.SetActive(false);
        }
        SetListChrome(true);
        RequestList();
    }

    public void OnVenueCreateClosed() {
        if (detailPanel != null) {
            detailPanel.gameObject.SetActive(true);
            detailPanel.ShowRoomsAfterCreate();
        }
    }

    public void OpenDetail(string eventId) {
        if (string.IsNullOrEmpty(eventId) || detailPanel == null) return;
        SetListChrome(false);
        detailPanel.gameObject.SetActive(true);
        detailPanel.Open(eventId, _kind);
    }

    private void SetListChrome(bool show) {
        if (listRoot != null) listRoot.SetActive(show);
        Transform side = null;
        if (eventTab != null && eventTab.transform.parent != null && eventTab.transform.parent.name == "EventSideNav") {
            side = eventTab.transform.parent;
        }
        if (side != null) {
            side.gameObject.SetActive(show);
        }
        if (eventTab != null) eventTab.gameObject.SetActive(show);
        if (baseTab != null) baseTab.gameObject.SetActive(show);
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
