using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// 顶栏「通知」：标准 Scroll View + 活动预制体。
/// 场景与预制体由菜单 Tools/Notice/重建通知活动面板 生成，运行时只填数据。
/// </summary>
public class NoticePanel : MonoBehaviour {
    public static NoticePanel Instance { get; private set; }

    [SerializeField] private Transform listContent;
    [SerializeField] private GameObject itemTemplate;
    [SerializeField] private GameObject listRoot;
    [SerializeField] private ActivityDetailPanel detailPanel;
    [SerializeField] private TMP_Text emptyHint;
    [SerializeField] private TMP_Text headerTitle;

    private readonly List<GameObject> _spawned = new List<GameObject>();
    private readonly List<Texture2D> _covers = new List<Texture2D>();
    private readonly List<GameObject> _examples = new List<GameObject>();
    private Coroutine _loadRoutine;

    private void Awake() {
        Instance = this;
        ResolveRefs();
        CacheExamples();
        if (detailPanel != null) {
            detailPanel.Wire(ShowList);
            detailPanel.gameObject.SetActive(false);
        }
        WireExampleClicks();
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
        ResolveRefs();
        ShowList();
        if (_loadRoutine != null) StopCoroutine(_loadRoutine);
        _loadRoutine = StartCoroutine(LoadIndex());
    }

    public void ShowList() {
        if (detailPanel != null) detailPanel.Close();
        if (listRoot != null) listRoot.SetActive(true);
        if (headerTitle != null) headerTitle.gameObject.SetActive(true);
    }

    public void OpenDetail(string activityId) {
        if (string.IsNullOrEmpty(activityId)) return;
        if (_loadRoutine != null) StopCoroutine(_loadRoutine);
        _loadRoutine = StartCoroutine(LoadAndOpen(activityId));
    }

    private IEnumerator LoadIndex() {
        SetEmpty("正在加载活动…", true);
        ActivityIndexFile index = null;
        string error = null;
        yield return ActivityHttp.GetJson<ActivityIndexFile>(
            ActivityHttp.IndexPath,
            data => index = data,
            err => error = err
        );
        ClearSpawned();

        int count = 0;
        if (index != null && index.items != null) {
            foreach (ActivityIndexItem entry in index.items) {
                if (entry == null || string.IsNullOrEmpty(entry.id)) continue;
                SpawnItem(entry);
                count++;
            }
        }

        if (count > 0) {
            SetExamplesVisible(false);
            SetEmpty(null, false);
            yield break;
        }

        SetExamplesVisible(true);
        bool hasExample = HasVisibleExample();
        if (hasExample) {
            SetEmpty(null, false);
        } else {
            SetEmpty(string.IsNullOrEmpty(error) ? "暂无活动" : "活动加载失败", true);
        }
    }

    private IEnumerator LoadAndOpen(string activityId) {
        ActivityDetail detail = null;
        string error = null;
        yield return ActivityHttp.GetJson<ActivityDetail>(
            ActivityHttp.MetaPath(activityId),
            data => detail = data,
            err => error = err
        );
        if (detail == null) {
            if (NotificationManager.Instance != null) {
                NotificationManager.Instance.ShowTip("活动", false, error ?? "活动加载失败");
            }
            yield break;
        }
        ShowDetail(detail);
    }

    private void ShowDetail(ActivityDetail detail) {
        if (listRoot != null) listRoot.SetActive(false);
        if (headerTitle != null) headerTitle.gameObject.SetActive(false);
        if (detailPanel != null) detailPanel.Open(detail);
    }

    private void OpenPreview(ActivityItem item) {
        if (item == null) return;
        ShowDetail(new ActivityDetail {
            title = item.PreviewTitle,
            body = item.PreviewBody,
            image_urls = null,
        });
    }

    private void SpawnItem(ActivityIndexItem entry) {
        if (listContent == null || itemTemplate == null) return;
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

    private void CacheExamples() {
        _examples.Clear();
        if (listContent == null) return;
        for (int i = 0; i < listContent.childCount; i++) {
            Transform child = listContent.GetChild(i);
            if (child.name.StartsWith("Example_")) _examples.Add(child.gameObject);
        }
    }

    private void WireExampleClicks() {
        for (int i = 0; i < _examples.Count; i++) {
            if (_examples[i] == null) continue;
            ActivityItem item = _examples[i].GetComponent<ActivityItem>();
            if (item == null) continue;
            ActivityItem captured = item;
            item.SetClick(() => OpenPreview(captured));
        }
    }

    private void SetExamplesVisible(bool visible) {
        for (int i = 0; i < _examples.Count; i++) {
            if (_examples[i] != null) _examples[i].SetActive(visible);
        }
    }

    private bool HasVisibleExample() {
        for (int i = 0; i < _examples.Count; i++) {
            if (_examples[i] != null && _examples[i].activeSelf) return true;
        }
        return false;
    }

    private void SetEmpty(string text, bool visible) {
        if (emptyHint == null) return;
        if (text != null) emptyHint.text = text;
        emptyHint.gameObject.SetActive(visible);
    }

    private void ResolveRefs() {
        Transform chrome = transform.Find("Panel");
        if (chrome == null) chrome = transform;
        if (headerTitle == null) {
            Transform header = chrome.Find("Header/Title");
            if (header != null) headerTitle = header.GetComponent<TMP_Text>();
        }
        if (listRoot == null) {
            Transform scroll = chrome.Find("Scroll View");
            if (scroll != null) listRoot = scroll.gameObject;
        }
        if (listContent == null && listRoot != null) {
            Transform content = listRoot.transform.Find("Viewport/Content");
            if (content != null) listContent = content;
        }
        if (emptyHint == null) {
            Transform hint = chrome.Find("EmptyHint");
            if (hint != null) emptyHint = hint.GetComponent<TMP_Text>();
        }
        if (detailPanel == null) {
            Transform detail = chrome.Find("ActivityDetailPanel");
            if (detail != null) detailPanel = detail.GetComponent<ActivityDetailPanel>();
        }
    }
}
