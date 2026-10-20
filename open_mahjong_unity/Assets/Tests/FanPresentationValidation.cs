#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

/// <summary>小数番的协议、观战增量、牌谱计分板和结算面板共同回归。</summary>
public static class FanPresentationValidation {
    private sealed class QingqueEnvelopeProbe : QingqueGameState {
        public SettlementEnvelope Read(ShowResultInfo info) => BuildEnvelope(info);
    }

    public static string Run() {
        int checks = 0;
        void Check(bool condition, string label) {
            if (!condition) throw new Exception("Fan presentation: " + label);
            checks++;
        }
        var totalMethod = typeof(EndResultPanel).GetMethod("BuildTotalDisplay", BindingFlags.Static | BindingFlags.NonPublic);
        var spectatorDecoder = typeof(GameRecordManager).GetMethod("ConvertTickArrayToList", BindingFlags.Static | BindingFlags.NonPublic);
        var oldCulture = CultureInfo.CurrentCulture;
        var envelopeProbe = new QingqueEnvelopeProbe();
        try {
            foreach (string locale in new[] {"zh-CN", "en-US", "de-DE", "fr-FR"}) {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(locale);
                foreach (double fan in new[] {1, 3.60616, 8.01904, 8.93625, 18.49999, 18.5, 18.50001}) {
                    int baseScore = (int)Math.Round(fan * fan);
                    string fanText = fan.ToString("0.#####", CultureInfo.InvariantCulture) + "番";
                    foreach (string huClass in new[] {"hu_self", "hu_first", "hu_second", "hu_third"}) {
                        int winner = Array.IndexOf(new[] {"hu_self", "hu_first", "hu_second", "hu_third"}, huClass);
                        int[] changes = Enumerable.Range(0, 4).Select(i =>
                            huClass == "hu_self" ? baseScore * (i == winner ? 3 : -1)
                            : baseScore * (i == winner ? 3 : i == 0 ? -3 : 0)).ToArray();
                        var fans = new[] {"门前清", "一般高"};
                        var tick = new JArray(huClass, winner, fan, new JArray(fans), new JArray(changes), 31);
                        var infoJson = new JObject {
                            ["hu_class"] = huClass, ["hepai_player_index"] = winner, ["hu_score"] = fan,
                            ["hu_fan"] = new JArray(fans), ["score_changes"] = JObject.FromObject(
                                Enumerable.Range(0,4).ToDictionary(i=>i,i=>changes[i])),
                        }.ToString();
                        var info = JsonConvert.DeserializeObject<ShowResultInfo>(infoJson);
                        var envelope = envelopeProbe.Read(info);
                        Check(info.hu_score == fan && envelope.HuScore == fan, locale + " network precision");
                        Check(FanTextDictionary.ShouldPlayGongHuSound("qingque/standard", fans, envelope.HuScore) == (fan > 18.5),
                            "live/realtime Qingque gong uses precise fan total");
                        Check(envelope.ScoreChanges[winner] == baseScore * 3, "integer payment is separate");
                        var live = ScoreHistorySettlementHelper.CreateFromShowResult("qingque/standard", huClass,
                            winner, "test", envelope.HuScore, fans, new[]{31,31}, Array.Empty<int[]>(), null, null, null, null);
                        Check(live.huScore == fan, "live/realtime scoreboard snapshot preserves fraction");
                        live.winnerScoreDelta = changes[winner];
                        Check(ScoreHistorySettlementHelper.BuildScoreSummaryText("qingque/standard", live)
                            == fanText + " +" + changes[winner] + "分", "live/realtime scoreboard summary");

                        for (int rotation=0; rotation<4; rotation++) {
                            var record = GameRecordJsonDecoder.ParseGameRecord(RecordJson(tick, rotation, huClass == "hu_self"));
                            var parsed = record.gameRound.rounds[1].actionTicks[0];
                            var streamed = (List<string>)spectatorDecoder.Invoke(null, new object[]{tick});
                            Check(parsed.SequenceEqual(streamed), locale + " record and spectator decode agree");
                            Check(double.Parse(parsed[2], CultureInfo.InvariantCulture) == fan, "JSON decimal survives locale");
                            foreach (var input in new[]{parsed, streamed}) {
                                record.gameRound.rounds[1].actionTicks[0] = input;
                                var rows = ScoreHistoryRecordSettlementExtractor.ExtractScoreRows(record);
                                Check(rows.Count == 1, "one winner row");
                                var snapshot = rows[0].snapshot;
                                Check(snapshot.huScore == fan && snapshot.winnerScoreDelta == changes[winner], "record/observer fan and points");
                                Check(FanTextDictionary.ShouldPlayGongHuSound("qingque/standard", fans, snapshot.huScore) == (fan > 18.5),
                                    "record/observer Qingque gong uses precise fan total");
                                Check(ScoreHistorySettlementHelper.BuildScoreSummaryText("qingque/standard", snapshot)
                                    == fanText + " +" + changes[winner] + "分", "record/observer scoreboard summary");
                                var total = (SettlementTotalDisplay)totalMethod.Invoke(null, new object[]{
                                    "qingque/standard", snapshot.huScore, fans, null, null, changes[winner]});
                                Check(total.FanText == fanText && total.ScoreText == "+"+changes[winner]+"分", "result panel keeps precise fans and actual points");
                                Check(snapshot.hepaiPlayerHand.Length == 14, "ron/tsumo hand unchanged");
                            }
                        }
                    }
                }
            }
        } finally { CultureInfo.CurrentCulture = oldCulture; }

        // Fan combinations are scored jointly; no individual fixed values may be invented.
        foreach (string name in Qingque13.QingqueFanMetadata.ChineseNames.Values)
            Check(FanTextDictionary.GetFanDisplayText("qingque/standard",name)=="精算", "Qingque catalog: "+name);
        Check(FanTextDictionary.GetFanDisplayText("hongque/v1.6","清顺*3|3")=="3番", "Hongque repeated fan total");
        Check(FanTextDictionary.GetFanDisplayText("guobiao/standard","一般高*2")=="2番", "Guobiao repeated fan total");
        Check(ScoreHistorySettlementHelper.BuildAllFansText("hongque/v1.6", new RoundSettlementSnapshot {
            huFan = new[] {"清顺*2|2", "双同花*2|4", "门清|1"}
        }) == "清顺*2 2番 双同花*2 4番 门清 1番", "Hongque fan details retain repetition and accumulated values");
        Check(ScoreHistorySettlementHelper.BuildAllFansText("guobiao/standard", new RoundSettlementSnapshot {
            huFan = new[] {"一般高*2"}
        }) == FanTextDictionary.GetFanNameDisplayText("guobiao/standard", "一般高*2") + " 2番",
            "Guobiao fan details retain repetition in the selected language");
        Check(FanTextDictionary.GetFanDisplayText("guobiao/xiaolin","幺九刻*2")=="0番", "legitimate zero fan retained");
        Check(VietnameseScoreText.Amount(8)=="1满 + 2番", "Vietnamese full units stay integral");
        var hongzhong = RuleRegistry.Resolve("hongzhong").SettlementTotal(new SettlementTotalQuery {HuScore=4});
        Check(hongzhong.ScoreText == "基本分 16分", "Hongzhong exponential score stays integral");
        Check(FanTextDictionary.ShouldPlayGongHuSound("hongque/v1.6",Array.Empty<string>(),30), "Hongque gong threshold");
        Check(!FanTextDictionary.ShouldPlayGongHuSound("guobiao/standard",new[]{"花牌*1"},64), "Guobiao gong excludes flowers");

        foreach (string rule in new[] { "qingque", "qingque/standard" }) {
            Check(!FanTextDictionary.ShouldPlayGongHuSound(rule, null, 18.5), "Qingque 18.5 is below gong threshold");
            Check(FanTextDictionary.ShouldPlayGongHuSound(rule, null, 18.50001), "Qingque gong does not require a fan-name list");
            int[] mask = GameRecordMeldCodec.BuildAngangMaskFromRemoved(new[] { 35, 35, 35, 35 }, rule);
            Check(mask.SequenceEqual(new[] { 2, 35, 0, 35, 0, 35, 2, 35 }), "Qingque replay kong is back-face-face-back");
            var group = SettlementMeldLayoutBuilder.Build(new[] { mask })[0];
            Check(group.Select(tile => tile.FaceDown).SequenceEqual(new[] { true, false, false, true }),
                "Qingque settlement kong is back-face-face-back");
        }

        int subRules=0;
        foreach(var manifest in RuleRegistry.All) {
            foreach(string key in (manifest.LobbySubRules ?? Array.Empty<RuleLobbySubRule>()).Select(s=>s.Key)
                .Concat(new[]{manifest.DefaultSubRule}).Where(s=>!string.IsNullOrEmpty(s)).Distinct()) {
                subRules++;
                var query=new SettlementTotalQuery {Rule=key,HuScore=8,HuFan=Array.Empty<string>(),Han=3,Fu=30,WinnerPointDelta=192};
                var display=manifest.SettlementTotal?.Invoke(query) ?? query.DefaultDisplay();
                Check(display!=null && display.ScoreText!=null, key+" settlement renderer");
                string summary=ScoreHistorySettlementHelper.BuildScoreSummaryText(key,new RoundSettlementSnapshot {
                    hasWin=true,huScore=8,huFan=Array.Empty<string>(),han=3,fu=30,winnerScoreDelta=192});
                Check(summary.EndsWith("+192分",StringComparison.Ordinal),key+" integer points unchanged");
            }
        }
        return $"PASS: {checks} checks, {RuleRegistry.All.Count()} rule families, {subRules} subrules; 4 cultures × 7 fan totals × 4 win routes × 4 seat rotations × record/spectator; Qingque gong boundary and concealed kongs.";
    }

    private static string RecordJson(JArray tick, int rotation, bool tsumo) {
        int[] hand={11,11,11,12,12,12,13,13,13,21,21,21,31};
        var round=new JObject { ["current_round"]=1, ["dealer_index"]=0,["start_player_index"]=0,
            ["seats"]=new JArray(Enumerable.Range(0,4).Select(i=>(i+rotation)%4)), ["action_ticks"]=new JArray(tick.DeepClone()) };
        for(int i=0;i<4;i++) round["p"+i+"_tiles"]=new JArray(tsumo && i==(int)tick[1] ? hand.Concat(new[]{31}) : hand);
        return new JObject { ["game_title"]=new JObject {["rule"]="qingque",["sub_rule"]="qingque/standard"},
            ["game_round"]=new JObject {["round_index_1"]=round} }.ToString();
    }
}
#endif
