using System;
using System.Linq;
using System.Text;
using Newtonsoft.Json.Linq;
using TMPro;
using UnityEngine;

public partial class GameRecordManager {
    private enum InfoPage { Status, Game }
    private InfoPage infoPage;
    private string replayPresentationKey;
    private GameRecord presentedRecord;
    private JObject presentedManifest;
    private string gameInfoDocument;
    private MatchRecordSession.Location comparisonArrival;
    private string comparisonArrivalTable;
    private RecordInfoPopup InfoPopup => infoView ? infoView.GetComponent<RecordInfoPopup>() : null;
    private bool IsPairedMatchReplay => MatchSession != null && (MatchSession.Table("open") != null || MatchSession.Table("closed") != null || MatchSession.IsLiveSession);
    private string ComparisonPhase => comparePhaseDropdown && comparePhaseDropdown.value == 1 ? "before_draw" : "after_draw";

    private void OnInfoLateTick() => RefreshReplayPresentation();

    private string ReplayPlaybackState {
        get {
            if (IsSpectating && IsLiveSpectatorMode && waitingForMoreTicks) return "等待追加";
            if (BlocksRecordNavigation) return "等待结算确认";
            if (gameRecord?.gameRound?.rounds != null && gameRecord.gameRound.rounds.TryGetValue(currentRoundIndex, out var round)
                && currentNode >= round.actionTicks.Count && currentRoundIndex == gameRecord.gameRound.rounds.Keys.Max())
                return IsSpectating ? "等待追加" : "播放完毕";
            return IsRecordAutoPlaying || (IsSpectating && IsLiveSpectatorMode && !PauseAutoPlay) ? "播放中" : "已暂停";
        }
    }

    public string ReplayStatusText => BuildReplayStatus();
    private void RefreshReplayPresentation(bool force = false) {
        if (loadingMatchTable || gameRecord?.gameRound?.rounds == null) return;
        int count = gameRecord.gameRound.rounds.TryGetValue(currentRoundIndex, out var activeRound) ? activeRound.actionTicks.Count : 0;
        string key = $"{currentRoundIndex}:{currentNode}:{selectedPlayerOriginalIndex}:{ReplayPlaybackState}:{MatchSession?.ActiveTable}:{MatchSession?.Mode}:{ComparisonPhase}:{gameRecord.gameRound.rounds.Count}:{count}";
        bool newDocument = presentedRecord != gameRecord || presentedManifest != MatchSession?.Manifest;
        if (!force && !newDocument && key == replayPresentationKey) return;
        replayPresentationKey = key; presentedRecord = gameRecord; presentedManifest = MatchSession?.Manifest;
        if (newDocument) gameInfoDocument = null;
        if (MatchSession != null) {
            MatchSession.Remember(currentRoundIndex, currentNode, selectedPlayerOriginalIndex);
            UpdateMatchControls();
            if (IsPairedMatchReplay && !MatchSession.IsLiveSession) GameCanvas.Instance?.UpdateMatchReplayScores(MatchReplayProjection.ScoreAt(MatchSession, currentRoundIndex, currentNode));
        }
        if (infoView && infoView.activeSelf) {
            if (infoPage == InfoPage.Status) InfoPopup?.PresentStatus(BuildReplayStatus());
            else if (newDocument || gameInfoDocument == null) InfoPopup?.PresentGame(gameInfoDocument = BuildGameInfoString());
        }
    }

    private void ToggleInfoPage(InfoPage page) {
        bool close = infoView.activeSelf && infoPage == page;
        if (close) { infoView.SetActive(false); return; }
        infoPage = page;
        if (page == InfoPage.Status) InfoPopup?.PresentStatus(BuildReplayStatus());
        else InfoPopup?.PresentGame(gameInfoDocument ??= BuildGameInfoString());
        infoView.SetActive(true);
    }

