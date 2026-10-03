using System;
using System.Collections.Generic;
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

    private enum ListFilter { Overview, Guobiao, Other }
    private struct SpectatorEntry {
        public SpectatorPrefab Item;
        public bool IsGuobiao;
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

    private void ApplyFilter() {
        foreach (var entry in _items) {
            bool visible = _filter == ListFilter.Overview ||
                (_filter == ListFilter.Guobiao ? entry.IsGuobiao : !entry.IsGuobiao);
            entry.Item.gameObject.SetActive(visible);
        }

        if (contentTransform is RectTransform content) LayoutRebuilder.ForceRebuildLayoutImmediate(content);
        if (spectatorScrollRect != null) {
            spectatorScrollRect.StopMovement();
            spectatorScrollRect.verticalNormalizedPosition = 1f;
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

        _items.Clear();
        foreach (Transform child in contentTransform) {
            child.gameObject.SetActive(false);
            Destroy(child.gameObject);
        }

        if (spectatorList != null) {
            foreach (var spectator in spectatorList) {
                if (spectator == null) continue;
                SpectatorPrefab item = Instantiate(SpectatorPrefab, contentTransform);
                item.InitializeSpectatorItem(
                    spectator.rule,
                    spectator.sub_rule,
                    spectator.player1_name,
                    spectator.player2_name,
                    spectator.player3_name,
                    spectator.player4_name,
                    spectator.gamestate_id
                );
                _items.Add(new SpectatorEntry { Item = item, IsGuobiao = IsGuobiao(spectator) });
            }
        }
        ApplyFilter();
    }
}
