using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class NoticePanel : MonoBehaviour {
    public static NoticePanel Instance { get; private set; }
    [SerializeField] private Transform listContent;
    [SerializeField] private GameObject itemTemplate;
    [SerializeField] private ActivityDetailPanel detailPanel;
    [SerializeField] private NoticePanelView presentation;
    private readonly List<GameObject> _spawned = new List<GameObject>();
    private readonly List<Texture2D> _covers = new List<Texture2D>();
    private readonly List<string> _visibleIds = new List<string>();
    private readonly List<ActivityIndexItem> _items = new List<ActivityIndexItem>();
    private readonly List<ActivityIndexItem> _filtered = new List<ActivityIndexItem>();
    private Coroutine _loadRoutine;
    private string _selectedId;
    private int _filter, _page;
    private bool _indexReady;

    private void Awake() { Instance = this; if (detailPanel != null) detailPanel.gameObject.SetActive(true); HideEditorExamples(); }
    private void OnEnable() { Reload(); }
    private void OnDisable() { StopAllCoroutines(); _loadRoutine = null; ClearSpawned(); }
    private void OnDestroy() { if (Instance == this) Instance = null; }
    public void Reload() {
        StopAllCoroutines(); ClearSpawned(); _indexReady = false;
        if (detailPanel != null) detailPanel.ShowEmpty();
        if (presentation != null) presentation.ShowState(false);
        _loadRoutine = StartCoroutine(LoadIndex());
    }
    public void Close() {
        var lobby = FindAnyObjectByType<Salasasa.Home.CompleteLobbyController>();
        if (lobby != null) lobby.OpenHome();
        else if (WindowsManager.Instance != null) WindowsManager.Instance.SwitchWindow("menu");
    }
    public void OpenDetail(string id) {
        if (string.IsNullOrEmpty(id) || !_indexReady) return;
        SelectId(id);
        if (_loadRoutine != null) StopCoroutine(_loadRoutine);
        if (detailPanel != null) detailPanel.ShowPending("正在读取通知");
        _loadRoutine = StartCoroutine(LoadAndOpen(id));
    }
    public void SetFilter(int filter) {
        if (!_indexReady || presentation == null) return;
        if (_loadRoutine != null) StopCoroutine(_loadRoutine);
        _filter = Mathf.Clamp(filter, 0, 2); _page = 0; Refilter();
        string id = PickOpenId(); SelectId(id);
        if (id != null) _page = _filtered.FindIndex(entry => entry.id == id) / presentation.PageSize;
        if (detailPanel != null) detailPanel.ShowEmpty();
        RenderList();
        if (id != null) OpenDetail(id);
    }
    public void ChangePage(int direction) {
        if (!_indexReady || presentation == null || _filtered.Count == 0) return;
        _page = Mathf.Clamp(_page + direction, 0, (_filtered.Count - 1) / presentation.PageSize);
        string id = _filtered[_page * presentation.PageSize].id;
        SelectId(id); RenderList(); presentation.listScroll.verticalNormalizedPosition = 1; OpenDetail(id);
    }
    public void ChangeDocument(int direction) {
        if (!_indexReady || presentation == null || _filtered.Count == 0) return;
        int current = _filtered.FindIndex(entry => entry.id == _selectedId);
        int next = (Mathf.Max(0, current) + direction + _filtered.Count) % _filtered.Count;
        int page = next / presentation.PageSize; bool changedPage = page != _page; _page = page;
        SelectId(_filtered[next].id); RenderList();
        if (changedPage) presentation.listScroll.verticalNormalizedPosition = 1;
        OpenDetail(_filtered[next].id);
    }
    private IEnumerator LoadIndex() {
        HideEditorExamples(); ActivityIndexFile index = null; string error = null;
        yield return ActivityHttp.GetIndex(data => index = data, message => error = message);
        _items.Clear(); _visibleIds.Clear();
        if (index == null) {
            if (presentation != null) presentation.ShowState(true);
            else if (NotificationManager.Instance != null) NotificationManager.Instance.ShowTip("活动", false, error ?? "活动加载失败");
            yield break;
        }
        if (index.items != null) foreach (var entry in index.items) {
            if (entry == null || string.IsNullOrEmpty(entry.id) || _visibleIds.Contains(entry.id)) continue;
            if (!ActivityStatus.IsClientVisible(entry.status, entry.ended)) continue;
            _items.Add(entry); _visibleIds.Add(entry.id);
        }
        UnreadBadgeStore.ReplaceNoticeIds(_visibleIds); _indexReady = true; Refilter();
        string id = PickOpenId(); SelectId(id);
        _page = id == null || presentation == null ? 0 : _filtered.FindIndex(entry => entry.id == id) / presentation.PageSize;
        if (detailPanel != null) detailPanel.ShowEmpty();
        RenderList();
        if (string.IsNullOrEmpty(id)) yield break;
        if (detailPanel != null) detailPanel.ShowPending("正在读取通知");
        yield return LoadAndOpen(id);
    }
    private void Refilter() {
        _filtered.Clear();
        foreach (var item in _items) {
            if (presentation != null && _filter == 1 && !UnreadBadgeStore.IsNoticeUnseen(item.id)) continue;
            if (presentation != null && _filter == 2 && !ActivityStatus.IsEnded(item.status, item.ended)) continue;
            _filtered.Add(item);
        }
    }
    private string PickOpenId() {
        if (!string.IsNullOrEmpty(_selectedId) && _filtered.Exists(item => item.id == _selectedId)) return _selectedId;
        return _filtered.Count > 0 ? _filtered[0].id : null;
    }
    private void RenderList() {
        if (presentation != null) { presentation.Render(_filtered, _selectedId, _page, _filter); return; }
        ClearSpawned(); foreach (var entry in _filtered) SpawnItem(entry); SelectId(_selectedId);
    }
    private IEnumerator LoadAndOpen(string id) {
        ActivityDetail detail = null; string error = null;
        yield return ActivityHttp.GetJson<ActivityDetail>(ActivityHttp.WithCacheBust(ActivityHttp.MetaPath(id)), data => detail = data, message => error = message);
        if (detail == null || !ActivityStatus.IsClientVisible(detail.status, detail.ended)) {
            if (presentation != null && detailPanel != null) detailPanel.ShowPending(detail == null ? "活动加载失败" : "活动不可用");
            if (NotificationManager.Instance != null) NotificationManager.Instance.ShowTip("活动", false, detail == null ? error ?? "活动加载失败" : "活动不可用");
            yield break;
        }
        SelectId(id); UnreadBadgeStore.MarkNoticeSeen(id);
        var index = _items.Find(item => item.id == id);
        if (index != null) {
            if (string.IsNullOrEmpty(detail.updated_at)) detail.updated_at = index.updated_at;
            if (string.IsNullOrEmpty(detail.description)) detail.description = index.description;
        }
        if (detailPanel != null) { detailPanel.gameObject.SetActive(true); detailPanel.Open(detail); }
    }
    private void SelectId(string id) {
        _selectedId = id; if (presentation != null) presentation.Select(id);
        foreach (var go in _spawned) {
            if (go == null) continue;
            var binder = go.GetComponent<ActivityItem>() ?? go.GetComponentInChildren<ActivityItem>(true);
            if (binder != null) binder.SetSelected(binder.ActivityId == id);
        }
    }
    private void SpawnItem(ActivityIndexItem entry) {
        if (listContent == null || itemTemplate == null) { Debug.LogError("NoticePanel 列表未绑定"); return; }
        var go = Instantiate(itemTemplate, listContent); go.name = "ActivityItem_" + entry.id; go.SetActive(true);
        var binder = go.GetComponent<ActivityItem>() ?? go.GetComponentInChildren<ActivityItem>(true);
        if (binder != null) {
            binder.Bind(entry, OpenDetail); binder.SetCover(null);
            if (!string.IsNullOrEmpty(entry.cover_url)) StartCoroutine(LoadCover(binder, entry.cover_url));
        }
        _spawned.Add(go);
    }
    private IEnumerator LoadCover(ActivityItem item, string url) {
        Texture2D texture = null; yield return ActivityHttp.GetTexture(url, value => texture = value, _ => { });
        if (item == null || texture == null) { if (texture != null) Destroy(texture); yield break; }
        _covers.Add(texture); item.SetCover(texture);
    }
    private void ClearSpawned() {
        foreach (var item in _spawned) if (item != null) { item.SetActive(false); Destroy(item); }
        _spawned.Clear(); ActivityHttp.DestroyTextures(_covers);
    }
    private void HideEditorExamples() {
        if (presentation != null || listContent == null) return;
        if (itemTemplate != null && itemTemplate.scene.IsValid()) itemTemplate.SetActive(false);
        for (int i = 0; i < listContent.childCount; i++) {
            var child = listContent.GetChild(i); if (!_spawned.Contains(child.gameObject)) child.gameObject.SetActive(false);
        }
    }
}
