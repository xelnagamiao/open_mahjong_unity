using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 牌张设置子面板。
/// - 勾选牌张：命中河牌/加杠牌时不询问任何操作（含荣和/抢杠）。
/// - 选中牌不自动自摸：开启且命中摸入牌时，在「自动胡牌」下跳过自动自摸（仍可手动和）；跨局保留。
/// - 不吃/不碰/不明杠：逐项过滤对应鸣牌，不过和牌；三者全选时与主面板「自动过牌」联动。
/// - 不点和：阻止自动荣和，并将点和纳入自动过牌判定（与未过滤鸣牌并存时不跳过，等待玩家）。
/// - 不自摸/不抢杠：仅在「自动胡牌」开启时，分别阻止自动自摸/抢杠和。
/// </summary>
public class TilePassSettingPanel : MonoBehaviour {
    private static readonly int[][] RowTileIds = {
        new[] { 11, 12, 13, 14, 15, 16, 17, 18, 19 },
        new[] { 31, 32, 33, 34, 35, 36, 37, 38, 39 },
        new[] { 21, 22, 23, 24, 25, 26, 27, 28, 29 },
        new[] { 41, 42, 43, 44, 45, 46, 47 },
    };

    private static readonly int[] RedDoraTileIds = { 105, 205, 305 };

    private const int FirstTileRowIndex = 1;

    [Header("牌张行（可留空，自动跳过说明行后取 4 排）")]
    [SerializeField] private Transform[] tileRows = new Transform[4];

    [Header("全选与行为（Inspector 拖拽赋值）")]
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
    [SerializeField] private Toggle skipAutoTsumoOnSelectedToggle;

    private readonly HashSet<int> passTileIds = new HashSet<int>();
    private readonly Dictionary<int, Toggle> tileToggles = new Dictionary<int, Toggle>();
    private bool isWired;
    private bool isUpdatingSelectAll;
    private bool isSyncingMeldPassOptions;
    private bool passChi;
    private bool passPeng;
    private bool passMingGang;
    private bool noRon;
    private bool noTsumo;
    private bool noRobKong;
    /// <summary>选中牌摸入时跳过自动自摸；默认开启，ResetSettings 不清空。</summary>
    private bool skipAutoTsumoOnSelected = true;

    public bool PassChi => passChi;
    public bool PassPeng => passPeng;
    public bool PassMingGang => passMingGang;
    public bool NoRon => noRon;
    public bool NoTsumo => noTsumo;
    public bool NoRobKong => noRobKong;
    public bool SkipAutoTsumoOnSelected => skipAutoTsumoOnSelected;

    public bool HasAnyMingPaiPassOption =>
        passChi || passPeng || passMingGang || passTileIds.Count > 0;

    /// <summary>「不吃/不碰/不明杠」变更时通知 AutoAction 同步「自动过牌」显示。</summary>
    public System.Action OnMeldPassOptionsChanged;

    public bool AreAllMeldPassOptionsEnabled => passChi && passPeng && passMingGang;

    /// <summary>批量设置鸣牌过滤项；供主面板「自动过牌」级联调用。</summary>
    public void SetMeldPassOptions(bool chi, bool peng, bool mingGang) {
        isSyncingMeldPassOptions = true;
        passChi = chi;
        passPeng = peng;
        passMingGang = mingGang;
        SetSelectAllSilently(passChiToggle, chi);
        SetSelectAllSilently(passPengToggle, peng);
        SetSelectAllSilently(passMingGangToggle, mingGang);
        isSyncingMeldPassOptions = false;
        OnMeldPassOptionsChanged?.Invoke();
    }

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
        // skipAutoTsumoOnSelected 跨局保留，不同步重置
        isUpdatingSelectAll = false;
    }

    public void SetPanelVisible(bool visible) {
        gameObject.SetActive(visible);
    }

    /// <summary>当前询问牌是否在跳过列表中（命中则直接 pass，含荣和）。</summary>
    public bool ShouldAutoPassForCurrentDiscard() {
        NormalGameStateManager gsm = NormalGameStateManager.Instance;
        if (gsm == null || gsm.currentAskCutTileId <= 0) return false;
        return IsPassTile(gsm.currentAskCutTileId);
    }

    /// <summary>
    /// 「选中牌不自动自摸」开启且摸入牌在跳过列表中时，跳过自动自摸（仍可手动和牌）。
    /// </summary>
    public bool ShouldAutoPassForDrawnTile(int drawnTileId) {
        return skipAutoTsumoOnSelected && IsPassTile(drawnTileId);
    }

    private bool IsPassTile(int tileId) {
        return tileId > 0 && passTileIds.Contains(tileId);
    }

    private void WireIfNeeded() {
        if (isWired) return;

        ResolveTileRowsIfNeeded();

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

        WireMeldPassToggle(passChiToggle, value => passChi = value);
        WireMeldPassToggle(passPengToggle, value => passPeng = value);
        WireMeldPassToggle(passMingGangToggle, value => passMingGang = value);
        WireBehaviorToggle(noRonToggle, value => noRon = value);
        WireBehaviorToggle(noTsumoToggle, value => noTsumo = value);
        WireBehaviorToggle(noRobKongToggle, value => noRobKong = value);
        WireBehaviorToggle(skipAutoTsumoOnSelectedToggle, value => skipAutoTsumoOnSelected = value);
        SetSelectAllSilently(skipAutoTsumoOnSelectedToggle, skipAutoTsumoOnSelected);

        isWired = true;
    }

    private void ResolveTileRowsIfNeeded() {
        if (HasAssignedTileRows()) return;
        if (transform.childCount <= FirstTileRowIndex + 3) {
            Debug.LogWarning("TilePassSettingPanel: 子物体不足，需要说明行 + 4 排牌张。");
            return;
        }

        for (int i = 0; i < 4; i++) {
            tileRows[i] = transform.GetChild(FirstTileRowIndex + i);
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

    private void WireMeldPassToggle(Toggle toggle, System.Action<bool> setter) {
        if (toggle == null || setter == null) return;
        toggle.onValueChanged.RemoveAllListeners();
        toggle.onValueChanged.AddListener(isOn => {
            setter(isOn);
            if (!isSyncingMeldPassOptions) {
                OnMeldPassOptionsChanged?.Invoke();
            }
        });
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
