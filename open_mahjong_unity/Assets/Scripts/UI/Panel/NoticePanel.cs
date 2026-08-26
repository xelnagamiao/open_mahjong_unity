using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 顶栏「通知」：侧栏活动标签 + 常亮详情。打开时显示上次选中的活动，没有则显示第一条。
/// </summary>
public class NoticePanel : MonoBehaviour {
    public static NoticePanel Instance { get; private set; }

    private const string PrefSelectedId = "notice.selected_activity_id";

    [SerializeField] private Transform listContent;
    [SerializeField] private GameObject itemTemplate;
    [SerializeField] private ActivityDetailPanel detailPanel;

    private readonly List<GameObject> _spawned = new List<GameObject>();
    private readonly List<Texture2D> _covers = new List<Texture2D>();
    private readonly List<string> _visibleIds = new List<string>();
    private Coroutine _loadRoutine;
    private string _selectedId;

    private void Awake() {
        Instance = this;
        if (detailPanel != null) detailPanel.gameObject.SetActive(true);
        HideEditorExamples();
    }

    private void OnEnable() {
        Reload();
    }

    private void OnDisable() {
        if (_loadRoutine != null) {
            StopCoroutine(_loadRoutine);
            _loadRoutine = null;
        }
        ClearSpawned();
    }

    public void Reload() {
        if (_loadRoutine != null) StopCoroutine(_loadRoutine);
        _loadRoutine = StartCoroutine(LoadIndex());
    }

    public void OpenDetail(string activityId) {
        if (string.IsNullOrEmpty(activityId)) return;
        SelectId(activityId);
        if (_loadRoutine != null) StopCoroutine(_loadRoutine);
        _loadRoutine = StartCoroutine(LoadAndOpen(activityId));
    }

    private IEnumerator LoadIndex() {
        HideEditorExamples();
        ActivityIndexFile index = null;
        yield return ActivityHttp.GetIndex(data => index = data, _ => { });
        ClearSpawned();
        HideEditorExamples();
        _visibleIds.Clear();

        if (index != null && index.items != null) {
            foreach (ActivityIndexItem entry in index.items) {
                if (entry == null || string.IsNullOrEmpty(entry.id)) continue;
                if (!ActivityStatus.IsClientVisible(entry.status, entry.ended)) continue;
                SpawnItem(entry);
                _visibleIds.Add(entry.id);
            }
        }

        string preferred = PlayerPrefs.GetString(PrefSelectedId, _selectedId ?? "");
        string openId = null;
        if (!string.IsNullOrEmpty(preferred) && _visibleIds.Contains(preferred)) {
            openId = preferred;
        } else if (_visibleIds.Count > 0) {
            openId = _visibleIds[0];
        }

        if (string.IsNullOrEmpty(openId)) {
            if (detailPanel != null) detailPanel.ShowEmpty();
            yield break;
        }
        yield return LoadAndOpen(openId);
    }

    private IEnumerator LoadAndOpen(string activityId) {
        ActivityDetail detail = null;
        string error = null;
        yield return ActivityHttp.GetJson<ActivityDetail>(
            ActivityHttp.WithCacheBust(ActivityHttp.MetaPath(activityId)),
            data => detail = data,
            err => error = err
        );
        if (detail == null) {
            if (NotificationManager.Instance != null) {
                NotificationManager.Instance.ShowTip("活动", false, error ?? "活动加载失败");
            }
            yield break;
        }
        if (!ActivityStatus.IsClientVisible(detail.status, detail.ended)) {
            if (NotificationManager.Instance != null) {
                NotificationManager.Instance.ShowTip("活动", false, "活动不可用");
            }
            yield break;
        }
        SelectId(activityId);
        if (detailPanel != null) {
            detailPanel.gameObject.SetActive(true);
            detailPanel.Open(detail);
        }
    }

    private void SelectId(string activityId) {
        _selectedId = activityId;
        PlayerPrefs.SetString(PrefSelectedId, activityId ?? "");
        PlayerPrefs.Save();
        for (int i = 0; i < _spawned.Count; i++) {
            if (_spawned[i] == null) continue;
            ActivityItem binder = _spawned[i].GetComponent<ActivityItem>()
                ?? _spawned[i].GetComponentInChildren<ActivityItem>(true);
            if (binder != null) binder.SetSelected(binder.ActivityId == activityId);
        }
    }

    private void SpawnItem(ActivityIndexItem entry) {
        if (listContent == null) {
            Debug.LogError("NoticePanel.listContent 未绑定");
            return;
        }
        if (itemTemplate == null) {
            Debug.LogError("NoticePanel.itemTemplate 未绑定");
            return;
        }
        GameObject go = Instantiate(itemTemplate, listContent);
        go.name = "ActivityItem_" + entry.id;
        go.SetActive(true);
        ActivityItem binder = go.GetComponent<ActivityItem>() ?? go.GetComponentInChildren<ActivityItem>(true);
        if (binder != null) {
            binder.Bind(entry, OpenDetail);
            binder.SetCover(null);
            if (!string.IsNullOrEmpty(entry.cover_url)) {
                StartCoroutine(LoadCover(binder, entry.cover_url));
            }
        }
        _spawned.Add(go);
    }

    private IEnumerator LoadCover(ActivityItem item, string url) {
        Texture2D texture = null;
        yield return ActivityHttp.GetTexture(url, tex => texture = tex, _ => { });
        if (item == null || texture == null) {
            if (texture != null) Destroy(texture);
            yield break;
        }
        _covers.Add(texture);
        item.SetCover(texture);
    }

    private void ClearSpawned() {
        for (int i = 0; i < _spawned.Count; i++) {
            if (_spawned[i] != null) Destroy(_spawned[i]);
        }
        _spawned.Clear();
        ActivityHttp.DestroyTextures(_covers);
    }

    private void HideEditorExamples() {
        if (listContent == null) return;
        if (itemTemplate != null && itemTemplate.scene.IsValid()) {
            itemTemplate.SetActive(false);
        }
        for (int i = 0; i < listContent.childCount; i++) {
            Transform child = listContent.GetChild(i);
            if (_spawned.Contains(child.gameObject)) continue;
            child.gameObject.SetActive(false);
        }
    }
}
