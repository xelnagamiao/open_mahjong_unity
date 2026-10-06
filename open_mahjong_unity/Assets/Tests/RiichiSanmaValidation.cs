#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using Riichi;
using UnityEngine;

/// <summary>三人立直本地计算与协议回归；在 Editor 或开发客户端中运行。</summary>
public static class RiichiSanmaValidation {
    public static object Run() {
        var checks = new List<string>();
        void Check(bool condition, string name) {
            if (!condition) throw new InvalidOperationException("Sanma: " + name);
            checks.Add(name);
        }
        var catalog = JObject.Parse(Resources.Load<TextAsset>("RiichiRuleOptions").text);
        Check(RiichiLobby.SubRules.Any(rule => rule.Key == "riichi/sanma"), "riichi subrule registered");
        Check(catalog["presets"].Count() == 6 && catalog["options"].Count() == 40, "complete catalogue");
        foreach (int index in new[] {4,5}) {
            Check(RiichiRoomRules.Presets[index].SubRule == "riichi/sanma", "preset subrule " + index);
            Check((int)RiichiRoomRules.Presets[index].Room["starting_score"] == 35000, "starting score " + index);
        }
        Check(RiichiRoundText.SanmaName(3) == "东三局" && RiichiRoundText.SanmaName(4) == "南一局"
            && RiichiRoundText.SanmaName(7) == "西一局" && RiichiRoundText.SanmaName(12) == "北三局", "three hands per wind");
        var scoreboardPlayers = new Dictionary<string,PlayerInfoClass> {
            ["self"]=new PlayerInfoClass {original_player_index=2},
            ["right"]=new PlayerInfoClass {original_player_index=0},
            ["left"]=new PlayerInfoClass {original_player_index=1},
            ["top"]=new PlayerInfoClass {original_player_index=0},
        };
        Check(ScoreHistoryPanel.SelectScoreboardPlayers(scoreboardPlayers,true).Select(p=>p.original_player_index)
            .SequenceEqual(new[] {0,1,2}),"live scoreboard omits empty top seat");
        scoreboardPlayers.Remove("top");
        Check(ScoreHistoryPanel.SelectScoreboardPlayers(scoreboardPlayers,true).Count==3,"record scoreboard accepts three entries");
        scoreboardPlayers["top"]=new PlayerInfoClass {original_player_index=3};
        Check(ScoreHistoryPanel.SelectScoreboardPlayers(scoreboardPlayers,false).Select(p=>p.original_player_index)
            .SequenceEqual(new[] {0,1,2,3}),"four-player scoreboard keeps all seats");
        Check(ScoreHistoryPanel.ScoreboardRoundName("riichi","riichi/sanma",4)=="南一局"
            && ScoreHistoryPanel.ScoreboardRoundName("riichi","riichi/sanma",3)=="东三局","sanma scoreboard wind labels");
        Check(ScoreHistoryPanel.ScoreboardRoundName("riichi","riichi/standard",4)=="东四局",
            "four-player scoreboard labels preserved");
        var mirror = TableMirror.Current;
        var oldMap = mirror.IndexToPosition;
        string oldSub = GameSession.Current.SubRule;
        try {
            GameSession.Current.SubRule = "riichi/sanma";
            Check(ScoreHistoryPanel.ScoreboardRoundName("riichi","riichi/standard",4)=="东四局",
                "four-player scoreboard ignores active sanma session");
            mirror.IndexToPosition = new Dictionary<int,string>();
            for (int seat = 0; seat < 3; seat++) {
                mirror.BuildSeatMap(seat, new[] {0,1,2});
                Check(mirror.IndexToPosition.Count == 3 && mirror.IndexToPosition[seat] == "self"
                    && mirror.IndexToPosition[(seat+1)%3] == "right" && mirror.IndexToPosition[(seat+2)%3] == "left", "seat map " + seat);
            }
        } finally { mirror.IndexToPosition = oldMap; GameSession.Current.SubRule = oldSub; }
        var hand = new List<int> {21,22,23,24,25,26,31,32,33,36,37,38,29,29};
        RiichiHandResult Calc(List<int> tiles, RiichiHandContext context, int win=38) =>
            RiichiExternal.FullHepaiCheck(tiles, new List<string>(), win, context);
        foreach (bool tsumo in new[] {false,true}) {
            foreach (bool riichi in new[] {false,true}) {
                foreach (bool ura in new[] {false,true}) {
                    var baseline = Calc(hand, new RiichiHandContext {IsSanma=true,IsTsumo=tsumo,IsRiichi=riichi,PlayerWind=42,RoundWind=41});
                    for (int north = 0; north < 5; north++) {
                        var ctx = new RiichiHandContext {IsSanma=true,IsTsumo=tsumo,IsRiichi=riichi,PlayerWind=42,RoundWind=41,
                            NukiCount=north,DoraIndicators=new List<int>{43},UraDoraIndicators=new List<int>{43}};
                        ctx.ApplyRuleOptions(new Dictionary<string,object> { ["ura_dora"]=ura });
                        var result=Calc(hand,ctx);
                        if (baseline.Han>0) Check(result.IsValid && result.Han==baseline.Han+north*(2+(riichi&&ura?1:0)), "north dora stacking " + tsumo + riichi + ura + north);
                        else Check(result.Han==0 && result.Score==0,"north alone is not a yaku " + north);
                    }
                }
            }
        }
        foreach (int man in new[] {11,19}) {
            var tiles=new List<int>{man,man,man,21,22,23,31,32,33,36,37,38,45,45};
            var ctx=new RiichiHandContext {IsSanma=true,IsRiichi=true,PlayerWind=42,RoundWind=41};
            int baseHan=Calc(tiles,ctx).Han;
            ctx.DoraIndicators=new List<int>{man==11?19:11};
            Check(Calc(tiles,ctx).Han==baseHan+3,"manzu indicator cycle " + man);
        }
        for (int man=12;man<=18;man++) {
            var invalid=hand.ToList();invalid[0]=man;
            Check(!Calc(invalid,new RiichiHandContext{IsSanma=true,IsRiichi=true}).IsValid,"removed tile " + man);
        }
        foreach (bool dealer in new[] {false,true}) {
            Check(RiichiScoreCalc.CalculateTotalScore(5,30,dealer,true,0,playerCount:3)==(dealer?8000:6000),"tsumo loss " + dealer);
            Check(RiichiScoreCalc.CalculateTotalScore(5,30,dealer,true,0,playerCount:3,sanmaTsumo:"split")== (dealer?12000:8000),"tsumo split " + dealer);
        }
        Check(RiichiScoreCalc.StripFieldBonuses(7400,true,2,1,3)==6000,"two-payer honba and deposit");
        Check(GameRecordJsonDecoder.ResolveRecordActingPlayerIndex(new List<string>{"nuki","2","44","F"},"nuki",0)==2,"explicit north actor");
        Check(GameRecordJsonDecoder.ConvertPlayerIndexScoreChangesToOriginal(new[] {-8000,9000,-1000},new List<int>{2,0,1})
            .SequenceEqual(new[] {-1000,-8000,9000,0}),"three seat score mapping");
        var record = JObject.Parse("{\"game_title\":{\"rule\":\"riichi\",\"sub_rule\":\"riichi/sanma\",\"p0_uid\":101,\"p1_uid\":102,\"p2_uid\":103},\"game_round\":{\"round_index_1\":{\"seats\":[2,0,1],\"dealer_index\":0,\"start_player_index\":0,\"action_ticks\":[[\"nuki\",2,44,\"T\"],[\"nd\",31],[\"ryuukyoku\",[true,false,false],[3000,-1500,-1500],\"exhaustive\"],[\"end\"]]}}}");
        var parsed = GameRecordJsonDecoder.ParseGameRecord(record.ToString());
        Check(parsed.gameRound.rounds[1].seats.SequenceEqual(new[] {2,0,1}), "parse three-seat round header");
        Check(parsed.gameRound.rounds[1].scoreChanges.Take(3).SequenceEqual(new[] {-1500,3000,-1500}), "parse rotated three-seat draw scores");
        var drawRows = ScoreHistoryRecordSettlementExtractor.ExtractScoreRows(parsed);
        Check(drawRows.Count == 1 && drawRows[0].scoreChangesByOriginal.SequenceEqual(new[] {-1500,3000,-1500,0}), "three-seat draw history");
        foreach (var seats in new[] {new[] {0,1,2},new[] {2,0,1},new[] {1,2,0}}) {
            var history = new GameRecord {gameTitle = new Dictionary<string,object> {
                ["rule"]="riichi",["sub_rule"]="riichi/sanma",["starting_score"]=35000,
            }};
            var round = new Round {roundIndex=1,currentRound=1,seats=seats.ToList(),actionTicks=new List<List<string>> {
                new List<string>{"riichi","2","0"},
                new List<string>{"hu_riichi","1","hu_first","2","30","[\"立直\"]","[-2000,3000,0]"},
                new List<string>{"hu_riichi","2","hu_second","2","30","[\"立直\"]","[-2000,0,2000]"},
            }};
            history.gameRound.rounds[1]=round;
            var rows = ScoreHistoryRecordSettlementExtractor.ExtractScoreRows(history);
            Check(rows.Count==2,"three-seat double-ron history " + string.Join(",",seats));
            Check(rows[0].scoreChangesByOriginal.SequenceEqual(GameRecordJsonDecoder.ConvertPlayerIndexScoreChangesToOriginal(
                new[] {-2000,3000,-1000},round.seats)),"three-seat deposit charged once " + string.Join(",",seats));
            Check(rows[1].scoreChangesByOriginal.SequenceEqual(GameRecordJsonDecoder.ConvertPlayerIndexScoreChangesToOriginal(
                new[] {-2000,0,2000},round.seats)),"three-seat second ron mapping " + string.Join(",",seats));
            Check(rows.All(row=>row.scoreChangesByOriginal[3]==0),"empty fourth-seat history " + string.Join(",",seats));
        }
        parsed.gameTitle["starting_scores"] = new[] {35000,35000,35000};
        parsed.gameTitle["riichi_final_scores"] = new[] {33500,39000,33500};
        Check(ScoreHistoryRecordSettlementExtractor.ExtractScoreRows(parsed)[0].scoreChangesByOriginal.SequenceEqual(
            new[] {-1500,4000,-1500,0}),"three-seat final deposits in last row");
        record["game_title"]["sub_rule"] = "riichi/standard";
        bool rejected = false;
        try { GameRecordJsonDecoder.ParseGameRecord(record.ToString()); } catch (Exception) { rejected = true; }
        Check(rejected, "four-player record rejects three-seat header");
        return new {passed=checks.Count,checks};
    }
}
#endif
