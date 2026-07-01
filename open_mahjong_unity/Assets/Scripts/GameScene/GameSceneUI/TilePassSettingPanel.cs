using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// 牌张设置：勾选牌张后不询问对应弃牌/加杠牌的一切鸣牌（含荣和）。
/// 层级：HintRow + 4 排牌张 + 1 排选项（全选/行为开关）。
/// 选项排列：所有牌张、万/条/饼/字/红宝、不吃、不碰、不明杠、不点和、不自摸、不抢杠。
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
    private const int SelectAllTilesChildIndex = 0;
    private const int SelectAllManChildIndex = 1;
    private const int SelectAllSouChildIndex = 2;
    private const int SelectAllPinChildIndex = 3;
    private const int SelectAllHonorChildIndex = 4;
    private const int SelectAllRedDoraChildIndex = 5;
    private const int PassChiChildIndex = 6;
    private const int PassPengChildIndex = 7;
    private const int PassMingGangChildIndex = 8;
    private const int NoRonChildIndex = 9;
    private const int NoTsumoChildIndex = 10;
    private const int NoRobKongChildIndex = 11;

    [Header("说明（可留空，自动从 HintRow 查找）")]
    [SerializeField] private TMP_Text hintLabel;

    [Header("牌张行（可留空，自动跳过 HintRow 后取 4 排）")]
    [SerializeField] private Transform[] tileRows = new Transform[4];

    [Header("全选与行为（可留空，自动从第六排子物体查找 Toggle）")]
    [SerializeField] private Toggle selectAllTilesToggle;
    [SerializeField] private Toggle selectAllManToggle;
    [SerializeField] private Toggle selectAllSouToggle;
    [SerializeField] private Toggle selectAllPinToggle;
    [SerializeField] private Toggle selectAllHonorToggle;
    [SerializeField] private Toggle selectAllRedDoraToggle;
    [SerializeField] private Toggle passChiToggle;
    [SerializeField] private Toggle passPengToggle;
    [SerializeField] private Toggle passMingGangToggle;
    [SerializeField] private Toggle noRonToggle;
    [SerializeField] private Toggle noTsumoToggle;
    [SerializeField] private Toggle noRobKongToggle;

    private readonly HashSet<int> passTileIds = new HashSet<int>();
    private readonly Dictionary<int, Toggle> tileToggles = new Dictionary<int, Toggle>();
    private bool isWired;
    private bool isUpdatingSelectAll;
    private bool passChi;
    private bool passPeng;
    private bool passMingGang;
    private bool noRon;
    private bool noTsumo;
    private bool noRobKong;

    public bool PassChi => passChi;
    public bool PassPeng => passPeng;
    public bool PassMingGang => passMingGang;
    public bool NoRon => noRon;
    public bool NoTsumo => noTsumo;
    public bool NoRobKong => noRobKong;

    public bool HasAnyMingPaiPassOption =>
        passChi || passPeng || passMingGang || passTileIds.Count > 0;

    public void Initialize() {
        WireIfNeeded();
        ResetSettings();
    }

    public void ResetSettings() {
        isUpdatingSelectAll = true;
        passTileIds.Clear();
        passChi = false;
        passPeng = false;
        passMingGang = false;
        noRon = false;
        noTsumo = false;
        noRobKong = false;
        foreach (Toggle toggle in tileToggles.Values) {
            if (toggle != null) toggle.SetIsOnWithoutNotify(false);
        }
        SetSelectAllSilently(selectAllTilesToggle, false);
        SetSelectAllSilently(selectAllManToggle, false);
        SetSelectAllSilently(selectAllSouToggle, false);
        SetSelectAllSilently(selectAllPinToggle, false);
        SetSelectAllSilently(selectAllHonorToggle, false);
        SetSelectAllSilently(selectAllRedDoraToggle, false);
        SetSelectAllSilently(passChiToggle, false);
        SetSelectAllSilently(passPengToggle, false);
        SetSelectAllSilently(passMingGangToggle, false);
        SetSelectAllSilently(noRonToggle, false);
        SetSelectAllSilently(noTsumoToggle, false);
        SetSelectAllSilently(noRobKongToggle, false);
        isUpdatingSelectAll = false;
    }

    public void SetPanelVisible(bool visible) {
        gameObject.SetActive(visible);
    }

    public bool ShouldAutoPassForCurrentDiscard() {
        NormalGameStateManager gsm = NormalGameStateManager.Instance;
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
        WireSelectAllTilesToggle();

        WireBehaviorToggle(passChiToggle, value => passChi = value);
        WireBehaviorToggle(passPengToggle, value => passPeng = value);
        WireBehaviorToggle(passMingGangToggle, value => passMingGang = value);
        WireBehaviorToggle(noRonToggle, value => noRon = value);
        WireBehaviorToggle(noTsumoToggle, value => noTsumo = value);
        WireBehaviorToggle(noRobKongToggle, value => noRobKong = value);

        isWired = true;
    }

    private void ResolveHierarchyReferences() {
        if (transform.childCount <= SelectAllRowIndex) {
            Debug.LogWarning("TilePassSettingPanel: 子物体不足，需要 HintRow + 4 排牌张 + 1 排选项。");
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
        selectAllTilesToggle = ResolveOptionToggle(selectAllRow, selectAllTilesToggle, -1, "所有牌张");
        selectAllManToggle = ResolveOptionToggle(selectAllRow, selectAllManToggle, SelectAllManChildIndex, "所有万子");
        selectAllSouToggle = ResolveOptionToggle(selectAllRow, selectAllSouToggle, SelectAllSouChildIndex, "所有条子");
        selectAllPinToggle = ResolveOptionToggle(selectAllRow, selectAllPinToggle, SelectAllPinChildIndex, "所有饼子");
        selectAllHonorToggle = ResolveOptionToggle(selectAllRow, selectAllHonorToggle, SelectAllHonorChildIndex, "所有字牌");
        selectAllRedDoraToggle = ResolveOptionToggle(selectAllRow, selectAllRedDoraToggle, SelectAllRedDoraChildIndex, "所有红宝");
        passChiToggle = ResolveOptionToggle(selectAllRow, passChiToggle, PassChiChildIndex, "不吃", "NoChi")
            ?? FindToggleInNamedDescendant(transform, "不吃", "NoChi");
        passPengToggle = ResolveOptionToggle(selectAllRow, passPengToggle, PassPengChildIndex, "不碰", "NoPeng")
            ?? FindToggleInNamedDescendant(transform, "不碰", "NoPeng");
        passMingGangToggle = ResolveOptionToggle(selectAllRow, passMingGangToggle, PassMingGangChildIndex, "不明杠", "NoGang")
            ?? FindToggleInNamedDescendant(transform, "不明杠", "NoGang");
        noRonToggle = ResolveOptionToggle(selectAllRow, noRonToggle, NoRonChildIndex, "不点和", "OnlyWinOther")
            ?? FindToggleInNamedDescendant(transform, "不点和", "OnlyWinOther");
        noTsumoToggle = ResolveOptionToggle(selectAllRow, noTsumoToggle, NoTsumoChildIndex, "不自摸", "OnlyWinSelf")
            ?? FindToggleInNamedDescendant(transform, "不自摸", "OnlyWinSelf");
        noRobKongToggle = ResolveOptionToggle(selectAllRow, noRobKongToggle, NoRobKongChildIndex, "不抢杠", "无视抢杠")
            ?? FindToggleInNamedDescendant(transform, "不抢杠", "无视抢杠");
    }

    private static Toggle FindToggleInNamedDescendant(Transform root, params string[] names) {
        if (root == null) return null;
        foreach (string name in names) {
            Transform[] all = root.GetComponentsInChildren<Transform>(true);
            foreach (Transform node in all) {
                if (node.name != name) continue;
                Toggle toggle = FindToggleInCell(node);
                if (toggle != null) return toggle;
            }
        }
        return null;
    }

    private static Toggle ResolveOptionToggle(Transform row, Toggle assigned, int childIndex, params string[] names) {
        if (assigned != null) return assigned;
        if (row == null) return null;

        foreach (string name in names) {
            Transform cell = FindDirectChildByName(row, name);
            if (cell != null) {
                Toggle toggle = FindToggleInCell(cell);
                if (toggle != null) return toggle;
            }
        }

        if (childIndex >= 0 && row.childCount > childIndex) {
            return FindToggleInCell(row.GetChild(childIndex));
        }

        return null;
    }

    private static Transform FindDirectChildByName(Transform parent, string name) {
        if (parent == null || string.IsNullOrEmpty(name)) return null;
        for (int i = 0; i < parent.childCount; i++) {
            Transform child = parent.GetChild(i);
            if (child.name == name) return child;
        }
        return null;
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

    private void WireSelectAllTilesToggle() {
        if (selectAllTilesToggle == null) return;
        selectAllTilesToggle.onValueChanged.RemoveAllListeners();
        selectAllTilesToggle.onValueChanged.AddListener(isOn => {
            OnSelectAllChanged(RowTileIds[0], isOn);
            OnSelectAllChanged(RowTileIds[1], isOn);
            OnSelectAllChanged(RowTileIds[2], isOn);
            OnSelectAllChanged(RowTileIds[3], isOn);
            OnSelectAllChanged(RedDoraTileIds, isOn);
        });
    }

    private void WireSelectAllToggle(Toggle toggle, int[] tileIds) {
        if (toggle == null || tileIds == null) return;
        toggle.onValueChanged.RemoveAllListeners();
        toggle.onValueChanged.AddListener(isOn => OnSelectAllChanged(tileIds, isOn));
    }

    private static void WireBehaviorToggle(Toggle toggle, System.Action<bool> setter) {
        if (toggle == null || setter == null) return;
        toggle.onValueChanged.RemoveAllListeners();
        toggle.onValueChanged.AddListener(isOn => setter(isOn));
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
        bool allTilesSelected =
            AreAllSelected(RowTileIds[0]) &&
            AreAllSelected(RowTileIds[1]) &&
            AreAllSelected(RowTileIds[2]) &&
            AreAllSelected(RowTileIds[3]) &&
            AreAllSelected(RedDoraTileIds);
        SetSelectAllSilently(selectAllTilesToggle, allTilesSelected);
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
