#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using UnityEngine;

public static class GuobiaoFourJokersValidation {
    public static object RunPaging() => UpwardPopupLayoutValidation.RunHints();
    public static object RunOpeningHints() {
        var checks=new List<string>();
        void Check(bool ok,string label){if(!ok)throw new Exception(label);checks.Add(label);}
        WaitTileHint Resolve(WaitHintQuery query) {
            WaitTileHint result=null;
            var routine=FourJokersTips.Resolve(query,hint=>result=hint);
            var watch=System.Diagnostics.Stopwatch.StartNew();
            while(routine.MoveNext()) {
                if(watch.ElapsedMilliseconds>5000)throw new Exception("Opening hint job timed out");
                System.Threading.Thread.Sleep(1);
            }
            return result??throw new Exception("Opening hint job failed");
        }
        foreach(string sub in new[]{GuobiaoGameState.FourJokersSubRule,GuobiaoGameState.HuahunSubRule,GuobiaoGameState.SixteenSubRule}) {
            bool sixteen=sub==GuobiaoGameState.SixteenSubRule;
            var hand=new List<int>{11,12,13,24,25,26,36,37,38,41,41,41,45,45};
            if(sixteen)hand.AddRange(new[]{21,22,23});
            var query=new WaitHintQuery{SubRule=sub,HandWithWin=hand,Melds=new List<string>(),HepaiTile=45,
                WayToHepai=new List<string>(),SingleTileWay=new List<string>(),MergedWay=new List<string>{"点和"},HepaiLimit=8,NoJokerBonus="none"};
            int ordinary=sixteen?12:10;
            var baseline=Resolve(query);
            Check(baseline.Kind==WaitTileHint.KindRon&&baseline.Label==ordinary+"番",sub+" ordinary hint unchanged");
            query.GuobiaoTiming=new GuobiaoHintTiming{DrawTile=45,DrawOpening="人和"};
            var human=Resolve(query);
            Check(human.Kind==WaitTileHint.KindTsumoOnly&&human.Label=="自摸"+(ordinary+10)+"番",sub+" current human win uses self draw score");
            query.GuobiaoTiming=new GuobiaoHintTiming{DrawTile=45,InitialDraw=true};
            var initial=Resolve(query);
            Check(initial.Kind==WaitTileHint.KindTsumoOnly&&initial.Label=="自摸"+(ordinary+1)+"番",sub+" initial draw excludes single wait with opening switch off");
            query.GuobiaoTiming.DrawOpening="天和";
            var heavenly=Resolve(query);
            Check(heavenly.Label=="自摸"+(ordinary+9)+"番",sub+" heavenly win adds eight without single wait");
            query.GuobiaoTiming=new GuobiaoHintTiming{RonTile=45,RonOpening="地和"};
            var earth=Resolve(query);
            Check(earth.Kind==WaitTileHint.KindRon&&earth.Label==(ordinary+8)+"番",sub+" earth win is added to actual discard");
            query.GuobiaoTiming.RonTile=46;
            Check(Resolve(query).Label==baseline.Label,sub+" another wait does not inherit earth win");
            query.GuobiaoTiming=null;
            Check(FourJokersTips.TryDescribeCached(query,out var cached)&&cached.Label==baseline.Label,sub+" expired opening restores ordinary cache");
        }
        var onlyRon=new WaitHintQuery{SubRule=GuobiaoGameState.FourJokersSubRule,HandWithWin=new List<int>{25,25},
            Melds=new List<string>{"s12","s24","s36","s17"},HepaiTile=25,HepaiLimit=8,
            WayToHepai=new List<string>(),SingleTileWay=new List<string>(),MergedWay=new List<string>{"点和"}};
        Check(Resolve(onlyRon).Kind==WaitTileHint.KindRonOnly,"all-exposed hand qualifies only by ron");
        onlyRon.GuobiaoTiming=new GuobiaoHintTiming{DrawTile=25};
        Check(Resolve(onlyRon).Kind==WaitTileHint.KindNone,"actual self draw cannot fall back to qualifying ron");
        return new{count=checks.Count,checks};
    }
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
