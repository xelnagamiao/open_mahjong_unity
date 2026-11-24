using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

// Content has its full scroll height even when only the rows near the viewport exist.
public sealed class ViewportCardList<TKey, TRow> : IDisposable where TRow : Component {
    private struct Entry {
        public TKey key;
        public int kind;
        public float top, height;
    }

    private readonly ScrollRect scroll;
    private readonly RectTransform content, viewport;
    private readonly RectOffset padding;
    private readonly float spacing;
    private readonly GridLayoutGroup grid;
    private readonly int columns;
    private readonly Func<int, TRow> create;
    private readonly Action<TRow, int> bind;
    private readonly CanvasGroup visibility;
    private int firstDataFrame;
    public bool FirstScreenShown { get; private set; }
    private readonly List<Entry> entries = new();
    private readonly Dictionary<TKey, int> indices = new();
    private readonly Dictionary<TKey, (TRow row, int kind)> visible = new();
    private readonly Dictionary<int, Stack<TRow>> pools = new();
    private readonly HashSet<TKey> dirty = new();
    private readonly List<TKey> recycle = new();
    private int previousFirst = -1, previousLast = -1;
    private int lastWorkFrame = -1;
    private bool pending = true;

    private const double FrameBudgetSeconds = 0.003;

    public ViewportCardList(ScrollRect scroll, RectTransform content, Func<int, TRow> create, Action<TRow, int> bind) {
        this.scroll = scroll;
        this.content = content;
        this.create = create;
        this.bind = bind;
        visibility = content.TryGetComponent(out CanvasGroup group) ? group : content.gameObject.AddComponent<CanvasGroup>();
        SetShown(false);
        viewport = scroll.viewport ? scroll.viewport : (RectTransform)scroll.transform;
        var layout = content.GetComponent<VerticalLayoutGroup>();
        grid = content.GetComponent<GridLayoutGroup>();
        columns = grid ? Mathf.Max(1, grid.constraintCount) : 1;
        padding = grid ? grid.padding : layout ? layout.padding : new RectOffset();
        spacing = grid ? grid.spacing.y : layout ? layout.spacing : 0;
        if (grid) grid.enabled = false;
        if (layout) layout.enabled = false;
        var fitter = content.GetComponent<ContentSizeFitter>();
        if (fitter) fitter.enabled = false;
        var anchor = content.anchorMin;
        anchor.y = 1;
        content.anchorMin = anchor;
    }

