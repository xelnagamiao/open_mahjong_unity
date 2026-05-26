using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 游戏场景内的"被实时观战"指示器（仅 B 端使用）。
/// 挂载在常驻节点上，仅控制观战图标按钮与列表面板的显隐。
/// 数据源：FriendNetworkManager 的观战者列表推送 + 进入对局时主动 ListRealtimeSpectators。
/// </summary>
public class RealtimeSpectatorIndicator : MonoBehaviour {
    public static RealtimeSpectatorIndicator Instance { get; private set; }

    [Header("图标")]
    [SerializeField] private Button iconButton;
    [SerializeField] private Image icon;
    [SerializeField] private TMP_Text countText;

    [Header("观战者面板")]
    [SerializeField] private GameObject spectatorListPanel;
    [SerializeField] private Button closeButton;
    [SerializeField] private Transform listContent;
    [SerializeField] private RealtimeSpectatorRow rowPrefab;

    private readonly List<GameObject> _spawnedRows = new List<GameObject>();
    private readonly List<RealtimeSpectatorEntry> _currentSpectators = new List<RealtimeSpectatorEntry>();

    private void Awake() {
        if (Instance != null && Instance != this) {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        RegisterEvents();
        if (spectatorListPanel != null) spectatorListPanel.SetActive(false);
        if (iconButton != null) iconButton.onClick.AddListener(TogglePanel);
        if (closeButton != null) closeButton.onClick.AddListener(HidePanel);
        ApplyVisibility();
    }

    private void OnDestroy() {
        UnregisterEvents();
        if (Instance == this) Instance = null;
    }

    private void RegisterEvents() {
        if (FriendNetworkManager.Instance == null) return;
        FriendNetworkManager.Instance.OnRealtimeSpectatorsChanged -= HandleSpectatorsChanged;
        FriendNetworkManager.Instance.OnListRealtimeSpectatorsResp -= HandleSpectatorsChanged;
        FriendNetworkManager.Instance.OnRealtimeSpectatorsChanged += HandleSpectatorsChanged;
        FriendNetworkManager.Instance.OnListRealtimeSpectatorsResp += HandleSpectatorsChanged;
    }

    private void UnregisterEvents() {
        if (FriendNetworkManager.Instance == null) return;
        FriendNetworkManager.Instance.OnRealtimeSpectatorsChanged -= HandleSpectatorsChanged;
        FriendNetworkManager.Instance.OnListRealtimeSpectatorsResp -= HandleSpectatorsChanged;
    }

    /// <summary>由 GameSceneUIManager 在进入对局时调用：清空 + 主动询问一次当前观战者列表。</summary>
    public void ResetForNewGame() {
        _currentSpectators.Clear();
        ClearRows();
        HidePanel();
        ApplyVisibility();
        if (FriendNetworkManager.Instance != null) {
            FriendNetworkManager.Instance.ListRealtimeSpectators();
        }
    }

    public void HandleSpectatorsChanged(Response response) {
        _currentSpectators.Clear();
        if (response?.realtime_spectators != null) {
            foreach (var entry in response.realtime_spectators) {
                if (entry != null) _currentSpectators.Add(entry);
            }
        }
        RefreshList();
        ApplyVisibility();
        if (_currentSpectators.Count == 0) HidePanel();
    }

    private void ApplyVisibility() {
        bool hasSpectators = _currentSpectators.Count > 0;
        bool isSelfRealtimeSpectator = NormalGameStateManager.Instance != null
            && NormalGameStateManager.Instance.IsRealtimeSpectator;
        bool show = hasSpectators && !isSelfRealtimeSpectator;
        if (iconButton != null) iconButton.gameObject.SetActive(show);
        if (icon != null) icon.gameObject.SetActive(show);
        if (countText != null) {
            countText.gameObject.SetActive(show);
            countText.text = _currentSpectators.Count.ToString();
        }
        if (!show) HidePanel();
    }

    private void RefreshList() {
        ClearRows();
        if (rowPrefab == null || listContent == null) return;
        foreach (var entry in _currentSpectators) {
            RealtimeSpectatorRow row = Instantiate(rowPrefab, listContent);
            _spawnedRows.Add(row.gameObject);
            row.Bind(entry, uid => {
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
