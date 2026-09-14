using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>原版局制和番种明细。仅调用已有五种统计接口；其他规则显示其条目和空数据状态。</summary>
public sealed class PlayerInfoStatistics : MonoBehaviour {
    [SerializeField] private ScrollRect scroll;
    [SerializeField] private RectTransform content;
    [SerializeField] private PlayerInfoEntry entryPrefab;
    [SerializeField] private List<PlayerInfoEntry> entries = new List<PlayerInfoEntry>();
    private readonly Dictionary<string, RuleStatsResponse> cache = new Dictionary<string, RuleStatsResponse>();
    private readonly Dictionary<string, int> inFlightUsers = new Dictionary<string, int>();
    private readonly HashSet<string> failedRules = new HashSet<string>();
    private int userId;
    private string currentRule;
    private bool ranked;
    private bool refreshing;
    private LayoutElement contentMinimum;
    private float naturalContentHeight;
    public float ViewportHeight => scroll.viewport.rect.height;

    public void ResetUser(int id, string loadedRule = null, RuleStatsResponse loadedStats = null) {
        userId = id;
        cache.Clear();
        failedRules.Clear();
        currentRule = null;
        if (loadedRule != null && loadedStats != null) cache[loadedRule] = loadedStats;
        // 未完成请求仍保留其用户归属；切换玩家后旧回复不能写进新玩家缓存。
    }

    public void Show(string rule, bool isRanked, int id) {
        if (id != userId) ResetUser(id);
        bool changed = currentRule != rule || ranked != isRanked;
        currentRule = rule;
        ranked = isRanked && rule == "guobiao";
        RefreshRows(!changed);
        if (changed) {
            scroll.StopMovement();
            scroll.verticalNormalizedPosition = 1;
        }
        RequestCurrent();
    }

    private void RefreshRows(bool keepExpanded) {
        Vector2 offset = keepExpanded ? content.anchoredPosition : Vector2.zero;
        refreshing = true;
        cache.TryGetValue(currentRule, out var response);
        string[] modes = PlayerInfoStatsFormatter.Modes(currentRule, ranked);
        int index = 0;
        void Add(string caption, IList<KeyValuePair<string, string>> fields) {
            if (index == entries.Count) {
                var entry = Instantiate(entryPrefab, content);
                foreach (var child in entry.GetComponentsInChildren<Transform>(true)) child.gameObject.layer = gameObject.layer;
                entries.Add(entry);
            }
            var row = entries[index++];
            row.ExpansionChanged -= OnExpansionChanged;
            row.ExpansionChanged += OnExpansionChanged;
            row.gameObject.SetActive(true);
            row.Bind(caption, fields, keepExpanded);
        }
        foreach (string mode in modes) {
            var modeStats = PlayerInfoStatsFormatter.Find(response, currentRule, mode);
            // 已收到完整统计但此局制尚无对局时，保留原版的零值明细。
            if (modeStats == null && response?.history_stats != null)
                modeStats = new PlayerStatsInfo { rule = currentRule, mode = mode };
            Add(PlayerInfoStatsFormatter.ModeCaption(currentRule, mode, ranked), PlayerInfoStatsFormatter.GameDetails(modeStats, currentRule));
        }
        if (ranked) {
            var total = PlayerInfoStatsFormatter.Aggregate(response, currentRule, modes);
            if (total == null && response?.history_stats != null) total = new PlayerStatsInfo { rule = currentRule };
            Add("国标麻将总计（天梯）", PlayerInfoStatsFormatter.GameDetails(total, currentRule));
        }
        Add(PlayerInfoStatsFormatter.FanCaption(currentRule, ranked),
            PlayerInfoStatsFormatter.FanDetails(currentRule, ranked ? response?.ranked_fan_stats : response?.total_fan_stats));
        for (int i = index; i < entries.Count; i++) entries[i].gameObject.SetActive(false);
        refreshing = false;
        // 固定浏览窗口，局制和明细按可读尺寸排布，通过滚动访问完整内容。
        RebuildAtOffset(offset);
    }

    private void OnExpansionChanged(PlayerInfoEntry entry) {
        if (refreshing) return;
        RebuildAtOffset(content.anchoredPosition);
    }

    private void RebuildAtOffset(Vector2 offset) {
        scroll.StopMovement();
        if (contentMinimum == null)
            contentMinimum = content.GetComponent<LayoutElement>() ?? content.gameObject.AddComponent<LayoutElement>();
        contentMinimum.minHeight = -1;
        LayoutRebuilder.ForceRebuildLayoutImmediate(content);
        naturalContentHeight = content.rect.height;
        // 收起底部条目后保留必要的尾部空白，避免 ScrollRect 钳制边界时挪动标签。
        contentMinimum.minHeight = Mathf.Max(naturalContentHeight, ViewportHeight + Mathf.Max(0, offset.y));
        LayoutRebuilder.ForceRebuildLayoutImmediate(content);
        content.anchoredPosition = offset;
    }

    private void LateUpdate() {
        if (contentMinimum == null || contentMinimum.minHeight <= naturalContentHeight) return;
        float height = Mathf.Max(naturalContentHeight, ViewportHeight + Mathf.Max(0, content.anchoredPosition.y));
        if (Mathf.Abs(height - contentMinimum.minHeight) < .1f) return;
        // 用户往上滑时逐步释放尾部空白，保持当前像素偏移，不按滚动百分比跳转。
        Vector2 offset = content.anchoredPosition;
        contentMinimum.minHeight = height;
        LayoutRebuilder.ForceRebuildLayoutImmediate(content);
        content.anchoredPosition = offset;
    }

    private void RequestCurrent() {
        if (!Application.isPlaying || userId <= 0 || cache.ContainsKey(currentRule) || failedRules.Contains(currentRule)
            || inFlightUsers.ContainsKey(currentRule) || DataNetworkManager.Instance == null) return;
        var network = DataNetworkManager.Instance;
        string id = userId.ToString();
        switch (currentRule) {
            case "guobiao": inFlightUsers[currentRule] = userId; network.GetGuobiaoStats(id); break;
            case "riichi": inFlightUsers[currentRule] = userId; network.GetRiichiStats(id); break;
            case "qingque": inFlightUsers[currentRule] = userId; network.GetQingqueStats(id); break;
            case "classical": inFlightUsers[currentRule] = userId; network.GetClassicalStats(id); break;
            case "jiandan": inFlightUsers[currentRule] = userId; network.GetJiandanStats(id); break;
        }
    }

    public void Receive(string rule, bool success, string message, RuleStatsResponse response) {
        if (!inFlightUsers.TryGetValue(rule, out int requestedUser)) return;
        inFlightUsers.Remove(rule);
        if (requestedUser == userId) {
            if (success && response != null) cache[rule] = response;
            else {
                failedRules.Add(rule);
                if (gameObject.activeInHierarchy) NotificationManager.Instance?.ShowTip("获取数据", false, message ?? "获取统计数据失败");
            }
            if (rule == currentRule) RefreshRows(true);
        }
        RequestCurrent();
    }
}