    public void SetRows(IReadOnlyList<TKey> keys, Func<int, int> kindAt, Func<int, float> heightAt,
                        ISet<TKey> dirty = null, bool resetScroll = false) {
        float offset = content.anchoredPosition.y;
        int anchorIndex = FindRow(offset);
        bool hadAnchor = entries.Count > 0;
        Entry anchor = hadAnchor ? entries[anchorIndex] : default;
        entries.Clear();
        indices.Clear();
        float top = padding.top;
        for (int i = 0; i < keys.Count; i++) {
            var entry = new Entry { key = keys[i], kind = kindAt(i), top = top, height = Mathf.Max(1, heightAt(i)) };
            entries.Add(entry);
            indices.Add(entry.key, i);
            if ((i + 1) % columns == 0 || i + 1 == keys.Count) top += entry.height + spacing;
        }
        float height = top + padding.bottom - (entries.Count > 0 ? spacing : 0);
        if (!Mathf.Approximately(content.rect.height, height))
            content.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, height);
        if (resetScroll) {
            scroll.StopMovement();
            offset = 0;
        } else if (hadAnchor && indices.TryGetValue(anchor.key, out int next)) {
            offset += entries[next].top - anchor.top;
        }
        offset = Mathf.Clamp(offset, 0, Mathf.Max(0, height - viewport.rect.height));
        if (!Mathf.Approximately(content.anchoredPosition.y, offset))
            content.anchoredPosition = new Vector2(content.anchoredPosition.x, offset);
        if (dirty != null)
            foreach (TKey key in dirty) Invalidate(key);
        if (entries.Count == 0) Hide();
        else if (!FirstScreenShown && !hadAnchor) firstDataFrame = Time.frameCount;
        pending = true;
    }

    public bool TryGetVisible(TKey key, out TRow row) {
        if (visible.TryGetValue(key, out var live)) {
            row = live.row;
            return true;
        }
        row = null;
        return false;
    }
    public bool IsInViewport(TKey key) => indices.TryGetValue(key, out var index) &&
        index >= previousFirst && index <= previousLast && visible.ContainsKey(key);

    public int FirstVisibleIndex => previousFirst;
    public int LastVisibleIndex => previousLast;

    public bool PrepareFirstRows(int kind, float rowHeight, Action<TRow> prepare) {
        if (entries.Count != 0) return true;
        if (viewport.rect.width <= 0 || viewport.rect.height <= 0) return false;
        int needed = Mathf.CeilToInt((viewport.rect.height - padding.top) / (rowHeight + spacing));
        if (needed <= 0) return true;
        if (!pools.TryGetValue(kind, out var pool)) pools.Add(kind, pool = new Stack<TRow>());
        double deadline = Time.realtimeSinceStartupAsDouble + FrameBudgetSeconds;
        int checkedRows = 0;
        foreach (var row in pool) {
            if (checkedRows++ >= needed) break;
            if (row.gameObject.activeInHierarchy) continue;
            prepare(row);
            if (Time.realtimeSinceStartupAsDouble >= deadline) return false;
        }
        while (pool.Count < needed) {
            var row = create(kind);
            prepare(row);
            pool.Push(row);
            if (Time.realtimeSinceStartupAsDouble >= deadline) break;
        }
        return pool.Count >= needed;
    }

    public void Invalidate(TKey key) {
        if (visible.ContainsKey(key)) {
            dirty.Add(key);
            pending = true;
        }
    }

    private int FindRow(float offset) {
        int lo = 0, hi = entries.Count - 1;
        while (lo < hi) {
            int mid = lo + (hi - lo) / 2;
            if (entries[mid].top + entries[mid].height <= offset) lo = mid + 1;
            else hi = mid;
        }
        return lo - lo % columns;
    }

    private void SetShown(bool shown) {
        FirstScreenShown = shown;
        visibility.alpha = shown ? 1 : 0;
        visibility.interactable = visibility.blocksRaycasts = shown;
    }

    public void Tick() {
        float extent = viewport.rect.height;
        if (entries.Count == 0 || extent <= 0 || viewport.rect.width <= 0 ||
            (!FirstScreenShown && firstDataFrame == Time.frameCount)) return;
        int first = FindRow(content.anchoredPosition.y);
        int last = Mathf.Min(entries.Count - 1, FindRow(content.anchoredPosition.y + extent - 0.01f) + columns - 1);
        if (first == previousFirst && last == previousLast &&
            (!pending || lastWorkFrame == Time.frameCount)) return;
        lastWorkFrame = Time.frameCount;
        previousFirst = first;
        previousLast = last;
        int from = Mathf.Max(0, first - 1), to = Mathf.Min(entries.Count - 1, last + 1);
        recycle.Clear();
        foreach (var pair in visible) {
            if (!indices.TryGetValue(pair.Key, out int index) || index < from || index > to ||
                entries[index].kind != pair.Value.kind) recycle.Add(pair.Key);
        }
        foreach (var key in recycle) Release(key, deactivate: false);
        double deadline = Time.realtimeSinceStartupAsDouble + FrameBudgetSeconds;
        pending = true;
        for (int i = first; i <= last; i++) {
            if (Show(i) && i < last && Time.realtimeSinceStartupAsDouble >= deadline) {
                HideUnused();
                return;
            }
        }
        if (!FirstScreenShown) SetShown(true);
        if (Time.realtimeSinceStartupAsDouble < deadline && from < first) Show(from);
        if (Time.realtimeSinceStartupAsDouble < deadline && to > last) Show(to);
        pending = dirty.Count != 0 || !visible.ContainsKey(entries[from].key) || !visible.ContainsKey(entries[to].key);
        HideUnused();
    }

    private void HideUnused() {
        foreach (var pool in pools.Values)
            foreach (var row in pool)
                if (row.transform.parent == content && row.gameObject.activeSelf) row.gameObject.SetActive(false);
    }

    private bool Show(int index) {
        Entry entry = entries[index];
        bool needsBind = dirty.Remove(entry.key);
        if (!visible.TryGetValue(entry.key, out var live)) {
            TRow row;
            if (pools.TryGetValue(entry.kind, out var pool) && pool.Count > 0) row = pool.Pop();
            else row = create(entry.kind);
            live = (row, entry.kind);
            visible.Add(entry.key, live);
            needsBind = true;
        }
        if (live.row.transform.parent != content) live.row.transform.SetParent(content, false);
        Position(live.row, entry, index);
        if (needsBind) bind(live.row, index);
        if (!live.row.gameObject.activeSelf) live.row.gameObject.SetActive(true);
        return needsBind;
    }

    private void Position(TRow row, Entry entry, int index) {
        var rect = (RectTransform)row.transform;
        Vector2 anchorMin = new Vector2(0, 1), anchorMax = Vector2.one;
        Vector2 size = new Vector2(-padding.horizontal, entry.height);
        Vector2 position = new Vector2(padding.left - padding.horizontal * rect.pivot.x,
                                       -entry.top - entry.height * (1 - rect.pivot.y));
        if (grid) {
            anchorMax = anchorMin;
            size = grid.cellSize;
            position.x = padding.left + (index % columns) * (size.x + grid.spacing.x) + size.x * rect.pivot.x;
        }
        if (rect.anchorMin != anchorMin) rect.anchorMin = anchorMin;
        if (rect.anchorMax != anchorMax) rect.anchorMax = anchorMax;
        if (rect.sizeDelta != size) rect.sizeDelta = size;
        if (rect.anchoredPosition != position) rect.anchoredPosition = position;
    }

    private void Release(TKey key, bool deactivate = true) {
        var live = visible[key];
        visible.Remove(key);
        dirty.Remove(key);
        if (deactivate) live.row.gameObject.SetActive(false);
        if (!pools.TryGetValue(live.kind, out var pool)) pools.Add(live.kind, pool = new Stack<TRow>());
        pool.Push(live.row);
    }

    public void Hide() {
        SetShown(false);
        previousFirst = previousLast = -1;
        firstDataFrame = Time.frameCount;
        recycle.Clear();
        recycle.AddRange(visible.Keys);
        foreach (TKey key in recycle) Release(key);
        pending = true;
    }

    /// <summary>
    /// 收缩已经滚过的卡片缓存，保留少量热卡片避免长列表无限增长。
    /// </summary>
    public void TrimPooledRows(int maximumPerKind) {
        maximumPerKind = Mathf.Max(0, maximumPerKind);
        foreach (var pool in pools.Values) {
            while (pool.Count > maximumPerKind) {
                var row = pool.Pop();
                if (row) UnityEngine.Object.Destroy(row.gameObject);
            }
        }
    }

    public void Dispose() {
        foreach (var live in visible.Values)
            if (live.row) UnityEngine.Object.Destroy(live.row.gameObject);
        foreach (var pool in pools.Values)
            foreach (var row in pool) if (row) UnityEngine.Object.Destroy(row.gameObject);
        visible.Clear();
        pools.Clear();
    }
}
