using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

public partial class GameRecordManager {
    private void RestoreSichuanBoardAfterSeek(Round round, int node) {
        if (!SichuanLobby.IsDoubles(ReadGameTitleString(gameRecord.gameTitle, "sub_rule", ""))) return;
        int summaryIndex=round.actionTicks.FindLastIndex(t=>t.Count>2&&t[0]=="liuju"&&t[1]=="board_summary");
        if(summaryIndex>=0) {
            if(node>summaryIndex) {
                var summary=Newtonsoft.Json.JsonConvert.DeserializeObject<SichuanBoardSettlementInfo>(round.actionTicks[summaryIndex][2]);
                if(!summary.dealer_seat.HasValue)summary.dealer_seat=round.dealerIndex;
                RoundEndPresentation.Instance.PresentSichuanBoard(summary,null,true);
            }
            return;
        }
        int last=round.actionTicks.FindLastIndex(t=>t.Count>1&&(t[0]=="liuju"||t[0]=="blood")&&t[1]!="reveal_hu");
        if(last>=0&&node>last)RoundEndPresentation.Instance.PresentSichuanBoard(BuildLegacySichuanBoard(round),null,true);
    }

    // Historical records keep their recorded transfers, including earlier rule profiles.
    // Presentation is grouped without recalculating any historical score.
    private bool TryPresentGroupedSichuanRecord(List<string> tick) {
        if (!SichuanLobby.IsDoubles(ReadGameTitleString(gameRecord.gameTitle, "sub_rule", ""))
            || tick[1] == "reveal_hu"
            || !gameRecord.gameRound.rounds.TryGetValue(currentRoundIndex, out Round round)) return false;
        ApplySichuanLiujuStepState(tick);
        BoardCanvas.Instance.UpdatePlayerScores(recordPlayerList.ToDictionary(p=>p.playerIndex,p=>p.score),indexToPosition);
        TryRefreshRecordScoreTable();
        bool hasSummary=round.actionTicks.Any(t=>t.Count>1&&t[0]=="liuju"&&t[1]=="board_summary");
        int last=round.actionTicks.FindLastIndex(t=>t.Count>1&&(t[0]=="liuju"||t[0]=="blood")&&t[1]!="reveal_hu");
        if(!hasSummary&&currentNode==last) {
            RoundEndPresentation.Instance.PresentSichuanBoard(BuildLegacySichuanBoard(round),null,true);
        } else {
            StartCoroutine(AutoNextActionAfterDelay(0f));
        }
        return true;
    }

    private SichuanBoardSettlementInfo BuildLegacySichuanBoard(Round round) {
        var info=new SichuanBoardSettlementInfo {
            version=0,round=round.currentRound,max_rounds=gameRecord.gameRound.rounds.Count,dealer_seat=round.dealerIndex,
            players=new List<SichuanSettlementPlayer>(),events=new List<SichuanSettlementEvent>(),
            changes=new Dictionary<int,int>(),scores_before=new Dictionary<int,int>(),
            scores_after=new Dictionary<int,int>(),team_changes=new int[2],team_totals=new int[2],
            reason="wall_exhausted"
        };
        var teams=SichuanRoundScoreState.RecordSnapshot(gameRecord,currentRoundIndex)?.team_by_player_index;
        foreach(var p in recordPlayerList.OrderBy(p=>p.playerIndex)) {
            info.players.Add(new SichuanSettlementPlayer {
                seat=p.playerIndex,original_seat=p.originalPlayerIndex,user_id=p.userId,
                username=userIdToUsername.TryGetValue(p.userId,out var name)?name:"选手",
                team_id=teams?[p.playerIndex]??p.playerIndex%2,status=p.isHu?(p.bloodWinZimo?"zimo":"hu"):"unfinished",
                max_fan=-1,win_tile=p.bloodWinTile,
                hand=sichuanRecordRevealHands!=null&&sichuanRecordRevealHands.TryGetValue(p.playerIndex,out var hand)?hand:p.tileList.ToArray(),
                melds=p.combinationMasks.ToArray()
            });
            info.changes[p.playerIndex]=0;info.scores_after[p.playerIndex]=p.score;
        }
        foreach(var tick in round.actionTicks) {
            if(tick.Count<2)continue;
            SichuanSettlementEvent row=null;
            int scoreIndex=-1;
            if(tick[0]=="liuju"||tick[0]=="blood") {
                if(tick[1]=="settle_hu"&&tick.Count>6) {
                    int seat=ParseTickInt(tick,3);bool zimo=tick[2]=="hu_self";
                    row=new SichuanSettlementEvent {kind=zimo?"zimo":"ron",seat=seat,points=ParseTickInt(tick,4),fans=ParseHuFanList(tick,5)};
                    scoreIndex=6;
                } else if(tick[1]=="chajiao"&&tick.Count>5) {
                    int seat=ParseTickInt(tick,2);
                    var player=info.players.FirstOrDefault(p=>p.seat==seat);
                    if(player!=null)player.status=tick[3];
                    row=new SichuanSettlementEvent {kind="chajiao",seat=seat,detail="查叫 / 退税"};scoreIndex=5;
                } else if((tick[1]=="settle_gang"||tick[1]=="cha_refund")&&tick.Count>2) {
                    row=new SichuanSettlementEvent {kind=tick[1]=="settle_gang"?"kong":"refund",seat=-1,detail=tick[1]=="settle_gang"?"杠牌":"退税"};scoreIndex=2;
                }
                if(row!=null) {
                    var changes=ParseTickScoreChanges(tick,scoreIndex);row.changes=new Dictionary<int,int>();
                    if(changes!=null)MapTickScoreChangesToDeltas(changes,row.changes);
                }
            } else if(TryParseGangScoreChangesFromTick(tick,out var changes)&&changes.Values.Any(v=>v!=0)) {
                bool refund=tick[0]=="gr";
                row=new SichuanSettlementEvent {kind=refund?"refund":"kong",seat=-1,detail=refund?"退税":"杠牌",changes=changes};
            } else if(tick[0]=="gr"&&tick.Count>=6&&tick[1]=="gs") {
                var values=Enumerable.Range(2,4).Select(i=>ParseTickInt(tick,i)).ToArray();
                var delta=new Dictionary<int,int>();MapTickScoreChangesToDeltas(values,delta);
                row=new SichuanSettlementEvent {kind="refund",seat=-1,detail="退税",changes=delta};
            }
            if(row==null)continue;
            if(row.kind=="zimo") {
                row.points=row.changes.Values.Where(v=>v<0).Select(v=>-v).DefaultIfEmpty(0).Max();
                row.per_payer=true;
            }
            foreach(var delta in row.changes)info.changes[delta.Key]+=delta.Value;
            info.events.Add(row);
        }
        foreach(var p in info.players) {
            info.scores_before[p.seat]=info.scores_after[p.seat]-info.changes[p.seat];
            info.team_changes[p.team_id]+=info.changes[p.seat];
            info.team_totals[p.team_id]+=info.scores_after[p.seat];
        }
        if(info.players.GroupBy(p=>p.team_id).Any(team=>team.All(p=>p.status=="hu"||p.status=="zimo")))info.reason="team_finished";
        return info;
    }
}
