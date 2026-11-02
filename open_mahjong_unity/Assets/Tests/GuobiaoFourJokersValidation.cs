#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using UnityEngine;

public static class GuobiaoFourJokersValidation {
    public static object RunPaging() => UpwardPopupLayoutValidation.RunHints();
    public static object Run() {
        var checks = new List<string>();
        void Check(bool ok, string label) { if (!ok) throw new Exception(label); checks.Add(label); }
        string sub = GuobiaoGameState.FourJokersSubRule;
        Check(Array.FindIndex(GuobiaoLobby.SubRules, row => row.Key == sub) == 9, "new room option preserves previous indices");
        Check(MahjongPlayerCount.ForSubRule(sub) == 4, "four seats");
        RuleRegistry.TryResolve("guobiao", sub, out RuleManifest manifest);
        Check(manifest != null && manifest.HandJokerTiles(sub).SequenceEqual(new[]{59}), "red flower is the only joker");
        Check(TilePackIds.IsStandardFaceId(59), "red flower face registered");
        foreach (string kind in new[]{"hand","table"}) {
            var sprite = kind == "hand" ? BuiltinTileSprites.Hand(TilePackIds.PackOfficial, 59) : BuiltinTileSprites.OfficialTable(59);
            Check(sprite != null && sprite.rect.width > 100, "red flower sprite " + kind);
        }
        var response = JsonConvert.DeserializeObject<Response>("{\"game_info\":{\"four_jokers_hints\":{\"hand\":[11,11,11,21,22,23,31,32,33,44,44,44,45],\"melds\":[],\"waits\":[45,59],\"discard_waits\":{}}}}");
        var snapshot = response.game_info.four_jokers_hints;
        FourJokersTips.Accept(snapshot);
        var query = new TingpaiQuery {Hand=snapshot.hand.ToList(),Melds=new List<string>(),SubRule=sub};
        var waiting = LocalHintValidation.WaitForLocal(query);
        Check(waiting.SetEquals(new[]{45,59}), "local waits preserve red flower");
        waiting.Clear();
        Check(GuobiaoTips.Tingpai(query).Count == 2, "hint cache is not mutable by callers");
        Check(snapshot.hand.Length == 13 && !snapshot.hand.Contains(59), "hint lookup does not alter hand");
        var config = new GB_Create_RoomConfig {SubRule=sub, DuplicateKey="ordinary-wall"};
        Check(!config.Validate(out _, false, false), "ordinary duplicate wall rejected");
        Check(!new GB_Create_RoomConfig().JokerMultiWait, "multi-wait defaults off");
        var session = new GameSession();
        session.LoadFrom(new GameInfo {sub_rule=sub, joker_multi_wait=true}, 0);
        Check(session.JokerMultiWait, "live and reconnect keep multi-wait");
        session.ResetForExit();
        Check(!session.JokerMultiWait, "leaving clears multi-wait");
        var ctx = new RecordTipsContext {SubRule=sub,HepaiLimit=8,JokerMultiWait=true,CurrentRound=1,SelfPlayerIndex=0,
            SelfHuapaiList=new List<int>(),PlayersByPosition=new Dictionary<string,RecordTipsPlayerVisible>()};
        var hint = RecordWaitHintCalculator.BuildQueries(ctx,new List<int>{45},new List<int>{45})[0];
        Check(hint.JokerMultiWait, "replay copies multi-wait into immutable hint query");
        var keyMethod=typeof(FourJokersTips).GetMethod("HintKey",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Static);
        string on=(string)keyMethod.Invoke(null,new object[]{hint});
        hint.JokerMultiWait=false;
        Check(on!=(string)keyMethod.Invoke(null,new object[]{hint}), "hint cache distinguishes policies");
        var score=GBhepai.HepaiCheck(new List<int>{24,24},new List<string>{"k32","k16","k36","k28"},
            new List<string>{"点和","花牌","花牌","花牌"},24);
        Check(score.Item1==35, "all-exposed ron does not depend on wait hint flag");
        return new {count=checks.Count,checks};
    }
}
#endif
