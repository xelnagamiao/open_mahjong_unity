#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using Riichi;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

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
            bool mil = (string)preset["id"] == "mil";
            Check(mil ? (string)preset["values"]["noten_penalty"] == "fixed_gap_2000"
                    : (int)preset["values"]["noten_penalty"] == (sanma ? 2000 : 3000),
                "preset penalty " + (string)preset["id"]);
            Check((bool)preset["values"]["honba_enabled"] == !mil, "preset honba " + (string)preset["id"]);
        }
        var option = catalog["options"].Single(o=>(string)o["key"]=="noten_penalty");
        Check(option["values"].Select(v=>v.ToString()).SequenceEqual(new[] {"0","2000","3000","fixed_gap_2000"}), "explicit penalty choices");
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
            record=DeviceRecordMigration.ConvertSingle("dora-regression",record,JArray.FromObject(users)) }))
            throw new InvalidOperationException("Could not open dora regression record");
        var manager=GameRecordManager.Instance;
        var round=manager.gameRecord.gameRound.rounds[1];
        var panel=RoundPanel.Instance.GetComponentInChildren<IndicatorRoundPanel>(true);
        var slots=SlotsInDisplayOrder(panel);
        int checks=0;
        void CheckHud() {
            var revealed=new List<int>();
            if(((string)title["sub_rule"])?.StartsWith("riichi/washizu",StringComparison.Ordinal)==true)
                revealed.AddRange(round.washizu?.dora_indicators ?? Array.Empty<int>());
            else if(round.riichi?.doraMarker>=10)
                revealed.Add(round.riichi.doraMarker);
            else if(round.tilesList.Count>=6)
                revealed.Add(round.tilesList[round.tilesList.Count-6]);
            foreach(var tick in round.actionTicks.Take(manager.currentNode))
                if(tick.Count>1 && tick[0]=="dora") revealed.Add(int.Parse(tick[1]));
            var expected=revealed.Concat(Enumerable.Repeat(0,slots.Length)).Take(slots.Length);
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

    private static StaticCard[] SlotsInDisplayOrder(IndicatorRoundPanel panel) {
        return panel.GetComponentsInChildren<StaticCard>(true)
            .OrderBy(slot => slot.transform.GetSiblingIndex()).ToArray();
    }

    /// <summary>Switch the real MainScene HUD between rules and restore the open replay afterwards.</summary>
    public static object RunIndicatorLayout() {
        var manager = GameRecordManager.Instance;
        var hud = RoundPanel.Instance;
        if (!hud || !hud.gameObject.activeInHierarchy || !manager || manager.gameRecord == null)
            throw new InvalidOperationException("Open a MainScene replay before checking the indicator layout");
        int savedRound = manager.currentRoundIndex, savedNode = manager.currentNode;
        var panel = hud.GetComponentInChildren<IndicatorRoundPanel>(true);
        var slots = SlotsInDisplayOrder(panel);
        int checks = 0;
        void Check(bool ok, string message) {
            if (!ok) throw new InvalidOperationException("Indicator layout: " + message);
            checks++;
        }
        Check(slots.Length == 5, "expected five authored indicator slots");
        var row = (RectTransform)slots[0].transform.parent;
        Check(slots.All(s => s.transform.parent == row), "slots must share a single row");
        float[] Centers() {
            Canvas.ForceUpdateCanvases();
            LayoutRebuilder.ForceRebuildLayoutImmediate(row);
            return slots.Select(slot => {
                var rect = (RectTransform)slot.transform;
                return row.InverseTransformPoint(rect.TransformPoint(rect.rect.center)).x;
            }).ToArray();
        }
        float[] reference = null;
        void VerifyCounters(string state, int? honba = null, int? sticks = null) {
            var content = panel.transform.Find("Content");
            var signs = content.Cast<Transform>().Where(t => t.name == "x").ToArray();
            Check(signs.Length == 2, state + " missing authored multiplication signs");
            foreach (var sign in signs)
                Check(sign.gameObject.activeInHierarchy == honba.HasValue, state + " multiplication sign visibility");
            foreach (var (prefix, count) in new[] { ("100", honba), ("1000", sticks) }) {
                var icon = content.Find(prefix + "image").GetComponent<Image>();
                var label = content.Find(prefix + "count").GetComponent<TMP_Text>();
                Check(icon.isActiveAndEnabled == count.HasValue, state + " " + prefix + " stick icon");
                Check(label.isActiveAndEnabled == count.HasValue, state + " " + prefix + " stick counter");
                if (count.HasValue) Check(label.text == count.Value.ToString(), state + " " + prefix + " stick value");
            }
        }
        void Verify(string state, int[] faces, string[] roles, int? honba = null, int? sticks = null) {
            Check(panel.gameObject.activeInHierarchy, state + " indicator panel is inactive");
            VerifyCounters(state, honba, sticks);
            var centers = Centers();
            for (int i = 0; i < slots.Length; i++) {
                var slot = slots[i];
                bool visible = i < faces.Length;
                var group = slot.GetComponent<CanvasGroup>();
                bool drawn = slot.gameObject.activeInHierarchy && (!group || group.alpha > .99f);
                Check(drawn == visible, state + " visibility at slot " + i);
                Check(slot.TileId == (visible ? faces[i] : 0), state + " face at slot " + i);
                var label = slot.GetComponentsInChildren<TMP_Text>(false).SingleOrDefault(t => t.name == "RuleTileBadge");
                string role = visible ? roles[i] : null;
                Check((label ? label.text : null) == role, state + " role at slot " + i);
                if (label) Check(label.rectTransform.anchorMax == Vector2.one, state + " role outside upper right");
                if (reference != null)
                    Check(Mathf.Abs(centers[i] - reference[i]) < .05f, state + " changed native position of slot " + i);
            }
        }
        try {
            var riichi = new GameInfo {room_rule="riichi", sub_rule="riichi/standard", room_type="custom",
                current_round=1, max_round=4, dora_indicators=new[]{24}, kan_dora_indicators=new[]{24,16}};
            hud.UpdateRoomInfo(riichi, riichi.sub_rule);
            Verify("riichi repeated dora", new[]{24,24,16,0,0}, new string[5], 0, 0);
            reference = Centers();
            float pitch = row.rect.width / slots.Length;
            for (int i = 1; i < slots.Length; i++)
                Check(Mathf.Abs(reference[i] - reference[i-1] - pitch) < .05f, "native left-to-right slot pitch " + i);

            var wuhan = new GameInfo {room_rule="wuhan", sub_rule="wuhan/mil2025", room_type="custom",
                current_round=1, max_round=4, wuhan_info=new WuhanInfo { skins=new[]{45} }};
            hud.UpdateRoomInfo(wuhan, wuhan.sub_rule);
            Verify("Wuhan pending", new[]{0,45}, new[]{null,"皮"});
            var sanhua = new GameInfo {room_rule="new_chinese", sub_rule="new_chinese/sanhua", room_type="custom",
                current_round=1, max_round=4, sanhua=new SanhuaInfo { indicator=23, skin=23, joker=24 }};
            hud.UpdateRoomInfo(sanhua, sanhua.sub_rule);
            Verify("Sanhua number", new[]{23,24}, new[]{"指","赖"});
            hud.RefreshIndicators(wuhan.room_rule, WuhanPresentation.Indicators(null));
            Verify("late Wuhan clear", new[]{23,24}, new[]{"指","赖"});
            sanhua.sanhua = new SanhuaInfo { indicator=45, skin=46, joker=47 };
            hud.UpdateRoomInfo(sanhua, sanhua.sub_rule);
            Verify("Sanhua red", new[]{45,46,47}, new[]{"指","皮","赖"});
            hud.RefreshIndicators(sanhua.sub_rule, RoundIndicatorDisplay.Pending);
            Verify("red rewind to pending", new[]{0}, new string[1]);
            var fuzhou = new GameInfo {room_rule="fuzhou", sub_rule="fuzhou/standard", room_type="custom",
                current_round=1, max_round=16, fuzhou_info=new FuzhouInfo {
                    gold_tile=13, dealer_streak=3, reserved_tiles=24, last_four=true,
                    declarations=Enumerable.Range(0,4).Select(_ => new FuzhouDeclaration {golden_middle=true}).ToArray()
                }};
            hud.UpdateRoomInfo(fuzhou, fuzhou.sub_rule);
            VerifyCounters("Fuzhou");
            Canvas.ForceUpdateCanvases();
            var status = panel.GetComponentsInChildren<TMP_Text>().Single(t => t.name=="Status");
            var strip = (RectTransform)status.transform.parent;
            Rect InkBounds(TMP_Text label) {
                label.ForceMeshUpdate();
                var lower = new Vector2(float.PositiveInfinity, float.PositiveInfinity);
                var upper = new Vector2(float.NegativeInfinity, float.NegativeInfinity);
                for (int i = 0; i < label.textInfo.characterCount; i++) {
                    var glyph = label.textInfo.characterInfo[i];
                    if (!glyph.isVisible) continue;
                    var vertices = label.textInfo.meshInfo[glyph.materialReferenceIndex].vertices;
                    for (int corner = 0; corner < 4; corner++) {
                        Vector2 position = strip.InverseTransformPoint(label.transform.TransformPoint(vertices[glyph.vertexIndex + corner]));
                        lower = Vector2.Min(lower, position);
                        upper = Vector2.Max(upper, position);
                    }
                }
                Check(lower.x < upper.x && lower.y < upper.y, label.name + " has no visible glyphs");
                return Rect.MinMaxRect(lower.x, lower.y, upper.x, upper.y);
            }
            var venue = InkBounds(strip.Find("Venue").GetComponent<TMP_Text>());
            var format = InkBounds(strip.Find("GameRound").GetComponent<TMP_Text>());
            Check(venue.yMin >= format.yMax - .1f,
                $"room and match length glyphs overlap or appear out of order: venue {venue}, format {format}");
            status.ForceMeshUpdate();
            Check(strip.rect.height <= 44.1f, "long Fuzhou status increased the player HUD footprint");
            Check(!status.isTextTruncated, "long Fuzhou status truncated its declarations");
            if (ConfigManager.CurrentLanguage == AppLanguage.SimplifiedChinese)
                foreach (string term in new[]{"连庄3","死牌24","东听金坎","南听金坎","西听金坎","北听金坎","末四张"})
                    Check(status.text.Contains(term), "missing public Fuzhou status " + term);
            var glyphs = status.textInfo.characterInfo.Take(status.textInfo.characterCount).Where(c=>c.isVisible).ToArray();
            Check(glyphs.Length == status.text.Count(c=>!char.IsWhiteSpace(c)), "incomplete Fuzhou status glyphs");
            foreach (var glyph in glyphs) {
                Vector3 lower = strip.InverseTransformPoint(status.transform.TransformPoint(glyph.bottomLeft));
                Vector3 upper = strip.InverseTransformPoint(status.transform.TransformPoint(glyph.topRight));
                Check(lower.y>=strip.rect.yMin-1f && upper.y<=strip.rect.yMax+1f &&
                      lower.x>=strip.rect.xMin-1f && upper.x<=strip.rect.xMax+1f,
                      "public status glyph extends beyond the strip");
            }
            riichi.honba = 2;
            riichi.riichi_sticks = 3;
            hud.UpdateRoomInfo(riichi, riichi.sub_rule);
            Verify("riichi after local rules", new[]{24,24,16,0,0}, new string[5], 2, 3);
            return new { checks, failures=Array.Empty<string>() };
        } finally {
            manager.GotoSelectRound(savedRound);
            manager.GotoSelectNode(savedNode);
        }
    }
}
#endif
