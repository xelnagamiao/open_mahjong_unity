using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>自由模式操作记录。布局来自预制体，筛选与阅读位置保留在客户端。</summary>
public sealed class FreeModeActivityPanel : MonoBehaviour {
    public RectTransform dock;
    public GameObject body;
    public Button toggleButton, latestButton;
    public Text title, toggleLabel, latestLabel, emptyLabel, countLabel;
    public Dropdown categoryDropdown;
    public ScrollRect scroll;
    public RectTransform content, rowTemplate;

    private static readonly string[] CategoryNames = {
        "全部操作", "出牌与补花", "摸牌", "副露、收回与转移", "分数变更", "推牌与立牌", "投票", "喊话"
    };
    private sealed class Row {
        public RectTransform root;
        public Text time, message;
        public Image marker;
        public long id;
        public float top;
    }
    private readonly List<Row> rows = new List<Row>();
    private readonly List<FreeModeActivityEntry> visible = new List<FreeModeActivityEntry>();
    private FreeModeActivityLog log;
    private bool wired, expanded, refreshing;
    private long observedId, readId;
    private int unread;
    private float laidOutWidth;
    public bool IsExpanded => expanded;
    public int UnreadCount => unread;
    public event Action StateChanged;

    public void Bind(FreeModeActivityLog activityLog) {
        if (log != null) log.Changed -= OnLogChanged;
        log = activityLog;
        if (!wired) {
            wired = true;
            categoryDropdown.ClearOptions();
            categoryDropdown.AddOptions(new List<string>(CategoryNames));
            categoryDropdown.onValueChanged.AddListener(_ => RefreshRows(true));
            toggleButton.onClick.AddListener(() => SetExpanded(!expanded));
            latestButton.onClick.AddListener(ShowLatest);
            scroll.onValueChanged.AddListener(OnScrollChanged);
        }
        if (log != null) log.Changed += OnLogChanged;
        observedId = NewestId();
        readId = observedId;
        unread = 0;
        SetExpanded(false);
        RefreshRows(true);
    }

    private void OnDestroy() {
        if (log != null) log.Changed -= OnLogChanged;
    }

    private void LateUpdate() {
        if (!wired || !expanded) return;
        float width = scroll.viewport.rect.width;
        if (width > 0f && Mathf.Abs(width - laidOutWidth) > .5f) RefreshRows(IsAtLatest());
    }

    private void OnRectTransformDimensionsChange() {
        if (dock == null) return;
        Rect area = ((RectTransform)transform).rect;
        if (area.width <= 0f || area.height <= 0f) return;
        float scale = Mathf.Min(1f, Mathf.Max(1f, area.width - 236f) / 360f,
            Mathf.Max(120f, area.height - 350f) / 300f);
        dock.localScale = new Vector3(scale, scale, 1f);
    }

    private long NewestId() {
        return log != null && log.Entries.Count > 0 ? log.Entries[log.Entries.Count - 1].Id : 0;
    }

    private bool Matches(FreeModeActivityEntry entry) {
        return categoryDropdown.value == 0 || (int)entry.Category == categoryDropdown.value;
    }

    private void OnLogChanged() {
        bool follow = expanded && IsAtLatest();
        long newest = NewestId();
        // A room reset may replace the log contents; old unread markers must not survive it.
        if (newest < observedId || (log != null && log.Entries.Count == 0)) readId = 0;
        observedId = newest;
        RefreshRows(follow);
    }

    private bool IsAtLatest() {
        if (content == null || scroll == null || scroll.viewport == null) return true;
        float maximum = Mathf.Max(0f, content.rect.height - scroll.viewport.rect.height);
        return maximum <= 1f || content.anchoredPosition.y >= maximum - 12f;
    }

    public void SetExpanded(bool open) {
        expanded = open;
        dock.gameObject.SetActive(open);
        body.SetActive(open);
        dock.sizeDelta = new Vector2(360f, 300f);
        toggleLabel.text = "收起";
        if (!open) categoryDropdown.Hide();
        OnRectTransformDimensionsChange();
        if (open) RefreshRows(true);
        else RefreshStatus();
    }

