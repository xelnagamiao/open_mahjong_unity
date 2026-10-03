using System;
using System.Collections.Generic;
using Newtonsoft.Json;

public partial class GameRecordManager {
    private HangzhouInfo recordHangzhouInfo;
    private string recordHangzhouWinSource;
    private readonly HangzhouHintsCache[] recordHangzhouHints = { new HangzhouHintsCache(),new HangzhouHintsCache(),new HangzhouHintsCache(),new HangzhouHintsCache() };
    private bool IsHangzhouRecord() => RecordRuleManifest?.RuleId == HangzhouGameState.RuleId;
    private bool IsHangzhouRecordTenWinds => IsHangzhouRecord() && recordHangzhouWinSource == "ten_winds";
    private void InitializeHangzhouRecordDrawSlots() {
        if (!IsHangzhouRecord() || !gameRecord.gameRound.rounds.TryGetValue(currentRoundIndex,out Round round)) return;
        foreach(var player in recordPlayerList)
            player.showHandDrawSlotActive=player.playerIndex==round.startPlayerIndex && player.tileList.Count==14;
    }
    private void ResetHangzhouRecordState() {
        recordHangzhouInfo=null;recordHangzhouWinSource=null;
        foreach(var cache in recordHangzhouHints) cache.Reset();
        if (IsHangzhouRecord() && gameRecord.gameRound.rounds.TryGetValue(currentRoundIndex,out Round round))
            recordHangzhouInfo=round.hangzhou==null ? null : JsonConvert.DeserializeObject<HangzhouInfo>(JsonConvert.SerializeObject(round.hangzhou));
        HangzhouStatePanel.Show(recordHangzhouInfo);
    }
    public HashSet<int> HangzhouWaiting(TingpaiQuery query) {
        if (!IsHangzhouRecord()) return new HashSet<int>();
        int? seat=query?.RecordPlayerIndex;
        if (!seat.HasValue && recordPlayer_to_info.TryGetValue("self",out RecordPlayer self)) seat=self.playerIndex;
        return seat.HasValue && seat.Value>=0 && seat.Value<4 ? recordHangzhouHints[seat.Value].Waiting(query) : new HashSet<int>();
    }
    private void PlayHangzhouRecordAnnouncement(IReadOnlyList<string> tick,string position,int voiceId) {
        if (!IsHangzhouRecord() || tick==null || tick.Count<2 || tick[0]!="c" || (tick.Count>3 && tick[3]=="C")
            || !int.TryParse(tick[1],out int tile) || tile!=HangzhouGameState.Joker) return;
        SoundManager.Instance?.PlayActionSound(position,"hangzhou_piao",voiceId);
        GameCanvas.Instance?.ShowActionDisplay(position,"hangzhou_piao");
    }
    private bool ApplyHangzhouRecordAction(IReadOnlyList<string> tick) {
        if (!IsHangzhouRecord() || tick == null || tick.Count<3 || tick[0]!="hangzhou") return false;
        switch(tick[1]) {
            case "hints":
                if (tick.Count>=4 && int.TryParse(tick[2],out int seat) && seat>=0 && seat<4)
                    recordHangzhouHints[seat].Accept(JsonConvert.DeserializeObject<HangzhouHints>(tick[3]));
                break;
            case "win_source":
                if (tick.Count>=6) {
                    recordHangzhouWinSource=tick[3];
                    recordHangzhouInfo=JsonConvert.DeserializeObject<HangzhouInfo>(tick[5]);
                    if (recordHangzhouWinSource=="ten_winds") { lastWinnableTileId=-1;lastJiagangPlayerIndex=-1; }
                }
                break;
            case "tail_burn":
                // The burn is recorded before gd: remove the upper penultimate
                // tile and keep the lower last tile for the following replacement.
                if (currentTilesList.Count>1) {
                    int index=currentTilesList.Count-2;
                    int original=currentOriginalIndices[index];
                    currentTilesList.RemoveAt(index);currentOriginalIndices.RemoveAt(index);consumedBackIndices.Add(original);
                }
                break;
            case "state":
                recordHangzhouInfo=JsonConvert.DeserializeObject<HangzhouInfo>(tick[2]);
                if (tick.Count>=4) {
                    var scores=JsonConvert.DeserializeObject<int[]>(tick[3]);
                    if (scores?.Length==4) foreach(var player in recordPlayerList) {
                        player.score=scores[player.playerIndex];userIdToScore[player.userId]=player.score;
                    }
                }
                break;
        }
        HangzhouStatePanel.Show(recordHangzhouInfo);
        return true;
    }
}
