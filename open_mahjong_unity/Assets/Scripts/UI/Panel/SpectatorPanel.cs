using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class SpectatorPanel : MonoBehaviour {
    public static SpectatorPanel Instance { get; private set; }

    [SerializeField] private SpectatorPrefab SpectatorPrefab;
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
    private struct SpectatorEntry {
        public SpectatorPrefab Item;
        public bool IsGuobiao;
        public SpectatorInfo Info;
    }

    private readonly List<SpectatorEntry> _items = new List<SpectatorEntry>();
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
        ApplyFilter();
    }

    private void OnEnable() {
        RefreshSpectatorList();
    }

    private void OnDestroy() {
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
        _filter = filter;
        ApplyFilter();
    }

    private void OnRoomFilterChanged(int value) {
        if (tierFilterDropdown != null) {
            bool custom = value == (int)SpectatorRoomPresentation.RoomFilter.Custom;
            if (custom) tierFilterDropdown.SetValueWithoutNotify(0);
            tierFilterDropdown.interactable = !custom;
        }
        ApplyFilter();
    }

    private void OnDropdownChanged(int value) { ApplyFilter(); }

    private void ApplyFilter(float? preservedPosition = null) {
        var room = (SpectatorRoomPresentation.RoomFilter)(roomFilterDropdown != null ? roomFilterDropdown.value : 0);
        var format = (SpectatorRoomPresentation.FormatFilter)(formatFilterDropdown != null ? formatFilterDropdown.value : 0);
        int tierIndex = tierFilterDropdown != null ? tierFilterDropdown.value : 0;
        string tier = SpectatorRoomPresentation.TierKeys[Mathf.Clamp(tierIndex, 0, SpectatorRoomPresentation.TierKeys.Length - 1)];
        int count = 0;
        foreach (var entry in _items) {
            bool visible = (_filter == ListFilter.Overview ||
                (_filter == ListFilter.Guobiao ? entry.IsGuobiao : !entry.IsGuobiao)) &&
                SpectatorRoomPresentation.Matches(entry.Info, room, tier, format);
            entry.Item.gameObject.SetActive(visible);
            if (visible) count++;
        }
        if (contentTransform is RectTransform content) LayoutRebuilder.ForceRebuildLayoutImmediate(content);
        if (spectatorScrollRect != null) {
            // Resolve the content/viewport bounds before restoring the scroll position.
            // A refresh must not continually pull a reader back to the first row.
            Canvas.ForceUpdateCanvases();
            spectatorScrollRect.StopMovement();
            spectatorScrollRect.verticalNormalizedPosition = count == 0 || !preservedPosition.HasValue
                ? 1f : Mathf.Clamp01(preservedPosition.Value);
        }
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

        // Capture before removing rows; an empty list has no meaningful scroll position.
        float? position = _items.Count > 0 && spectatorScrollRect != null
            ? spectatorScrollRect.verticalNormalizedPosition : (float?)null;
        _items.Clear();
        foreach (Transform child in contentTransform) {
            child.gameObject.SetActive(false);
            Destroy(child.gameObject);
        }

        if (spectatorList != null) {
            foreach (var spectator in spectatorList.Where(item => item != null)
                .OrderByDescending(SpectatorRoomPresentation.Priority).ThenBy(item => item.gamestate_id, StringComparer.Ordinal)) {
                SpectatorPrefab item = Instantiate(SpectatorPrefab, contentTransform);
                item.InitializeSpectatorItem(spectator);
                _items.Add(new SpectatorEntry { Item = item, IsGuobiao = IsGuobiao(spectator), Info = spectator });
            }
        }
        ApplyFilter(position);
    }
}
