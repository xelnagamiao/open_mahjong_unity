#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using Riichi;
using UnityEngine;

/// <summary>Washizu protocol and local calculation parity; runs in Editor and development Players.</summary>
public static class WashizuValidation {
    public static object Run() {
        var checks = new List<string>();
        void Check(bool valid, string label) {
            if (!valid) throw new InvalidOperationException("Washizu: " + label);
            checks.Add(label);
        }
        Check(RiichiLobby.SubRules.Any(rule => rule.Key == WashizuTile.SubRule), "room variant registered");
        var faces = Enumerable.Range(11, 9).Concat(Enumerable.Range(21, 9))
            .Concat(Enumerable.Range(31, 9)).Concat(Enumerable.Range(41, 7));
        foreach (int face in faces) {
            Check(WashizuTile.IsTransparent(face + 2000) && !WashizuTile.IsTransparent(face), "copy class " + face);
            Check(RiichiTileUtil.Normalize(face + 2000) == face, "calculation face " + face);
            Check(TileIdOrder.Normalize(face + 2000) == face, "sorting face " + face);
            Check(!WashizuTile.SameCopyKind(face, face + 2000), "copy identity " + face);
        }
        foreach (int id in new[] {0, 1, 105, 205, 305, 1011, 2010, 2020, 2048, 2051})
            Check(!WashizuTile.IsTransparent(id), "other encoding preserved " + id);
        var hand = new List<int> {12,13,14,22,23,24,33,34,35,36,37,38,28,28};
        foreach (bool tsumo in new[] {false,true}) {
            var normalContext = new RiichiHandContext {IsRiichi=true, IsTsumo=tsumo, PlayerWind=42, RoundWind=41,
                DoraIndicators=new List<int>{27}, UraDoraIndicators=new List<int>{32}};
            var glassContext = new RiichiHandContext {IsRiichi=true, IsTsumo=tsumo, PlayerWind=42, RoundWind=41,
                DoraIndicators=new List<int>{2027}, UraDoraIndicators=new List<int>{2032}};
            var normal = RiichiExternal.FullHepaiCheck(hand, new List<string>(), 28, normalContext);
            var glass = RiichiExternal.FullHepaiCheck(hand.Select(t=>t+2000).ToList(), new List<string>(), 2028, glassContext);
            Check(normal.IsValid && glass.IsValid && normal.Han == glass.Han && normal.Fu == glass.Fu
                && normal.Score == glass.Score, "glass scoring/dora/ura parity " + tsumo);
        }
        var exact = new List<int>{11,2011,2011,22};
        GameRecordMeldCodec.RemoveExactTilesFromList(exact, new[]{11,2011});
        Check(exact.SequenceEqual(new[]{2011,22}), "selected pon preserves remaining glass");
        var record = JObject.Parse(@"{""game_title"":{""rule"":""riichi"",""sub_rule"":""riichi/washizu""},""game_round"":{""round_index_1"":{""seats"":[0,1,2,3],""dealer_index"":0,""start_player_index"":0,""washizu"":{""version"":1,""draw_limit"":70,""dead_wall_count"":13,""dora_indicators"":[2027]},""action_ticks"":[]}}}");
        var parsed = GameRecordJsonDecoder.ParseGameRecord(record.ToString());
        Check(parsed.gameRound.rounds[1].washizu.draw_limit == 70
            && parsed.gameRound.rounds[1].washizu.dora_indicators.SequenceEqual(new[]{2027}), "record pool metadata");
        var visibleContext = new RecordTipsContext {RoomRule="riichi", SubRule=WashizuTile.SubRule,
            PlayersByPosition=new Dictionary<string,RecordTipsPlayerVisible> {
                ["self"]=new RecordTipsPlayerVisible {PublicHandTiles=new List<int>{2011}},
                ["right"]=new RecordTipsPlayerVisible {PublicHandTiles=new List<int>{2011,0,12}},
            }};
        var counts = RecordWaitHintCalculator.CountVisibleTiles(visibleContext,new List<int>{2011});
        Check(counts[11] == 2 && !counts.ContainsKey(12) && !counts.ContainsKey(0), "remaining count uses public glass only, own copies once");
        var shader = Resources.Load<Shader>("Washizu/TransparentTile");
        Check(shader != null && shader.isSupported, "glass shader packaged and supported");
        return new {passed=true, count=checks.Count, checks};
    }
}
#endif
