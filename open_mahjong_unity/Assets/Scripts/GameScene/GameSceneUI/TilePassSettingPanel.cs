using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// 牌张设置：勾选后不询问对应弃牌/加杠牌的一切鸣牌（含荣和；抢杠且未开「无视抢杠」时除外）。
/// 层级：HintRow + 4 排牌张 + 1 排全选（万/条/饼/字/红宝/无视抢杠）。
/// </summary>
public class TilePassSettingPanel : MonoBehaviour {
    private static readonly int[][] RowTileIds = {
        new[] { 11, 12, 13, 14, 15, 16, 17, 18, 19 },
        new[] { 31, 32, 33, 34, 35, 36, 37, 38, 39 },
        new[] { 21, 22, 23, 24, 25, 26, 27, 28, 29 },
        new[] { 41, 42, 43, 44, 45, 46, 47 },
    };

    private static readonly int[] RedDoraTileIds = { 105, 205, 305 };

    private const string HintText = "不询问以下选中的牌：";
    private const int HintRowIndex = 0;
    private const int FirstTileRowIndex = 1;
    private const int SelectAllRowIndex = 5;
    private const int SelectAllRedDoraChildIndex = 4;
    private const int IgnoreRobKongChildIndex = 5;

    [Header("说明（可留空，自动从 HintRow 查找）")]
    [SerializeField] private TMP_Text hintLabel;

    [Header("牌张行（可留空，自动跳过 HintRow 后取 4 排）")]
    [SerializeField] private Transform[] tileRows = new Transform[4];

    [Header("全选（可留空，自动从第六排子物体查找 Toggle）")]
    [SerializeField] private Toggle selectAllManToggle;
    [SerializeField] private Toggle selectAllSouToggle;
    [SerializeField] private Toggle selectAllPinToggle;
    [SerializeField] private Toggle selectAllHonorToggle;
    [SerializeField] private Toggle selectAllRedDoraToggle;
    [SerializeField] private Toggle ignoreRobKongToggle;

    private readonly HashSet<int> passTileIds = new HashSet<int>();
    private readonly Dictionary<int, Toggle> tileToggles = new Dictionary<int, Toggle>();
    private bool isWired;
    private bool isUpdatingSelectAll;
    private bool ignoreRobKong;

    public bool IgnoreRobKong => ignoreRobKong;

    public void Initialize() {
        WireIfNeeded();
        ResetSettings();
    }

    public void ResetSettings() {
        isUpdatingSelectAll = true;
        passTileIds.Clear();
        ignoreRobKong = false;
        foreach (Toggle toggle in tileToggles.Values) {
            if (toggle != null) toggle.SetIsOnWithoutNotify(false);
        }
        SetSelectAllSilently(selectAllManToggle, false);
        SetSelectAllSilently(selectAllSouToggle, false);
        SetSelectAllSilently(selectAllPinToggle, false);
        SetSelectAllSilently(selectAllHonorToggle, false);
        SetSelectAllSilently(selectAllRedDoraToggle, false);
        SetSelectAllSilently(ignoreRobKongToggle, false);
        isUpdatingSelectAll = false;
    }

    public void SetPanelVisible(bool visible) {
        gameObject.SetActive(visible);
    }

    public bool ShouldAutoPassForCurrentDiscard() {
        NormalGameStateManager gsm = NormalGameStateManager.Instance;
        if (gsm != null && gsm.IsQiangGangAsk && !ignoreRobKong) {
            return false;
        }
        if (gsm == null || gsm.currentAskCutTileId <= 0) return false;
        return passTileIds.Contains(gsm.currentAskCutTileId);
    }

    private void WireIfNeeded() {
        if (isWired) return;

        ResolveHierarchyReferences();

        if (hintLabel != null) {
            hintLabel.text = HintText;
        }

        tileToggles.Clear();
        for (int rowIndex = 0; rowIndex < tileRows.Length && rowIndex < RowTileIds.Length; rowIndex++) {
            WireTileRow(tileRows[rowIndex], RowTileIds[rowIndex]);
        }

        WireSelectAllToggle(selectAllManToggle, RowTileIds[0]);
        WireSelectAllToggle(selectAllSouToggle, RowTileIds[1]);
        WireSelectAllToggle(selectAllPinToggle, RowTileIds[2]);
        WireSelectAllToggle(selectAllHonorToggle, RowTileIds[3]);
        WireSelectAllToggle(selectAllRedDoraToggle, RedDoraTileIds);

        if (ignoreRobKongToggle != null) {
            ignoreRobKongToggle.onValueChanged.RemoveAllListeners();
            ignoreRobKongToggle.onValueChanged.AddListener(isOn => ignoreRobKong = isOn);
        }

        isWired = true;
    }