    private string BuildReplayStatus() {
        if (gameRecord?.gameRound?.rounds == null || !gameRecord.gameRound.rounds.TryGetValue(currentRoundIndex, out var round)) return "暂无阅览状态";
        string fallbackName = userIdToUsername.TryGetValue(selectedPlayerUserid, out string value) ? value : "未记录";
        string name = IsPairedMatchReplay ? MatchReplayProjection.PlayerName(MatchSession.Table(MatchSession.ActiveTable), selectedPlayerOriginalIndex)
            : ReadGameTitleString(gameRecord.gameTitle, $"p{selectedPlayerOriginalIndex}_name", fallbackName);
        string seat = SeatName(selectedPlayerOriginalIndex);
        string table = !IsPairedMatchReplay ? "" : (MatchSession.ActiveTable == "closed" ? "闭室" : "开室") + " · ";
        int turn = Math.Max(0, xunmuNodeList.Count(n => n <= currentNode) - 1);
        var sb = new StringBuilder();
        sb.AppendLine(table + ReplayPlaybackState);
        sb.AppendLine($"视角：{seat} · {name}");
        string unit = ReadGameTitleString(gameRecord.gameTitle, "rule", "") == "sichuan" ? "副" : "局";
        sb.AppendLine($"第 {currentRoundIndex}/{gameRecord.gameRound.rounds.Count} {unit} · {turn} 巡 · {currentNode}/{round.actionTicks.Count} 步");
        sb.AppendLine($"剩余牌山：{GetRecordRemainTiles()} 张");
        var tick = currentNode > 0 && currentNode <= round.actionTicks.Count ? round.actionTicks[currentNode - 1] : null;
        var metadata = MatchSession?.Table(MatchSession.ActiveTable)?["round_meta"]?["round_index_" + currentRoundIndex]?["action_meta"] as JArray;
        int? actor = currentNode > 0 && currentNode <= metadata?.Count ? (int?)metadata[currentNode - 1]["actor_original_seat"] : null;
        sb.AppendLine("最近动作：" + (actor.HasValue ? SeatName(actor.Value) + " · " : "") + (tick == null ? "起手" : ReplayActionName(tick.FirstOrDefault())));
        if (IsPairedMatchReplay) {
            bool compare = MatchSession.Mode == MatchRecordSession.PlaybackMode.Compare;
            sb.AppendLine(compare ? "切换到同牌山位置 · " + (ComparisonPhase == "before_draw" ? "摸牌前" : "摸牌后") : "独立阅览进度");
            var origin = MatchSession.AnchorAt(currentRoundIndex, currentNode, ComparisonPhase, selectedPlayerOriginalIndex);
            if (origin == null) sb.Append("当前视角尚无可验证的摸牌位置");
            else {
                string wall = (string)origin["zone"] == "common_wall" ? "公共牌山" : SeatName((int?)origin["source_original_seat"] ?? -1) + "牌山";
                sb.Append($"视角定位：{wall} · 第 {((int?)origin["offset"] ?? 0) + 1} 张");
            }
            if (comparisonArrival?.Exact == true && comparisonArrivalTable == MatchSession.ActiveTable
                && comparisonArrival.RoundKey == "round_index_" + currentRoundIndex && comparisonArrival.Node == currentNode)
                sb.Append("\n此牌摸牌人：" + SeatName(comparisonArrival.ActorOriginalSeat ?? -1));
        }
        return sb.ToString().TrimEnd();
    }

    private static string SeatName(int seat) => seat >= 0 && seat < 4 ? "东南西北"[seat] + "位" : "未选择";
    private static string ReplayActionName(string action) {
        switch (action) {
            case "d": case "nd": case "gd": case "bd": return "摸牌";
            case "c": return "打牌";
            case "p": return "碰牌";
            case "cl": case "cm": case "cr": case "ca": return "副露";
            case "g": case "ag": case "jg": return "杠牌";
            case "hu_self": case "hu_first": case "hu_second": case "hu_third": case "hu_riichi": return "和牌";
            case "liuju": case "blood": case "ryuukyoku": case "shuhewei": return "结算";
            case "end": return "本局结束";
            case "riichi": return "立直";
            case "bh": return "补花";
            case "dq": return "定缺";
            default: return "状态更新";
        }
    }

    private void AppendMatchPlayers(StringBuilder sb) {
        foreach (var id in new[] { "open", "closed" }) {
            sb.AppendLine(id == "open" ? "开室" : "闭室");
            var table = MatchSession.Table(id);
            for (int seat = 0; seat < 4; seat++) {
                string uid = (string)MatchReplayProjection.Player(table, seat)?["user_id"];
                if (string.IsNullOrWhiteSpace(uid)) uid = (string)(table?["payload"] as JObject)?["game_title"]?[$"p{seat}_uid"];
                sb.AppendLine($"{"东南西北"[seat]}: {MatchReplayProjection.PlayerName(table, seat)} (UID:{(string.IsNullOrWhiteSpace(uid) ? "未记录" : uid)})");
            }
        }
    }
}
