using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// 游戏场景内的"被实时观战"指示器（仅 B 端使用）。
/// - 当 <c>realtime_spectators</c> 非空时显示 <see cref="icon"/>；列表为空时隐藏整个 UI。
/// - 点击 icon 可打开 / 关闭 <see cref="spectatorListPanel"/>，里面列出所有正在实时观战自己的人。
/// - 每行有"踢出"按钮，点击发送 friend/kick_realtime。
/// - 数据源：FriendNetworkManager.OnRealtimeSpectatorsChanged + 进入游戏时主动 ListRealtimeSpectators。
/// 注意：实时观战者（A 端）不需要这个组件，因此 IsRealtimeSpectator 时强制隐藏。
/// 修改记录：原"鼠标指向打开 tooltip"改为"点击图标打开面板"，
/// 否则鼠标指向时焦点离开 icon 面板就会关闭，导致 kick 按钮无法被点中。
/// </summary>
public class RealtimeSpectatorIndicator : MonoBehaviour {
    public static RealtimeSpectatorIndicator Instance { get; private set; }

    [Header("根节点")]
    [SerializeField] private GameObject root;            // 整个指示器根节点（含图标 + 面板）

    [Header("图标")]
    [SerializeField] private Button iconButton;          // 点击 icon 切换面板显隐
    [SerializeField] private Image icon;                 // 仅当有实时观战者时显示
    [SerializeField] private TMP_Text countText;         // 可选：图标上的数量徽章

    [Header("观战者面板")]
    [SerializeField] private GameObject spectatorListPanel; // 点击 icon 后展开的列表面板
    [SerializeField] private Button closeButton;            // 面板内可选的关闭按钮
    [SerializeField] private Transform listContent;         // 列表 ScrollView Content
    [SerializeField] private GameObject rowPrefab;          // 每行预制体（含 username TMP_Text + kickButton）

    private readonly List<GameObject> _spawnedRows = new List<GameObject>();
    private readonly List<RealtimeSpectatorEntry> _currentSpectators = new List<RealtimeSpectatorEntry>();

    private void Awake() {
        if (Instance != null && Instance != this) {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        if (spectatorListPanel != null) spectatorListPanel.SetActive(false);
        if (iconButton != null) iconButton.onClick.AddListener(TogglePanel);
        if (closeButton != null) closeButton.onClick.AddListener(HidePanel);
        UpdateIconVisibility();
    }

    private void OnEnable() {
        if (FriendNetworkManager.Instance != null) {
            FriendNetworkManager.Instance.OnRealtimeSpectatorsChanged += HandleSpectatorsChanged;
            FriendNetworkManager.Instance.OnListRealtimeSpectatorsResp += HandleSpectatorsChanged;
        }
    }

    private void OnDisable() {
        if (FriendNetworkManager.Instance != null) {
            FriendNetworkManager.Instance.OnRealtimeSpectatorsChanged -= HandleSpectatorsChanged;
            FriendNetworkManager.Instance.OnListRealtimeSpectatorsResp -= HandleSpectatorsChanged;
        }
    }

    /// <summary>由 GameSceneUIManager 在进入游戏时调用：清空 + 主动询问一次当前观战者列表。</summary>
    public void ResetForNewGame() {
        _currentSpectators.Clear();
        ClearRows();
        HidePanel();
        UpdateIconVisibility();
        if (FriendNetworkManager.Instance != null) {
            FriendNetworkManager.Instance.ListRealtimeSpectators();
        }
    }

    private void HandleSpectatorsChanged(Response response) {
        _currentSpectators.Clear();
        if (response?.realtime_spectators != null) {
            foreach (var entry in response.realtime_spectators) {
                if (entry != null) _currentSpectators.Add(entry);
            }
        }
        RefreshList();
        UpdateIconVisibility();
        if (_currentSpectators.Count == 0) HidePanel();
    }

    private void UpdateIconVisibility() {
        bool hasSpectators = _currentSpectators.Count > 0;
        bool isSelfRealtimeSpectator = NormalGameStateManager.Instance != null
            && NormalGameStateManager.Instance.IsRealtimeSpectator;
        bool show = hasSpectators && !isSelfRealtimeSpectator;
        if (root != null) root.SetActive(show);
        else if (icon != null) icon.gameObject.SetActive(show);
        if (countText != null) countText.text = _currentSpectators.Count.ToString();
        if (!show && spectatorListPanel != null) spectatorListPanel.SetActive(false);
    }

    private void RefreshList() {
        ClearRows();
        if (rowPrefab == null || listContent == null) return;
        foreach (var entry in _currentSpectators) {
            GameObject row = Instantiate(rowPrefab, listContent);
            _spawnedRows.Add(row);
            BindRow(row, entry);
        }
    }

    private void BindRow(GameObject row, RealtimeSpectatorEntry entry) {
        var label = row.GetComponentInChildren<TMP_Text>(true);
        if (label != null) {
            label.text = string.IsNullOrEmpty(entry.username) ? $"UID {entry.user_id}" : entry.username;
        }
        var btn = row.GetComponentInChildren<Button>(true);
        if (btn != null) {
            int uid = entry.user_id;
            btn.onClick.RemoveAllListeners();
            btn.onClick.AddListener(() => {
                if (FriendNetworkManager.Instance != null) {
                    FriendNetworkManager.Instance.KickRealtime(uid);
                }
            });
        }
    }

    private void ClearRows() {
        foreach (var go in _spawnedRows) {
            if (go != null) Destroy(go);
        }
        _spawnedRows.Clear();
    }

    private void TogglePanel() {
        if (spectatorListPanel == null) return;
        if (_currentSpectators.Count == 0) return;
        bool willShow = !spectatorListPanel.activeSelf;
        if (willShow) RefreshList();
        spectatorListPanel.SetActive(willShow);
    }

    private void HidePanel() {
        if (spectatorListPanel != null) spectatorListPanel.SetActive(false);
    }
}
