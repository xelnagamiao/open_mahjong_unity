using System;
using System.Linq;
using System.Collections;
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
    [SerializeField] private Button collapseMatchControlsButton;
    private bool matchControlsCollapsed;
    private float expandedMatchControlsWidth;
    public MatchRecordSession MatchSession { get; private set; }
    private bool loadingMatchTable;
    private JObject legacyMatchCursorMap;
    private string renderedMatchEpoch;
    private bool renderedMatchFinal;
    private bool matchCloudSaved;
    private string matchGameId;

    public void LoadMatchRecord(JObject record, string tableId, JObject cursorMap = null, bool cloudSaved = false, string gameId = null) {
        ReplayFrameLoader.Finish(LoadArchive(record, tableId, cursorMap, cloudSaved, gameId, copyTables: true));
    }

    internal IEnumerable LoadArchive(JObject record, string tableId, JObject cursorMap, bool cloudSaved, string gameId, SharedRecordLink.Position jump = null, bool copyTables = false) {
        if (infoView) infoView.SetActive(false);
        InfoPopup?.ResetDocument();
        gameInfoDocument = null;
        comparisonArrival = null;
        legacyMatchCursorMap = cursorMap;
        matchCloudSaved = cloudSaved;
        matchGameId = gameId ?? MatchRecordCodec.GameId(record);
        var session = MatchRecordSession.OpenArchive(record, tableId, copyTables);
        session.Mode = (string)record["match_format"] == "paired_tables_2v2"
            ? MatchRecordSession.PlaybackMode.Compare : MatchRecordSession.PlaybackMode.History;
        yield return null;
        BindMatchSession(session);
        foreach (var step in PrepareMatchTable(session.Position(session.ActiveTable), jump)) yield return step;
    }

    public void LoadMatchSession(MatchRecordSession session) {
        BindMatchSession(session);
        RenderMatchTable(session.Position(session.ActiveTable, session.Mode == MatchRecordSession.PlaybackMode.Live));
    }

    private void BindMatchSession(MatchRecordSession session) {
        MatchSession = session;
        IsSpectating = session.IsLiveSession;
        if (goOpenTableButton != null) { goOpenTableButton.onClick.RemoveAllListeners(); goOpenTableButton.onClick.AddListener(() => RequestMatchTable("open")); }
        if (goClosedTableButton != null) { goClosedTableButton.onClick.RemoveAllListeners(); goClosedTableButton.onClick.AddListener(() => RequestMatchTable("closed")); }
        if (compareWallToggle != null) {
            compareWallToggle.SetIsOnWithoutNotify(session.Mode == MatchRecordSession.PlaybackMode.Compare);
            compareWallToggle.onValueChanged.RemoveAllListeners();
            compareWallToggle.onValueChanged.AddListener(SetMatchReplayMode);
        }
        if (independentProgressButton != null) {
            independentProgressButton.onClick.RemoveAllListeners();
            independentProgressButton.onClick.AddListener(() => SetMatchReplayMode(false));
        }
        if (collapseMatchControlsButton != null) {
            collapseMatchControlsButton.onClick.RemoveListener(ToggleMatchControls);
            collapseMatchControlsButton.onClick.AddListener(ToggleMatchControls);
        }
        if (comparePhaseDropdown != null) {
            comparePhaseDropdown.onValueChanged.RemoveAllListeners();
            comparePhaseDropdown.onValueChanged.AddListener(_ => RefreshReplayPresentation(true));
        }
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

    private void RenderMatchTable(MatchRecordSession.Cursor cursor) => ReplayFrameLoader.Finish(PrepareMatchTable(cursor));

    private IEnumerable PrepareMatchTable(MatchRecordSession.Cursor cursor, SharedRecordLink.Position jump = null) {
        if (cursor == null || MatchSession == null) yield break;
        var table = MatchSession.Table(MatchSession.ActiveTable);
        var payload = (JObject)table["payload"];
        bool live = MatchSession.Mode == MatchRecordSession.PlaybackMode.Live;
        int originalSeat = cursor.ViewSeat >= 0 ? cursor.ViewSeat : selectedPlayerOriginalIndex;
        bool infoOpen = infoView && infoView.activeSelf;
        loadingMatchTable = true;
        try {
            var players = table["players"]?.ToObject<PlayerRecordInfo[]>();
            if (players == null || players.Length == 0) players = ExtractPlayerArray(payload);
            var decoded = new GameRecord();
            foreach (var step in GameRecordJsonDecoder.DecodeRounds(payload, decoded)) yield return step;
            int roundIndex = (int)table["round_meta"][cursor.RoundKey]["round_index"];
            int node = cursor.Node;
            if (jump != null) {
                var rounds = decoded.gameRound.rounds;
                if (rounds.ContainsKey(jump.Round)) roundIndex = jump.Round;
                else if (jump.Round >= 1 && jump.Round <= rounds.Count)
                    roundIndex = decoded.gameRound.GetRoundsList()[jump.Round - 1].roundIndex;
                node = jump.Legacy ? MapLegacyMatchNode(jump.Round, jump.Node) : jump.Node;
                int count = MahjongPlayerCount.ForSubRule(ReadGameTitleString(decoded.gameTitle, "sub_rule", ""));
                var seats = Enumerable.Range(0, count);
                bool matches(int seat) => !jump.UserId.HasValue || ReadGameTitleInt(decoded.gameTitle, $"p{seat}_uid") == jump.UserId;
                if (jump.Seat.HasValue && jump.Seat >= 0 && jump.Seat < count && matches(jump.Seat.Value))
                    originalSeat = jump.Seat.Value;
                else if (jump.UserId > 0) {
                    var matching = seats.Where(matches).ToArray();
                    if (matching.Length == 1) originalSeat = matching[0];
                }
            }
            foreach (var step in LoadRecordAt(decoded, players, IsSpectating || !matchCloudSaved ? null : matchGameId,
                roundIndex, originalSeat, node)) yield return step;
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

    public void SwitchMatchTable(string target) => ReplayFrameLoader.Finish(SwitchTableSteps(target));

    public void RequestMatchTable(string target) {
        if (MatchSession == null || MatchSession.ActiveTable == target || ReplayFrameLoader.Busy) return;
        if (comparePhaseDropdown != null && comparePhaseDropdown.IsExpanded) comparePhaseDropdown.Hide();
        if (IsSpectating) { SwitchMatchTable(target); return; }
        ReplayFrameLoader.Begin(SwitchTableSteps(target), error => {
            Debug.LogException(error);
            QuitRecord();
            GameHost.Current.ShowTip("牌谱", false, error.Message);
        });
    }

    private IEnumerable SwitchTableSteps(string target) {
        if (MatchSession == null || target == MatchSession.ActiveTable) yield break;
        StopRecordAutoPlay();
        bool compare = compareWallToggle != null && compareWallToggle.isOn;
        string phase = comparePhaseDropdown != null && comparePhaseDropdown.value == 1 ? "before_draw" : "after_draw";
        var result = MatchSession.Switch(target, currentRoundIndex, currentNode, compare, phase, selectedPlayerOriginalIndex);
        if (!result.Exact) {
            string message = MatchRecordSession.Explain(result.Status);
            GameHost.Current.ShowTip("切桌", false, message);
            yield break;
        }
        comparisonArrival = compare ? result : null;
        comparisonArrivalTable = target;
        foreach (var step in PrepareMatchTable(new MatchRecordSession.Cursor(result.RoundKey, result.Node, result.ViewSeat))) yield return step;
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

    private void ToggleMatchControls() {
        var bar = (RectTransform)matchTableControls.transform;
        var button = (RectTransform)collapseMatchControlsButton.transform;
        if (!matchControlsCollapsed) expandedMatchControlsWidth = bar.rect.width;
        matchControlsCollapsed = !matchControlsCollapsed;
        float width = matchControlsCollapsed ? -2f * button.anchoredPosition.x : expandedMatchControlsWidth;
        bar.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, width);
        goOpenTableButton.gameObject.SetActive(!matchControlsCollapsed);
        goClosedTableButton.gameObject.SetActive(!matchControlsCollapsed);
        compareWallToggle.gameObject.SetActive(!matchControlsCollapsed);
        independentProgressButton.gameObject.SetActive(!matchControlsCollapsed);
        TableText.Set(collapseMatchControlsButton.GetComponentInChildren<TMP_Text>(), matchControlsCollapsed ? "展开" : "收起");
        UpdateMatchControls();
    }

    private void UpdateMatchControls() {
        bool paired = MatchSession != null && (MatchSession.Table("open") != null || MatchSession.Table("closed") != null || MatchSession.IsLiveSession);
        if (matchTableControls != null) matchTableControls.SetActive(paired);
        if (!paired) return;
        if (goOpenTableButton != null) goOpenTableButton.interactable = MatchSession.ActiveTable != "open" && MatchSession.Ready("open");
        if (goClosedTableButton != null) goClosedTableButton.interactable = MatchSession.ActiveTable != "closed" && MatchSession.Ready("closed");
        bool compare = MatchSession.Mode == MatchRecordSession.PlaybackMode.Compare;
        if (compareWallToggle != null) compareWallToggle.SetIsOnWithoutNotify(compare);
        if (comparePhaseDropdown != null) comparePhaseDropdown.gameObject.SetActive(compare && !matchControlsCollapsed);
        if (independentProgressButton != null) {
            var image = independentProgressButton.GetComponent<Image>();
            if (image) image.color = !compare && MatchSession.Mode != MatchRecordSession.PlaybackMode.Live
                ? new Color32(59, 108, 146, 255) : new Color32(62, 74, 84, 255);
        }
    }

    public int MapLegacyMatchNode(int round, int node) {
        var map = legacyMatchCursorMap?["node_map"]?["round_index_" + round] as JArray;
        return map != null && node >= 0 && node < map.Count ? (int)map[node] : node;
    }

}
