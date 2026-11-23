using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class SpectatorPanel : MonoBehaviour {
    public static SpectatorPanel Instance { get; private set; }

    [SerializeField] private SpectatorPrefab SpectatorPrefab;
    [SerializeField] private SpectatorPrefab pairedSpectatorPrefab;
    [SerializeField] private Transform contentTransform;
    [SerializeField] private Button RefreshButton;
    [SerializeField] private Button OverviewButton;
    [SerializeField] private Button GuobiaoButton;
    [SerializeField] private Button OtherButton;
    [SerializeField] private ScrollRect spectatorScrollRect;
    [SerializeField] private TMP_Dropdown roomFilterDropdown;
    [SerializeField] private TMP_Dropdown tierFilterDropdown;
    [SerializeField] private TMP_Dropdown formatFilterDropdown;

    private enum ListFilter { Overview, Guobiao, Other }
    private sealed class SpectatorEntry {
        public readonly SpectatorInfo Info;
        public readonly SpectatorInfo[] Tables;
        public readonly string Key;

        public SpectatorEntry(SpectatorInfo info) {
            Info = info;
            // The event lobby groups the same packet independently and replaces paired_tables.
            Tables = info.paired_tables;
            Key = string.IsNullOrEmpty(info.match_id) ? "game:" + info.gamestate_id : "match:" + info.match_id;
        }

        public bool SameDisplay(SpectatorEntry previous) {
            if (!SameTable(Info, previous.Info)) return false;
            if (Tables == null || previous.Tables == null) return Tables == previous.Tables;
            if (Tables.Length != previous.Tables.Length) return false;
            for (int i = 0; i < Tables.Length; i++)
                if (!SameTable(Tables[i], previous.Tables[i])) return false;
            return true;
        }

        private static bool SameTable(SpectatorInfo a, SpectatorInfo b) {
            return a.gamestate_id == b.gamestate_id && a.table_id == b.table_id && a.match_id == b.match_id &&
                a.current_round == b.current_round && a.total_rounds == b.total_rounds &&
                a.rule == b.rule && a.sub_rule == b.sub_rule && a.room_type == b.room_type &&
                a.match_queue_type == b.match_queue_type && a.match_tier == b.match_tier &&
                a.player1_name == b.player1_name && a.player2_name == b.player2_name &&
                a.player3_name == b.player3_name && a.player4_name == b.player4_name &&
                (a.team_by_player_index == b.team_by_player_index ||
                 a.team_by_player_index != null && b.team_by_player_index != null &&
                 a.team_by_player_index.SequenceEqual(b.team_by_player_index));
        }
    }

    private SpectatorEntry[] entries = Array.Empty<SpectatorEntry>();
    private readonly Dictionary<string, SpectatorEntry> previousEntries = new();
    private readonly List<SpectatorEntry> filteredEntries = new();
    private readonly List<string> filteredKeys = new();
    private readonly HashSet<string> dirty = new();
    private ViewportCardList<string, SpectatorPrefab> cards;
    private float normalHeight, pairedHeight;
    private ListFilter _filter;

    private void Awake() {
        if (Instance == null) {
            Instance = this;
        } else {
            Destroy(gameObject);
            return;
        }
        if (RefreshButton != null) RefreshButton.onClick.AddListener(RefreshSpectatorList);
        if (OverviewButton != null) OverviewButton.onClick.AddListener(ShowOverview);
        if (GuobiaoButton != null) GuobiaoButton.onClick.AddListener(ShowGuobiao);
        if (OtherButton != null) OtherButton.onClick.AddListener(ShowOther);
        if (roomFilterDropdown != null) roomFilterDropdown.onValueChanged.AddListener(OnRoomFilterChanged);
        if (tierFilterDropdown != null) tierFilterDropdown.onValueChanged.AddListener(OnDropdownChanged);
        if (formatFilterDropdown != null) formatFilterDropdown.onValueChanged.AddListener(OnDropdownChanged);
        if (spectatorScrollRect != null) {
            spectatorScrollRect.movementType = UnityEngine.UI.ScrollRect.MovementType.Elastic;
            spectatorScrollRect.elasticity = .12f;
            spectatorScrollRect.inertia = true;
        }
        normalHeight = CardHeight(SpectatorPrefab);
        pairedHeight = pairedSpectatorPrefab ? CardHeight(pairedSpectatorPrefab) : normalHeight;
        cards = new ViewportCardList<string, SpectatorPrefab>(spectatorScrollRect, (RectTransform)contentTransform,
            kind => Instantiate(kind == 1 ? pairedSpectatorPrefab : SpectatorPrefab, contentTransform),
            (card, index) => {
                var entry = filteredEntries[index];
                card.InitializeSpectatorItem(entry.Info, entry.Tables);
            });
    }

    private static float CardHeight(SpectatorPrefab prefab) {
        var layout = prefab.GetComponent<LayoutElement>();
        return layout != null && layout.preferredHeight > 0 ? layout.preferredHeight : ((RectTransform)prefab.transform).rect.height;
    }

    private void OnEnable() {
        if (cards == null) return;
        var games = GameStateNetworkManager.Instance;
        if (games != null) {
            SetSnapshot(games.LatestSpectatorList, true);
            RefreshSpectatorList();
        }
    }

    private void LateUpdate() => cards?.Tick();

    private void OnDisable() => cards?.Hide();

    private void OnDestroy() {
        cards?.Dispose();
        if (RefreshButton != null) RefreshButton.onClick.RemoveListener(RefreshSpectatorList);
        if (OverviewButton != null) OverviewButton.onClick.RemoveListener(ShowOverview);
        if (GuobiaoButton != null) GuobiaoButton.onClick.RemoveListener(ShowGuobiao);
        if (OtherButton != null) OtherButton.onClick.RemoveListener(ShowOther);
        if (roomFilterDropdown != null) roomFilterDropdown.onValueChanged.RemoveListener(OnRoomFilterChanged);
        if (tierFilterDropdown != null) tierFilterDropdown.onValueChanged.RemoveListener(OnDropdownChanged);
        if (formatFilterDropdown != null) formatFilterDropdown.onValueChanged.RemoveListener(OnDropdownChanged);
        if (Instance == this) Instance = null;
    }

    private void RefreshSpectatorList() {
        if (GameStateNetworkManager.Instance != null) GameStateNetworkManager.Instance.GetSpectatorList();
    }

    private void ShowOverview() { SetFilter(ListFilter.Overview); }
    private void ShowGuobiao() { SetFilter(ListFilter.Guobiao); }
    private void ShowOther() { SetFilter(ListFilter.Other); }

    private void SetFilter(ListFilter filter) {
        if (_filter == filter) return;
        _filter = filter;
        ApplyFilter(true);
    }

    private void OnRoomFilterChanged(int value) {
        if (tierFilterDropdown != null) {
            bool custom = value == (int)SpectatorRoomPresentation.RoomFilter.Custom;
            if (custom) tierFilterDropdown.SetValueWithoutNotify(0);
            tierFilterDropdown.interactable = !custom;
        }
        ApplyFilter(true);
    }

    private void OnDropdownChanged(int value) { ApplyFilter(true); }

    private int CardKind(int index) => filteredEntries[index].Tables != null && pairedSpectatorPrefab != null ? 1 : 0;

    private void ApplyFilter(bool resetScroll) {
        if (!isActiveAndEnabled || cards == null) return;
        var room = (SpectatorRoomPresentation.RoomFilter)(roomFilterDropdown != null ? roomFilterDropdown.value : 0);
        var format = (SpectatorRoomPresentation.FormatFilter)(formatFilterDropdown != null ? formatFilterDropdown.value : 0);
        int tierIndex = tierFilterDropdown != null ? tierFilterDropdown.value : 0;
        string tier = SpectatorRoomPresentation.TierKeys[Mathf.Clamp(tierIndex, 0, SpectatorRoomPresentation.TierKeys.Length - 1)];
        filteredKeys.Clear();
        filteredEntries.Clear();
        foreach (var entry in entries) {
            if (_filter != ListFilter.Overview && (_filter == ListFilter.Guobiao) != IsGuobiao(entry.Info)) continue;
            if (!SpectatorRoomPresentation.Matches(entry.Info, room, tier, format)) continue;
            filteredKeys.Add(entry.Key);
            filteredEntries.Add(entry);
        }
        cards.SetRows(filteredKeys, CardKind, index => CardKind(index) == 1 ? pairedHeight : normalHeight, dirty, resetScroll);
        dirty.Clear();
    }

    private static bool IsGuobiao(SpectatorInfo spectator) {
        string rule = string.IsNullOrWhiteSpace(spectator.rule) ? spectator.sub_rule : spectator.rule;
        if (string.IsNullOrWhiteSpace(rule)) return false;
        rule = rule.Trim();
        return rule.Equals("guobiao", StringComparison.OrdinalIgnoreCase) ||
            rule.StartsWith("guobiao/", StringComparison.OrdinalIgnoreCase);
    }

    public void GetSpectatorListResponse(bool success, string message, SpectatorInfo[] spectatorList) {
        if (!success) {
            Debug.LogError($"获取观战列表失败: {message}");
            return;
        }

        if (isActiveAndEnabled && cards != null) SetSnapshot(spectatorList, false);
    }

    private void SetSnapshot(SpectatorInfo[] snapshot, bool reopening) {
        var next = SpectatorRoomPresentation.GroupMatches(snapshot)
            .OrderByDescending(SpectatorRoomPresentation.Priority)
            .ThenBy(item => item.gamestate_id, StringComparer.Ordinal)
            .Select(info => new SpectatorEntry(info)).ToArray();
        bool changed = next.Length != entries.Length;
        dirty.Clear();
        for (int i = 0; i < next.Length; i++) {
            var entry = next[i];
            if (i >= entries.Length || entry.Key != entries[i].Key) changed = true;
            if (!previousEntries.TryGetValue(entry.Key, out var old) || !entry.SameDisplay(old))
                dirty.Add(entry.Key);
        }
        if (!changed && dirty.Count == 0 && !reopening) return;
        entries = next;
        previousEntries.Clear();
        foreach (var entry in next) previousEntries.Add(entry.Key, entry);
        ApplyFilter(false);
    }
}
