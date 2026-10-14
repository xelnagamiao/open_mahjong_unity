#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using UnityEngine;

public static class GuobiaoFourJokersValidation {
    public static object RunPaging() {
        const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
        string previous = GameSession.Current.SubRule;
        var root = new GameObject("FourJokersPagingTest", typeof(RectTransform));
        root.SetActive(false);
        try {
            var tips = root.AddComponent<TipsContainer>();
            var rows = new[] { "TileContainer", "FanContainer" }.Select(name => {
                var row = new GameObject(name, typeof(RectTransform), typeof(UnityEngine.UI.HorizontalLayoutGroup));
                row.transform.SetParent(root.transform, false);
                typeof(TipsContainer).GetField(name, flags).SetValue(tips, row);
                for (int i = 0; i < 35; i++) new GameObject(i.ToString(), typeof(RectTransform)).transform.SetParent(row.transform, false);
                return row;
            }).ToArray();
            GameSession.Current.SubRule = GuobiaoGameState.FourJokersSubRule;
            var update = typeof(TipsContainer).GetMethod("UpdateGuangdongHintPages", flags);
            var visited = new HashSet<int>();
            for (int page = 0; page < 4; page++) {
                typeof(TipsContainer).GetField("guangdongHintPage", flags).SetValue(tips, page);
                update.Invoke(tips, null);
                var visible = rows[0].transform.Cast<Transform>().Where(t => t.gameObject.activeSelf).Select(t => t.GetSiblingIndex()).ToArray();
                if (visible.Length != (page == 3 ? 5 : 10) || visible.Any(i => !visited.Add(i))) throw new Exception("35 waits must appear exactly once across four pages");
                if (!visible.SequenceEqual(rows[1].transform.Cast<Transform>().Where(t => t.gameObject.activeSelf).Select(t => t.GetSiblingIndex()))) throw new Exception("tile and fan pages must match");
            }
            if (visited.Count != 35) throw new Exception("missing wait page");
            GameSession.Current.SubRule = "guobiao/standard";
            typeof(TipsContainer).GetField("visibleHintSubRule", flags).SetValue(tips, GuobiaoGameState.FourJokersSubRule);
            update.Invoke(tips, null);
            if (rows[0].transform.Cast<Transform>().Count(t => t.gameObject.activeSelf) != 5) throw new Exception("replay page must not depend on last live rule");
            typeof(TipsContainer).GetField("visibleHintSubRule", flags).SetValue(tips, null);
            update.Invoke(tips, null);
            if (rows.Any(row => row.transform.Cast<Transform>().Any(t => !t.gameObject.activeSelf))) throw new Exception("switching rules must restore unpaged rows");
            return new { count = 11, faces = visited.Count, pages = 4 };
        } finally { GameSession.Current.SubRule = previous; UnityEngine.Object.DestroyImmediate(root); }
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
            var sprite = Resources.Load<Sprite>("image/Cards/Faces/official/" + kind + "/59");
            Check(sprite != null && sprite.rect.width > 100, "red flower sprite " + kind);
        }
        var response = JsonConvert.DeserializeObject<Response>("{\"game_info\":{\"four_jokers_hints\":{\"hand\":[11,11,11,21,22,23,31,32,33,44,44,44,45],\"melds\":[],\"waits\":[45,59],\"discard_waits\":{}}}}");
        var snapshot = response.game_info.four_jokers_hints;
        FourJokersTips.Accept(snapshot);
        var query = new TingpaiQuery {Hand=snapshot.hand.ToList(),Melds=new List<string>(),SubRule=sub};
        var waiting = GuobiaoTips.Tingpai(query);
        Check(waiting.SetEquals(new[]{45,59}), "authoritative waits preserve red flower");
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
