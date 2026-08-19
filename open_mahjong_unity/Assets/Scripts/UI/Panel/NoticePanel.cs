using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TMPro;

/// <summary>
/// 顶栏「通知」：场景里的示例只用于排版，运行时立刻隐藏，再按 HTTP 静态列表实例化预制体。
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
    private Coroutine _loadRoutine;

    private void Awake() {
        Instance = this;
        if (detailPanel != null) {
            detailPanel.Wire(ShowList);
            detailPanel.gameObject.SetActive(false);
        }
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
        HideEditorExamples();
        SetEmpty("正在加载活动…", true);
        ActivityIndexFile index = null;
        string error = null;
        yield return ActivityHttp.GetJson<ActivityIndexFile>(
            ActivityHttp.IndexPath,
            data => index = data,
            err => error = err
        );
        ClearSpawned();
        HideEditorExamples();

        int count = 0;
        if (index != null && index.items != null) {
            foreach (ActivityIndexItem entry in index.items) {
                if (entry == null || string.IsNullOrEmpty(entry.id)) continue;
                SpawnItem(entry);
                count++;
            }
        }

        if (count > 0) {
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
        if (listRoot != null) listRoot.SetActive(false);
        if (headerTitle != null) headerTitle.gameObject.SetActive(false);
        if (detailPanel != null) detailPanel.Open(detail);
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

    private void HideEditorExamples() {
        if (listContent == null) return;
        if (itemTemplate != null && itemTemplate.scene.IsValid()) {
            itemTemplate.SetActive(false);
        }
        for (int i = 0; i < listContent.childCount; i++) {
            Transform child = listContent.GetChild(i);
            if (emptyHint != null && child.gameObject == emptyHint.gameObject) continue;
            if (_spawned.Contains(child.gameObject)) continue;
            child.gameObject.SetActive(false);
        }
    }

    private void SetEmpty(string text, bool visible) {
        if (emptyHint == null) return;
        if (text != null) emptyHint.text = text;
        emptyHint.gameObject.SetActive(visible);
    }
}
