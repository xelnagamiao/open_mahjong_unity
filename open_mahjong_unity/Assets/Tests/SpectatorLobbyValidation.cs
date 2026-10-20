#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

public static class SpectatorLobbyValidation {
    public static string Run() {
        int checks = 0;
        void Check(bool value, string label) { if (!value) throw new Exception(label); checks++; }
        SpectatorInfo Ranked(string queue) => new SpectatorInfo { room_type = "match", match_queue_type = queue };
        var custom = new SpectatorInfo { room_type = "custom", match_queue_type = "advanced_quanzhuang" };
        Check(!SpectatorRoomPresentation.IsRanked(custom), "explicit custom rooms ignore stale queue metadata");
        Check(!SpectatorRoomPresentation.IsRanked(new SpectatorInfo()), "legacy payload compatibility");
        var ordered = new[] { custom, Ranked("beginner_quanzhuang"), Ranked("riichi_intermediate_banzhuang"),
            Ranked("mcrpl_quanzhuang"), Ranked("riichi_sanma_advanced_dongfeng"), Ranked("sichuan_xueliu_exchange_elo_quanzhuang") }
            .OrderByDescending(SpectatorRoomPresentation.Priority).ToArray();
        Check(ordered.Select(SpectatorRoomPresentation.Tier).SequenceEqual(new[] { "mcrpl", "advanced", "intermediate", "beginner", "elo", "" }), "descending tier order including prefixed queues");
        var doubles = new SpectatorInfo { rule = "sichuan", sub_rule = "sichuan/xueliu_doubles", room_type = "custom",
            player1_name = "Red A", player2_name = "<b>Blue A</b>", player3_name = "Red B", player4_name = "Blue B",
            team_by_player_index = new[] { 1, 0, 1, 0 }, gamestate_id = "teams" };
        Check(SpectatorRoomPresentation.HasTeams(doubles), "authoritative 2v2 roster");
        Check(SpectatorRoomPresentation.Matches(doubles, SpectatorRoomPresentation.RoomFilter.Custom, "", SpectatorRoomPresentation.FormatFilter.Doubles), "custom doubles filter");
        Check(!SpectatorRoomPresentation.Matches(doubles, SpectatorRoomPresentation.RoomFilter.Ranked, "", SpectatorRoomPresentation.FormatFilter.All), "ranked excludes custom");
        Check(!SpectatorRoomPresentation.Matches(doubles, SpectatorRoomPresentation.RoomFilter.All, "advanced", SpectatorRoomPresentation.FormatFilter.All), "tier excludes unranked");
        Check(!SpectatorRoomPresentation.Matches(doubles, SpectatorRoomPresentation.RoomFilter.All, "", SpectatorRoomPresentation.FormatFilter.Individual), "individual excludes doubles");
        Check(SpectatorRoomPresentation.Tier(Ranked("riichi_sanma_intermediate_banzhuang")) == "intermediate", "sanma queue parsing");
        Check(SpectatorRoomPresentation.Priority(Ranked("future_tier")) == 0, "unknown ranked tiers remain above custom");
        Check(!SpectatorRoomPresentation.HasTeams(new SpectatorInfo { sub_rule = "sichuan/doubles" }), "missing team data never guessed");

        foreach (var type in typeof(RuleRegistry).Assembly.GetTypes().Where(t => t.Name.EndsWith("RuleBootstrap")))
            type.GetMethod("Register", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)?.Invoke(null, null);
        var progressInfo = Newtonsoft.Json.JsonConvert.DeserializeObject<SpectatorInfo>(
            "{\"rule\":\"guobiao\",\"sub_rule\":\"guobiao/standard\",\"gamestate_id\":\"round-test\",\"current_round\":2,\"total_rounds\":16}");
        Check(SpectatorRoomPresentation.RoundProgress(progressInfo) == "东风南/全庄战", "wind-seat rule keeps its round name and match length");
        foreach (var sample in new[] {
            ("guobiao", 1, 16, "东风东/全庄战"),
            ("guobiao/sanma", 4, 12, "南风东/全庄战"),
            ("guobiao/transparent", 2, 16, "东风南/全庄战"),
            ("yixing", 2, 16, "东风南/全庄战"),
            ("wenzhou", 2, 16, "东风南/全庄战"),
            ("riichi", 1, 8, "东一/东南战"),
            ("riichi/standard", 2, 8, "东二/东南战"),
            ("riichi/sanma", 4, 6, "南一/东南战"),
            ("riichi/washizu", 5, 8, "南一/东南战"),
            ("riichi/standard", 9, 8, "西一/东南战"),
            ("qingque", 2, 8, "东二/东南战"),
            ("taiwan", 2, 8, "东二/东南战"),
            ("sichuan", 2, 16, "第2/16副"),
            ("wuhan", 2, 16, "第2/16副"),
            ("hongque", 2, 8, "第2/8局"),
            ("shanxi", 2, 16, "第2/16庄"),
            ("sichuan", 19, 0, "第19/不限副"),
            ("guobiao", 2, 0, "东风南/不限"),
        }) {
            var info = new SpectatorInfo { rule = sample.Item1.Split('/')[0], sub_rule = sample.Item1,
                current_round = sample.Item2, total_rounds = sample.Item3 };
            Check(SpectatorRoomPresentation.RoundProgress(info) == sample.Item4, sample.Item1 + " round display " + sample.Item4);
        }
        Check(SpectatorRoomPresentation.RoundProgress(new SpectatorInfo { rule = "sichuan", current_round = 2 }) == "第2/—副", "unknown count total stays unknown");
        Check(SpectatorRoomPresentation.RoundProgress(new SpectatorInfo { rule = "guobiao", current_round = 2 }) == "东风南/—", "unknown match length stays unknown");
        Check(SpectatorRoomPresentation.RoundProgress(new SpectatorInfo()) == "—/—", "legacy payload has no invented progress");

        var parent = new GameObject("SpectatorValidation");
        parent.SetActive(false);
        try {
            foreach (string path in new[] { "Assets/Prefabs/Room/SpectatoItem.prefab", "Assets/Prefabs/Room/SpectatorTierHeader.prefab", "Assets/Prefabs/Room/SpectatorSoftCard.prefab" }) {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                Check(prefab != null, path + " exists");
                var row = UnityEngine.Object.Instantiate(prefab, parent.transform).GetComponent<SpectatorPrefab>();
                var fields = new SerializedObject(row);
                TextMeshProUGUI Label(string key) => (TextMeshProUGUI)fields.FindProperty(key).objectReferenceValue;
                row.InitializeSpectatorItem(doubles);
                Check(Label("Player1NameText").text == "Red A" && Label("Player2NameText").text == "<b>Blue A</b>" &&
                    !Label("Player2NameText").richText && Label("Player3NameText").text == "Red B" &&
                    Label("Player4NameText").text == "Blue B", "original player order and plain names preserved");
                var teamColors = fields.FindProperty("playerTeamBackgrounds");
                var backgrounds = Enumerable.Range(0, 4).Select(i => (Image)teamColors.GetArrayElementAtIndex(i).objectReferenceValue).ToArray();
                Check(backgrounds.All(image => image.gameObject.activeSelf) && backgrounds[0].color == backgrounds[2].color &&
                    backgrounds[1].color == backgrounds[3].color && backgrounds[0].color != backgrounds[1].color,
                    "player cells follow authoritative teams after seat rotation");
                Check(backgrounds[0].color.r > backgrounds[0].color.b && backgrounds[1].color.b > backgrounds[1].color.r,
                    "red and blue colors match server team ids");
                Check(row.GetComponentsInChildren<TextMeshProUGUI>(true).Length == 8 &&
                    Mathf.Abs(((RectTransform)row.transform).rect.height - 90.8435f) < .01f &&
                    Mathf.Abs(((RectTransform)row.transform).rect.width - 945.15f) < .01f,
                    "original card width and height retained");
                row.InitializeSpectatorItem(progressInfo);
                Check(Label("RoundProgressText").text == "东风南/全庄战", "prefab binds public round progress");
                Check(Label("RuleText").rectTransform.anchorMax.x == .25f &&
                    Label("GamestateIdText").rectTransform.anchorMin.x == .25f &&
                    Label("GamestateIdText").rectTransform.anchorMax.x == .75f &&
                    Label("RoundProgressText").rectTransform.anchorMin.x == .75f,
                    "rule, game id and round progress occupy requested quarter boundaries");
                Check(!Label("RuleText").enableAutoSizing && Label("RuleText").fontSize == 25 &&
                    !Label("GamestateIdText").enableAutoSizing && Label("GamestateIdText").fontSize == 23,
                    "original fixed rule and game-id font sizes retained");
                Check(!Label("RoundProgressText").raycastTarget, "progress label does not intercept spectate clicks");
                row.InitializeSpectatorItem(new SpectatorInfo { rule = "guobiao", sub_rule = "guobiao/standard", room_type = "match", match_tier = "advanced", gamestate_id = "rank" });
                var rail = (Image)fields.FindProperty("accentRail").objectReferenceValue;
                Check(rail.gameObject.activeSelf && rail.color == (Color)new Color32(255, 143, 0, 255), "ranked stripe uses existing tier palette");
                Check(backgrounds.All(image => !image.gameObject.activeSelf), "ordinary room clears team colors on reuse");
                row.InitializeSpectatorItem(custom);
                Check(Label("RoundProgressText").text == "—/—", "reusing row clears stale round progress");
                Check(row.GetComponent<UnityEngine.UI.Image>().color == (Color)new Color32(82, 82, 82, 255), "custom rooms retain original grey in every variant");
                Check(!((UnityEngine.UI.Image)fields.FindProperty("accentRail").objectReferenceValue).gameObject.activeSelf, "custom rooms have no ranked stripe");
                Check(!((UnityEngine.UI.Button)fields.FindProperty("SpectateButton").objectReferenceValue).interactable, "missing game id disables spectate");
            }
        } finally { UnityEngine.Object.DestroyImmediate(parent); }
        return checks + " spectator lobby checks passed";
    }
}
#endif
