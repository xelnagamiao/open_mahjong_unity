using System;
using System.Collections.Generic;
using System.Globalization;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public partial class ScoreHistoryPanel {
    private const float TeamScoreCellWidth = 80f;
    private readonly List<GameObject> sichuanTeamHeaders = new();
    private readonly List<Transform> sichuanTeamScoreColumns = new();
    private readonly List<TeamRectState> sichuanTeamRectStates = new();
    private Transform[] sichuanOriginalColumnOrder;
    private GridLayoutGroup teamNamesGrid, teamCountsGrid;
    private bool teamNamesGridEnabled, teamCountsGridEnabled;
    private LayoutElement teamHeaderLayout;
    private float teamHeaderMinWidth, teamHeaderPreferredWidth;
    private bool sichuanTeamLayoutActive;

    private sealed class TeamRectState {
        private readonly RectTransform rect;
        private readonly Vector2 min, max, pivot, size, position;
        public TeamRectState(RectTransform target) {
            rect = target; min = target.anchorMin; max = target.anchorMax;
            pivot = target.pivot; size = target.sizeDelta; position = target.anchoredPosition;
        }
        public void Restore() {
            if (rect == null) return;
            rect.anchorMin = min; rect.anchorMax = max; rect.pivot = pivot;
            rect.sizeDelta = size; rect.anchoredPosition = position;
        }
    }

    private static bool ValidSichuanTeams(IReadOnlyList<int> teams) {
        if (teams == null || teams.Count != 4) return false;
        int blue = 0, red = 0;
        foreach (int team in teams) {
            if (team == 0) blue++;
            else if (team == 1) red++;
            else return false;
        }
        return blue == 2 && red == 2;
    }

    private static int[] ResolveSichuanScoreboardTeams(
        IReadOnlyDictionary<string, PlayerInfoClass> players, bool fromRecord) {
        if (fromRecord) {
            var record = GameRecordManager.Instance?.gameRecord;
            if (record?.gameTitle != null && record.gameTitle.TryGetValue("team_by_player_index", out object raw) && raw != null) {
                try {
                    int[] teams = JArray.FromObject(raw).ToObject<int[]>();
                    if (ValidSichuanTeams(teams)) return teams;
                } catch (JsonException) { } catch (ArgumentException) { }
            }
            if (record?.gameRound?.rounds != null) {
                foreach (var round in record.gameRound.GetRoundsList()) {
                    if (!ScoreHistoryRecordSettlementExtractor.TryGetSichuanBoardSummary(round, out var board) || board.players == null) continue;
                    int[] teams = { -1, -1, -1, -1 };
                    foreach (var player in board.players) {
                        if (player == null || player.original_seat < 0 || player.original_seat >= 4) continue;
                        teams[player.original_seat] = player.team_id;
                    }
                    if (ValidSichuanTeams(teams)) return teams;
                }
            }
            return null;
        }
        var currentTeams = GameSession.Current.SichuanTeamsByPlayerIndex;
        if (!ValidSichuanTeams(currentTeams) || players == null) return null;
        int[] originalTeams = { -1, -1, -1, -1 };
        foreach (var seat in TableMirror.Current.IndexToPosition) {
            if (seat.Key < 0 || seat.Key >= 4 || !players.TryGetValue(seat.Value, out var player)
                || player == null || player.original_player_index < 0 || player.original_player_index >= 4) continue;
            originalTeams[player.original_player_index] = currentTeams[seat.Key];
        }
        return ValidSichuanTeams(originalTeams) ? originalTeams : null;
    }

    private void RestoreSichuanTeamColumns() {
        foreach (var header in sichuanTeamHeaders) if (header != null) header.SetActive(false);
        foreach (var column in sichuanTeamScoreColumns) {
            if (column == null) continue;
            ClearContainer(column);
            column.gameObject.SetActive(false);
        }
        if (!sichuanTeamLayoutActive) return;
        foreach (var state in sichuanTeamRectStates) state.Restore();
        sichuanTeamRectStates.Clear();
        if (teamNamesGrid != null) teamNamesGrid.enabled = teamNamesGridEnabled;
        if (teamCountsGrid != null) teamCountsGrid.enabled = teamCountsGridEnabled;
        if (teamHeaderLayout != null) {
            teamHeaderLayout.minWidth = teamHeaderMinWidth;
            teamHeaderLayout.preferredWidth = teamHeaderPreferredWidth;
        }
        if (sichuanOriginalColumnOrder != null) {
            for (int i = 0; i < sichuanOriginalColumnOrder.Length; i++)
                if (sichuanOriginalColumnOrder[i] != null) sichuanOriginalColumnOrder[i].SetSiblingIndex(i);
        }
        sichuanTeamLayoutActive = false;
    }

    private void ApplySichuanTeamColumns(string subRule, IReadOnlyList<int> teams,
        int[] originalIndices, int[] scores, List<string>[] histories, int roundCount, int predictedCount) {
        if (!SichuanLobby.IsTeamMode(subRule) || !ValidSichuanTeams(teams)) return;
        var names = player0UserName != null ? player0UserName.transform.parent as RectTransform : null;
        var header = names != null ? names.parent as RectTransform : null;
        var counts = header != null ? header.Find("Count") as RectTransform : null;
        var body = player0RoundScoreContainer != null ? player0RoundScoreContainer.parent as RectTransform : null;
        if (names == null || header == null || counts == null || counts.childCount < 8 || body == null) return;
        teamNamesGrid = names.GetComponent<GridLayoutGroup>();
        teamCountsGrid = counts.GetComponent<GridLayoutGroup>();
        if (teamNamesGrid == null || teamCountsGrid == null) return;
        TMP_Text fanHeader = null;
        foreach (Transform child in header)
            if (child.TryGetComponent(out TMP_Text text) && (text.name == "MainFanHeader" || text.text == "主番"
                || text.GetComponent<LocalizedPlatformLabel>()?.Source == "主番")) fanHeader = text;
        if (fanHeader == null) return;
        var order = new List<int>();
        for (int team = 0; team < 2; team++)
            for (int slot = 0; slot < 4; slot++)
                if (originalIndices[slot] >= 0 && originalIndices[slot] < 4 && teams[originalIndices[slot]] == team) order.Add(slot);
        if (order.Count != 4 || new HashSet<int>(originalIndices).Count != 4) return;
        order.Sort((a, b) => {
            int comparison = teams[originalIndices[a]].CompareTo(teams[originalIndices[b]]);
            return comparison != 0 ? comparison : originalIndices[a].CompareTo(originalIndices[b]);
        });

        var nameTexts = new[] { player0UserName, player1UserName, player2UserName, player3UserName };
        var roundColumns = new[] { player0RoundScoreContainer, player1RoundScoreContainer, player2RoundScoreContainer, player3RoundScoreContainer };
        var totalColumns = new[] { player0GameScoreContainer, player1GameScoreContainer, player2GameScoreContainer, player3GameScoreContainer };
        foreach (RectTransform rect in new[] { (RectTransform)transform, header, names, counts, fanHeader.rectTransform })
            sichuanTeamRectStates.Add(new TeamRectState(rect));
        foreach (var text in nameTexts) sichuanTeamRectStates.Add(new TeamRectState(text.rectTransform));
        for (int i = 0; i < 8; i++) sichuanTeamRectStates.Add(new TeamRectState((RectTransform)counts.GetChild(i)));
        sichuanOriginalColumnOrder = new Transform[body.childCount];
        for (int i = 0; i < body.childCount; i++) sichuanOriginalColumnOrder[i] = body.GetChild(i);
        teamNamesGridEnabled = teamNamesGrid.enabled; teamCountsGridEnabled = teamCountsGrid.enabled;
        float playerWidth = teamNamesGrid.cellSize.x, playerScoreWidth = teamCountsGrid.cellSize.x;
        float rowHeight = teamNamesGrid.cellSize.y, extraWidth = TeamScoreCellWidth * 4f;
        teamNamesGrid.enabled = teamCountsGrid.enabled = false;
        teamHeaderLayout = header.GetComponent<LayoutElement>();
        if (teamHeaderLayout != null) {
            teamHeaderMinWidth = teamHeaderLayout.minWidth; teamHeaderPreferredWidth = teamHeaderLayout.preferredWidth;
            if (teamHeaderMinWidth >= 0) teamHeaderLayout.minWidth += extraWidth;
            if (teamHeaderPreferredWidth >= 0) teamHeaderLayout.preferredWidth += extraWidth;
        }
        foreach (RectTransform rect in new[] { (RectTransform)transform, header, names, counts })
            rect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, rect.rect.width + extraWidth);
        fanHeader.rectTransform.anchoredPosition += Vector2.right * extraWidth;
        sichuanTeamLayoutActive = true;

        if (sichuanTeamScoreColumns.Count == 0) {
            for (int i = 0; i < 4; i++) sichuanTeamScoreColumns.Add(CreateTeamScoreColumn(body, player0RoundScoreContainer, i));
            for (int i = 0; i < 6; i++) {
                var cell = CreateCell(header); cell.name = "SichuanTeamHeader" + i;
                sichuanTeamHeaders.Add(cell);
            }
        }
        foreach (var obj in sichuanTeamHeaders) obj.SetActive(true);
        foreach (var column in sichuanTeamScoreColumns) column.gameObject.SetActive(true);
        int sibling = 0;
        RoundIndexContainer.transform.SetSiblingIndex(sibling++);
        for (int team = 0; team < 2; team++) {
            float x = team * (TeamScoreCellWidth * 2 + playerWidth * 2);
            var title = sichuanTeamHeaders[team * 3].GetComponent<TMP_Text>();
            TableText.Set(title, team == 0 ? "蓝队" : "红队");
            title.color = team == 0 ? new Color32(109, 177, 255, 255) : new Color32(255, 111, 111, 255);
            PlaceTeamCell(title.rectTransform, names.anchoredPosition.x + x, 0, TeamScoreCellWidth * 2, rowHeight);
            for (int j = 0; j < 2; j++) {
                var label = sichuanTeamHeaders[team * 3 + j + 1].GetComponent<TMP_Text>();
                TableText.Set(label, j == 0 ? "本副" : "累计");
                PlaceTeamCell(label.rectTransform, names.anchoredPosition.x + x + j * TeamScoreCellWidth, rowHeight, TeamScoreCellWidth, rowHeight);
                sichuanTeamScoreColumns[team * 2 + j].SetSiblingIndex(sibling++);
            }
            for (int member = 0; member < 2; member++) {
                int slot = order[team * 2 + member];
                float playerX = x + TeamScoreCellWidth * 2 + member * playerWidth;
                PlaceTeamCell(nameTexts[slot].rectTransform, playerX, 0, playerWidth, rowHeight);
                for (int j = 0; j < 2; j++)
                    PlaceTeamCell((RectTransform)counts.GetChild(slot * 2 + j), playerX + j * playerScoreWidth, 0, playerScoreWidth, rowHeight);
                roundColumns[slot].SetSiblingIndex(sibling++); totalColumns[slot].SetSiblingIndex(sibling++);
            }
            PopulateTeamScoreCells(team, order[team * 2], order[team * 2 + 1], scores, histories, roundCount, predictedCount);
        }
        MainFanContainer.SetSiblingIndex(sibling);
        LayoutRebuilder.MarkLayoutForRebuild(header.parent as RectTransform);
    }

    private static void PlaceTeamCell(RectTransform rect, float x, float y, float width, float height) {
        rect.SetInsetAndSizeFromParentEdge(RectTransform.Edge.Left, x, width);
        rect.SetInsetAndSizeFromParentEdge(RectTransform.Edge.Top, y, height);
    }

    private static Transform CreateTeamScoreColumn(RectTransform body, Transform source, int index) {
        var obj = new GameObject("SichuanTeamScore" + index, typeof(RectTransform), typeof(GridLayoutGroup), typeof(ContentSizeFitter));
        obj.layer = source.gameObject.layer; obj.transform.SetParent(body, false);
        var grid = obj.GetComponent<GridLayoutGroup>(); var template = source.GetComponent<GridLayoutGroup>();
        grid.cellSize = new Vector2(TeamScoreCellWidth, template.cellSize.y);
        grid.spacing = template.spacing; grid.childAlignment = template.childAlignment;
        grid.startAxis = template.startAxis; grid.startCorner = template.startCorner;
        grid.padding = new RectOffset(template.padding.left, template.padding.right, template.padding.top, template.padding.bottom);
        grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount; grid.constraintCount = 1;
        obj.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.MinSize;
        return obj.transform;
    }

    private void PopulateTeamScoreCells(int team, int first, int second, int[] scores,
        List<string>[] histories, int rounds, int predicted) {
        long[] deltas = new long[rounds]; bool[] known = new bool[rounds];
        long total = (long)scores[first] + scores[second]; bool complete = true;
        for (int row = 0; row < rounds; row++) {
            known[row] = TryTeamScore(histories[first], row, out int a) & TryTeamScore(histories[second], row, out int b);
            deltas[row] = (long)a + b;
            if (known[row]) total -= deltas[row]; else complete = false;
        }
        for (int row = 0; row < rounds + predicted; row++) {
            var delta = CreateCell(sichuanTeamScoreColumns[team * 2]).GetComponent<TMP_Text>();
            var sum = CreateCell(sichuanTeamScoreColumns[team * 2 + 1]).GetComponent<TMP_Text>();
            delta.text = sum.text = "";
            if (row >= rounds || !known[row]) continue;
            long value = deltas[row];
            delta.text = SignedTeamScore(value);
            if (value != 0) delta.color = value > 0 ? scoreGainColor : scoreLossColor;
            total += value;
            if (complete) sum.text = SignedTeamScore(total);
        }
    }

    private static bool TryTeamScore(List<string> history, int row, out int value) {
        value = 0;
        return history != null && row < history.Count && !string.IsNullOrEmpty(history[row])
            && int.TryParse(history[row], NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
    }
    private static string SignedTeamScore(long value) => (value > 0 ? "+" : "") + value.ToString(CultureInfo.InvariantCulture);
}
