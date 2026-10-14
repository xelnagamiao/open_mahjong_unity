using System.Collections.Generic;
using Newtonsoft.Json;

public partial class GameRecordManager {
    internal RuleReplayModule CreateSingaporeReplayModule() => new RuleReplayModule("singapore") {
        ReservedWallTiles=15,
        Reset=ResetSingaporeRecordState,Clear=ResetSingaporeRecordState,
        ApplyBeforeMutation=ApplySingaporeRecordAction,
        InitializePlayers=()=>{foreach(var p in recordPlayerList) p.showHandDrawSlotActive=p.tileList.Count==14;},
        AfterSilentMutation=tick=>SyncRecordBoardScores(),
    };
    private readonly SingaporeHintsCache[] recordSingaporeHints={new SingaporeHintsCache(),new SingaporeHintsCache(),new SingaporeHintsCache(),new SingaporeHintsCache()};
    private bool IsSingaporeRecord()=>RecordRuleManifest?.RuleId==SingaporeGameState.RuleId;
    private void ResetSingaporeRecordState() {foreach(var cache in recordSingaporeHints) cache.Reset();}
    public HashSet<int> SingaporeWaiting(TingpaiQuery query) {
        var result=new HashSet<int>();
        if(!IsSingaporeRecord() || query==null) return result;
        int? seat=query.RecordPlayerIndex;
        if(!seat.HasValue && recordPlayer_to_info.TryGetValue("self",out RecordPlayer self)) seat=self.playerIndex;
        return seat.HasValue && seat.Value>=0 && seat.Value<4 ? recordSingaporeHints[seat.Value].Waiting(query) : result;
    }
    public WaitTileHint DescribeSingaporeWaiting(WaitHintQuery query) {
        if(!IsSingaporeRecord() || query==null) return null;
        int seat=query.Record?.SelfPlayerIndex??query.SelfIndex;
        return seat>=0 && seat<4 ? recordSingaporeHints[seat].Describe(query) : null;
    }
    private bool ApplySingaporeRecordAction(IReadOnlyList<string> tick) {
        if(!IsSingaporeRecord() || tick==null || tick.Count<3 || tick[0]!="singapore") return false;
        if(tick[1]=="hints" && tick.Count>=4 && int.TryParse(tick[2],out int seat) && seat>=0 && seat<4)
            recordSingaporeHints[seat].Accept(JsonConvert.DeserializeObject<SingaporeHints>(tick[3]));
        if(tick[1]=="payment" || tick[1]=="round_start") {
            var scores=JsonConvert.DeserializeObject<int[]>(tick[2]);
            if(scores?.Length==4) foreach(var p in recordPlayerList) {
                p.score=tick[1]=="round_start" ? scores[p.playerIndex] : p.score+scores[p.playerIndex];
                userIdToScore[p.userId]=p.score;
            }
        }
        return true;
    }
}