    private void ResolveHierarchyReferences() {
        if (transform.childCount <= SelectAllRowIndex) {
            Debug.LogWarning("TilePassSettingPanel: 子物体不足，需要 HintRow + 4 排牌张 + 1 排全选。");
            return;
        }

        if (hintLabel == null) {
            Transform hintRow = transform.GetChild(HintRowIndex);
            hintLabel = hintRow.GetComponentInChildren<TMP_Text>(true);
        }

        if (!HasAssignedTileRows()) {
            for (int i = 0; i < 4; i++) {
                tileRows[i] = transform.GetChild(FirstTileRowIndex + i);
            }
        }

        Transform selectAllRow = transform.GetChild(SelectAllRowIndex);
        if (selectAllManToggle == null && selectAllRow.childCount > 0) {
            selectAllManToggle = FindToggleInCell(selectAllRow.GetChild(0));
        }
        if (selectAllSouToggle == null && selectAllRow.childCount > 1) {
            selectAllSouToggle = FindToggleInCell(selectAllRow.GetChild(1));
        }
        if (selectAllPinToggle == null && selectAllRow.childCount > 2) {
            selectAllPinToggle = FindToggleInCell(selectAllRow.GetChild(2));
        }
        if (selectAllHonorToggle == null && selectAllRow.childCount > 3) {
            selectAllHonorToggle = FindToggleInCell(selectAllRow.GetChild(3));
        }
        if (selectAllRedDoraToggle == null && selectAllRow.childCount > SelectAllRedDoraChildIndex) {
            selectAllRedDoraToggle = FindToggleInCell(selectAllRow.GetChild(SelectAllRedDoraChildIndex));
        }
        if (ignoreRobKongToggle == null && selectAllRow.childCount > IgnoreRobKongChildIndex) {
            ignoreRobKongToggle = FindToggleInCell(selectAllRow.GetChild(IgnoreRobKongChildIndex));
        }
    }

    private bool HasAssignedTileRows() {
        if (tileRows == null || tileRows.Length < 4) return false;
        for (int i = 0; i < 4; i++) {
            if (tileRows[i] == null) return false;
        }
        return true;
    }

    private void WireTileRow(Transform row, int[] tileIds) {
        if (row == null || tileIds == null) return;

        int count = Mathf.Min(row.childCount, tileIds.Length);
        for (int i = 0; i < count; i++) {
            Transform cell = row.GetChild(i);
            Toggle toggle = FindToggleInCell(cell);
            if (toggle == null) {
                Debug.LogWarning($"TilePassSettingPanel: {cell.name} 下未找到 Toggle。");
                continue;
            }

            int tileId = tileIds[i];
            toggle.onValueChanged.RemoveAllListeners();
            tileToggles[tileId] = toggle;
            toggle.onValueChanged.AddListener(isOn => OnTileToggleChanged(tileId, isOn));
        }

        if (row.childCount < tileIds.Length) {
            Debug.LogWarning($"TilePassSettingPanel: {row.name} 子物体不足，需要 {tileIds.Length} 个，当前 {row.childCount} 个。");
        }
    }

    private static Toggle FindToggleInCell(Transform cell) {
        if (cell == null) return null;
        Toggle[] toggles = cell.GetComponentsInChildren<Toggle>(true);
        if (toggles == null || toggles.Length == 0) return null;
        return toggles[0];
    }

    private void WireSelectAllToggle(Toggle toggle, int[] tileIds) {
        if (toggle == null || tileIds == null) return;
        toggle.onValueChanged.RemoveAllListeners();
        toggle.onValueChanged.AddListener(isOn => OnSelectAllChanged(tileIds, isOn));
    }

    private void OnTileToggleChanged(int tileId, bool isOn) {
        if (isUpdatingSelectAll) return;

        if (isOn) passTileIds.Add(tileId);
        else passTileIds.Remove(tileId);

        RefreshSelectAllStates();
    }

    private void OnSelectAllChanged(int[] tileIds, bool isOn) {
        if (isUpdatingSelectAll) return;

        isUpdatingSelectAll = true;
        foreach (int tileId in tileIds) {
            if (tileToggles.TryGetValue(tileId, out Toggle toggle) && toggle != null) {
                toggle.SetIsOnWithoutNotify(isOn);
            }
            if (isOn) passTileIds.Add(tileId);
            else passTileIds.Remove(tileId);
        }
        isUpdatingSelectAll = false;
        RefreshSelectAllStates();
    }

    private void RefreshSelectAllStates() {
        SetSelectAllSilently(selectAllManToggle, AreAllSelected(RowTileIds[0]));
        SetSelectAllSilently(selectAllSouToggle, AreAllSelected(RowTileIds[1]));
        SetSelectAllSilently(selectAllPinToggle, AreAllSelected(RowTileIds[2]));
        SetSelectAllSilently(selectAllHonorToggle, AreAllSelected(RowTileIds[3]));
        SetSelectAllSilently(selectAllRedDoraToggle, AreAllSelected(RedDoraTileIds));
    }

    private bool AreAllSelected(int[] tileIds) {
        foreach (int tileId in tileIds) {
            if (!passTileIds.Contains(tileId)) return false;
        }
        return tileIds.Length > 0;
    }

    private static void SetSelectAllSilently(Toggle toggle, bool value) {
        if (toggle == null) return;
        toggle.SetIsOnWithoutNotify(value);
    }
}
