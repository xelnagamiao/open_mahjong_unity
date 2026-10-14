#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json.Linq;
using Riichi;
using UnityEngine;

/// <summary>Sanma field payments and the actual incremental replay HUD path.</summary>
public static class RiichiFieldStateValidation {
    public static object Run() {
        int checks = 0;
        void Check(bool ok, string label) {
            if (!ok) throw new InvalidOperationException("Riichi field: " + label);
            checks++;
        }
        foreach (int count in new[] {3,4})
        foreach (int honba in new[] {0,1,2,5})
        foreach (int sticks in new[] {0,1,3})
        foreach (bool tsumo in new[] {false,true}) {
            int bonus = honba * (count == 3 ? 200 : 300);
            int gain = 8000 + bonus + sticks * 1000;
            Check(RiichiScoreCalc.StripFieldBonuses(gain, tsumo, honba, sticks, count) == 8000,
                $"strip {count}/{honba}/{sticks}/{tsumo}");
            Check(RiichiScoreCalc.ResolveDisplayPoints(0,0,false,tsumo,gain,honba,sticks,playerCount:count) == 8000,
                $"fallback {count}/{honba}/{sticks}/{tsumo}");
        }
        var catalog = JObject.Parse(Resources.Load<TextAsset>("RiichiRuleOptions").text);
        foreach (var preset in catalog["presets"]) {
            bool sanma = (string)preset["sub_rule"] == "riichi/sanma";
            Check((int)preset["values"]["noten_penalty"] == (sanma ? 2000 : 3000),
                "preset penalty " + (string)preset["id"]);
        }
        var option = catalog["options"].Single(o=>(string)o["key"]=="noten_penalty");
        Check(option["values"].Select(v=>(int)v).SequenceEqual(new[] {0,2000,3000}), "explicit penalty choices");
        return new { checks, failures = Array.Empty<string>() };
    }

    /// <summary>Run only in an isolated live scene with a record containing dora ticks.</summary>
    public static object RunRecordDora(string recordJson) {
        var record=JObject.Parse(recordJson);var title=(JObject)record["game_title"];
        int count=(int)title["player_count"];
        var users=Enumerable.Range(0,count).Select(i=>new PlayerRecordInfo {
            user_id=(int)title["p"+i+"_uid"],username=(string)title["p"+i+"_name"]
        }).ToArray();
        PostGameNavigator.ExitToLobby(forceTeardown:true);
        if(!RecordPanel.OpenRecord(new RecordDetail { game_id="dora-regression",cloud_saved=true,
            rule="riichi",sub_rule=(string)title["sub_rule"],players=users,
            record=record.ToObject<Dictionary<string,object>>() }))
            throw new InvalidOperationException("Could not open dora regression record");
        var manager=GameRecordManager.Instance;
        var round=manager.gameRecord.gameRound.rounds[1];
        var flags=BindingFlags.Instance|BindingFlags.NonPublic;
        var panel=(RiichiRoundPanel)typeof(RoundPanel).GetField("riichiRoundPanel",flags).GetValue(RoundPanel.Instance);
        var slots=(StaticCard[])typeof(RiichiRoundPanel).GetField("doraSlots",flags).GetValue(panel);
        int checks=0;
        void CheckHud() {
            var model=(List<int>)typeof(GameRecordManager).GetField("recordRiichiDoraIndicators",flags).GetValue(manager);
            var expected=Enumerable.Range(0,slots.Length).Select(i=>i<model.Count?model[i]:0);
            if(!panel.gameObject.activeInHierarchy || !slots.Select(s=>s.TileId).SequenceEqual(expected))
                throw new InvalidOperationException("Dora HUD stale at node " + manager.currentNode);
            checks++;
        }
        for(int viewer=0;viewer<count;viewer++) {
            manager.SwitchRecordPerspectiveToOriginalPlayerIndex(viewer);
            manager.GotoSelectRound(1);manager.GotoSelectNode(0);CheckHud();
            while(manager.currentNode<round.actionTicks.Count) {manager.NextStep();CheckHud();}
            for(int node=round.actionTicks.Count;node>=0;node--) {manager.GotoSelectNode(node);CheckHud();}
        }
        return new {checks,failures=Array.Empty<string>()};
    }
}
#endif
