using System;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public partial class GameRecordManager {
    [SerializeField] private GameObject matchTableControls;
    [SerializeField] private Button goOpenTableButton;
    [SerializeField] private Button goClosedTableButton;
    [SerializeField] private Toggle compareWallToggle;
    [SerializeField] private Button independentProgressButton;
    [SerializeField] private TMP_Dropdown comparePhaseDropdown;
    [SerializeField] private TMP_Text matchTableStatus;
    public MatchRecordSession MatchSession { get; private set; }
    private bool loadingMatchTable;
    private JObject legacyMatchCursorMap;
    private string renderedMatchEpoch;
    private bool renderedMatchFinal;
    private bool matchCloudSaved;
    private string matchGameId;

    public void LoadMatchRecord(JObject record, string tableId, JObject cursorMap = null, bool cloudSaved = false, string gameId = null) {
        if (infoView) infoView.SetActive(false);
        InfoPopup?.ResetDocument();
        gameInfoDocument = null;
        comparisonArrival = null;
        legacyMatchCursorMap = cursorMap;
        matchCloudSaved = cloudSaved;
        matchGameId = gameId ?? MatchRecordCodec.GameId(record);
        var session = MatchRecordSession.FromRecord(record, tableId);
        session.Mode = (string)record["match_format"] == "paired_tables_2v2"
            ? MatchRecordSession.PlaybackMode.Compare : MatchRecordSession.PlaybackMode.History;
        LoadMatchSession(session);
    }

    public void LoadMatchSession(MatchRecordSession session) {
        MatchSession = session;
        IsSpectating = session.IsLiveSession;
        if (goOpenTableButton != null) { goOpenTableButton.onClick.RemoveAllListeners(); goOpenTableButton.onClick.AddListener(() => SwitchMatchTable("open")); }
        if (goClosedTableButton != null) { goClosedTableButton.onClick.RemoveAllListeners(); goClosedTableButton.onClick.AddListener(() => SwitchMatchTable("closed")); }
        if (compareWallToggle != null) {
            compareWallToggle.SetIsOnWithoutNotify(session.Mode == MatchRecordSession.PlaybackMode.Compare);
            compareWallToggle.onValueChanged.RemoveAllListeners();
            compareWallToggle.onValueChanged.AddListener(SetMatchReplayMode);
        }
        if (independentProgressButton != null) {
            independentProgressButton.onClick.RemoveAllListeners();
            independentProgressButton.onClick.AddListener(() => SetMatchReplayMode(false));
        }
        if (comparePhaseDropdown != null) {
            comparePhaseDropdown.onValueChanged.RemoveAllListeners();
            comparePhaseDropdown.onValueChanged.AddListener(_ => RefreshReplayPresentation(true));
        }
        RenderMatchTable(session.Position(session.ActiveTable, session.Mode == MatchRecordSession.PlaybackMode.Live));
    }

    private void SetMatchReplayMode(bool compare) {
        if (MatchSession == null) return;
        var mode = compare ? MatchRecordSession.PlaybackMode.Compare : MatchRecordSession.PlaybackMode.History;
        if (MatchSession.Mode == mode) return;
        StopRecordAutoPlay();
        var cursor = MatchSession.ChangeMode(mode, currentRoundIndex, currentNode, selectedPlayerOriginalIndex);
        if (IsSpectating) SetSpectatorModeFlags(false);
        if (!compare) RenderMatchTable(cursor);
        UpdateMatchControls();
        RefreshReplayPresentation(true);
    }

    /// <summary>Detach a reconnecting view while NetworkManager retains the match and its cursors.</summary>
    public void SuspendMatchRecordView() {
        IsSpectating = false;
        HideGameRecord();
    }

    public void ClearMatchRecord() {
        MatchSession = null; legacyMatchCursorMap = null; matchCloudSaved = false; matchGameId = null;
        if (matchTableControls != null) matchTableControls.SetActive(false);
    }

    private void RenderMatchTable(MatchRecordSession.Cursor cursor) {
        if (cursor == null || MatchSession == null) return;
        var table = MatchSession.Table(MatchSession.ActiveTable);
        string json = table["payload"].ToString(Formatting.None);
        bool live = MatchSession.Mode == MatchRecordSession.PlaybackMode.Live;
        int originalSeat = cursor.ViewSeat >= 0 ? cursor.ViewSeat : selectedPlayerOriginalIndex;
        bool infoOpen = infoView && infoView.activeSelf;
        loadingMatchTable = true;
        try {
            var players = table["players"]?.ToObject<PlayerRecordInfo[]>();
            if (players == null || players.Length == 0) players = ExtractPlayersSettings(json);
            LoadRecord(json, players, IsSpectating || !matchCloudSaved ? null : matchGameId);
            if (originalSeat >= 0) selectedPlayerOriginalIndex = originalSeat;
            GotoSelectRound((int)table["round_meta"][cursor.RoundKey]["round_index"], false);
            GotoAction(cursor.Node);
        } finally { loadingMatchTable = false; }
        if (IsSpectating) {
            ShowSpectatorModePanel(true); BindSpectatorButtons(); SetSpectatorModeFlags(live);
            if (autoPlayCoroutine != null) StopCoroutine(autoPlayCoroutine);
            autoPlayCoroutine = StartCoroutine(AutoPlayCoroutine());
        }
        renderedMatchEpoch = MatchSession.Epoch(MatchSession.ActiveTable);
        renderedMatchFinal = (string)table["record_state"] == "complete";
        if (infoOpen) infoView.SetActive(true);
        UpdateMatchControls();
        RefreshReplayPresentation(true);
    }

    public void SwitchMatchTable(string target) {
        if (MatchSession == null || target == MatchSession.ActiveTable) return;
        StopRecordAutoPlay();
        bool compare = compareWallToggle != null && compareWallToggle.isOn;
        string phase = comparePhaseDropdown != null && comparePhaseDropdown.value == 1 ? "before_draw" : "after_draw";
        var result = MatchSession.Switch(target, currentRoundIndex, currentNode, compare, phase, selectedPlayerOriginalIndex);
        if (!result.Exact) {
            string message = MatchRecordSession.Explain(result.Status);
            if (matchTableStatus != null) TableText.Set(matchTableStatus, message);
            GameHost.Current.ShowTip("切桌", false, message);
            return;
        }
        comparisonArrival = compare ? result : null;
        comparisonArrivalTable = target;
        RenderMatchTable(new MatchRecordSession.Cursor(result.RoundKey, result.Node, result.ViewSeat));
    }

    public void RefreshMatchStream(string streamId) {
        if (MatchSession == null) return;
        if (streamId == MatchSession.ActiveTable && MatchSession.Ready(streamId)) {
            // Retain the current replay object and animations for append-only updates.
            // Snapshots after an epoch reset may replace a board; those require a rebuild.
            var table = MatchSession.Table(streamId);
            var rounds = (JObject)table["payload"]["game_round"];
            var updates = new JArray();
            bool replacedEpoch = renderedMatchEpoch != MatchSession.Epoch(streamId);
            bool rebuild = gameRecord == null || replacedEpoch || (!renderedMatchFinal && (string)table["record_state"] == "complete");
            foreach (var round in rounds.Properties()) {
                int index = (int)table["round_meta"][round.Name]["round_index"];
                var ticks = (JArray)round.Value["action_ticks"];
                if (gameRecord != null && gameRecord.gameRound.rounds.TryGetValue(index, out Round old)) {
                    if (old.actionTicks.Count > ticks.Count) { rebuild = true; break; }
                    // Comparing the already displayed prefix also catches a restarted board.
                    for (int i = 0; i < old.actionTicks.Count; i++) {
                        if (!old.actionTicks[i].SequenceEqual(ConvertTickArrayToList((JArray)ticks[i]))) { rebuild = true; break; }
                    }
                    if (rebuild) break;
                    if (old.actionTicks.Count < ticks.Count) updates.Add(new JObject {
                        ["type"] = "ticks", ["round_index"] = index,
                        ["new_ticks"] = new JArray(ticks.Skip(old.actionTicks.Count).Select(t => t.DeepClone())) });
                } else updates.Add(new JObject { ["type"] = "new_round", ["round_index"] = index, ["round_data"] = round.Value.DeepClone() });
            }
            if (rebuild) {
                if (!replacedEpoch) MatchSession.Remember(currentRoundIndex, currentNode, selectedPlayerOriginalIndex);
                RenderMatchTable(MatchSession.Position(streamId, IsLiveSpectatorMode));
            }
            else if (updates.Count > 0) AppendSpectatorTicks(updates.ToString(Formatting.None));
        }
        if (IsSpectating && MatchSession.FinalStreamsApplied) {
            int round = currentRoundIndex, node = currentNode;
            string active = MatchSession.ActiveTable;
            var completeRecord = MatchSession.ExportRecord();
            var finished = MatchRecordSession.FromRecord(completeRecord, active);
            finished.Mode = MatchSession.Mode == MatchRecordSession.PlaybackMode.Compare
                ? MatchRecordSession.PlaybackMode.Compare : MatchRecordSession.PlaybackMode.History;
            finished.RestorePlaybackFrom(MatchSession);
            finished.Remember(round, node, selectedPlayerOriginalIndex);
            NetworkManager.Instance?.LeaveMatchSpectator();
            if (autoPlayCoroutine != null) { StopCoroutine(autoPlayCoroutine); autoPlayCoroutine = null; }
            IsSpectating = false;
            matchCloudSaved = finished.TableIds.Any(id => !string.IsNullOrEmpty((string)finished.Table(id)["table_record_id"]));
            matchGameId = matchCloudSaved ? MatchRecordCodec.GameId(completeRecord) : null;
            ShowSpectatorModePanel(false);
            LoadMatchSession(finished);
        }
        gameInfoDocument = null;
        UpdateMatchControls();
        RefreshReplayPresentation(true);
    }

    private void UpdateMatchControls() {
        bool paired = MatchSession != null && (MatchSession.Table("open") != null || MatchSession.Table("closed") != null || MatchSession.IsLiveSession);
        if (matchTableControls != null) matchTableControls.SetActive(paired);
        if (!paired) return;
        if (goOpenTableButton != null) goOpenTableButton.interactable = MatchSession.ActiveTable != "open" && MatchSession.Ready("open");
        if (goClosedTableButton != null) goClosedTableButton.interactable = MatchSession.ActiveTable != "closed" && MatchSession.Ready("closed");
        bool compare = MatchSession.Mode == MatchRecordSession.PlaybackMode.Compare;
        if (compareWallToggle != null) compareWallToggle.SetIsOnWithoutNotify(compare);
        if (comparePhaseDropdown != null) comparePhaseDropdown.gameObject.SetActive(compare);
        if (independentProgressButton != null) {
            var image = independentProgressButton.GetComponent<Image>();
            if (image) image.color = !compare && MatchSession.Mode != MatchRecordSession.PlaybackMode.Live
                ? new Color32(59, 108, 146, 255) : new Color32(62, 74, 84, 255);
        }
        if (matchTableStatus != null) {
            string table = MatchSession.ActiveTable == "open" ? "开室" : "闭室";
            TableText.Set(matchTableStatus, table + " · " + currentRoundIndex + " 副\n" + ReplayPlaybackState);
        }
    }

    public int MapLegacyMatchNode(int round, int node) {
        var map = legacyMatchCursorMap?["node_map"]?["round_index_" + round] as JArray;
        return map != null && node >= 0 && node < map.Count ? (int)map[node] : node;
    }

}