    private void RefreshRows(bool followLatest) {
        if (!wired) return;
        refreshing = true;
        try {
            // Remember the first visible row, including its offset, so trimming the bounded log
            // does not jump the reader forward when new operations arrive.
            long anchorId = 0;
            float anchorOffset = 0f;
            float previousY = content.anchoredPosition.y;
            foreach (Row row in rows) {
                if (row.root.gameObject.activeSelf && row.top + row.root.rect.height >= previousY) {
                    anchorId = row.id;
                    anchorOffset = previousY - row.top;
                    break;
                }
            }
            visible.Clear();
            if (log != null)
                foreach (FreeModeActivityEntry entry in log.Entries)
                    if (Matches(entry)) visible.Add(entry);
            laidOutWidth = Mathf.Max(1f, scroll.viewport.rect.width);
            float y = 0f;
            float restoredY = previousY;
            for (int i = 0; i < visible.Count; i++) {
                Row row = i < rows.Count ? rows[i] : CreateRow();
                FreeModeActivityEntry entry = visible[i];
                row.id = entry.Id;
                row.top = y;
                row.root.gameObject.SetActive(true);
                row.root.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, laidOutWidth);
                row.time.text = entry.ReceivedAt.ToLocalTime().ToString("HH:mm:ss");
                row.message.text = entry.Message ?? "";
                row.message.supportRichText = false;
                row.message.color = entry.Category == FreeModeActivityCategory.Scores
                    ? new Color(.98f, .85f, .53f) : new Color(.87f, .91f, .95f);
                row.marker.color = entry.Category == FreeModeActivityCategory.Scores
                    ? new Color(.92f, .68f, .27f) : new Color(.31f, .66f, .68f, .7f);
                float height = Mathf.Max(32f, row.message.preferredHeight + 6f);
                row.root.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, height);
                row.root.anchoredPosition = new Vector2(0f, -y);
                if (entry.Id == anchorId) restoredY = y + anchorOffset;
                y += height + 6f;
            }
            for (int i = visible.Count; i < rows.Count; i++) rows[i].root.gameObject.SetActive(false);
            content.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, Mathf.Max(0f, y - 6f));
            float maximum = Mathf.Max(0f, content.rect.height - scroll.viewport.rect.height);
            scroll.StopMovement();
            content.anchoredPosition = new Vector2(0f, followLatest ? maximum : Mathf.Clamp(restoredY, 0f, maximum));
            if (followLatest && expanded) readId = NewestId();
            emptyLabel.gameObject.SetActive(visible.Count == 0);
            emptyLabel.text = log == null || log.Entries.Count == 0
                ? "还没有操作记录\n收到操作后会显示在这里" : "此分类暂无操作\n可切换为「全部操作」";
            RefreshStatus();
        } finally { refreshing = false; }
    }

    private Row CreateRow() {
        RectTransform root = Instantiate(rowTemplate, content, false);
        root.name = "Operation";
        var row = new Row {
            root = root,
            time = root.Find("Time").GetComponent<Text>(),
            message = root.Find("Message").GetComponent<Text>(),
            marker = root.Find("Marker").GetComponent<Image>()
        };
        rows.Add(row);
        return row;
    }

    private void OnScrollChanged(Vector2 _) {
        if (refreshing || !expanded || !IsAtLatest()) return;
        readId = NewestId();
        RefreshStatus();
    }

    private void ShowLatest() {
        RefreshRows(true);
    }

    private void RefreshStatus() {
        unread = 0;
        int matchingUnread = 0;
        if (log != null) {
            foreach (FreeModeActivityEntry entry in log.Entries) {
                if (entry.Id <= readId) continue;
                unread++;
                if (Matches(entry)) matchingUnread++;
            }
        }
        title.text = !expanded && unread > 0 ? "记录 · " + unread + " 条新" : "记录";
        latestButton.gameObject.SetActive(expanded && matchingUnread > 0);
        latestLabel.text = matchingUnread + " 条新消息  ↓";
        countLabel.gameObject.SetActive(matchingUnread == 0);
        countLabel.text = "最近 " + visible.Count + " 条 · 全部玩家";
        StateChanged?.Invoke();
    }
}
