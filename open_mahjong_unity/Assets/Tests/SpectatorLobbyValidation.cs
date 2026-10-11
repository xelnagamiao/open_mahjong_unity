#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
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
                Check(row.GetComponentsInChildren<TextMeshProUGUI>(true).Length == 7 &&
                    Mathf.Abs(((RectTransform)row.transform).rect.height - 90.8435f) < .01f,
                    "original seven texts and row height retained");
                row.InitializeSpectatorItem(new SpectatorInfo { rule = "guobiao", sub_rule = "guobiao/standard", room_type = "match", match_tier = "advanced", gamestate_id = "rank" });
                var rail = (Image)fields.FindProperty("accentRail").objectReferenceValue;
                Check(rail.gameObject.activeSelf && rail.color == (Color)new Color32(255, 143, 0, 255), "ranked stripe uses existing tier palette");
                Check(backgrounds.All(image => !image.gameObject.activeSelf), "ordinary room clears team colors on reuse");
                row.InitializeSpectatorItem(custom);
                Check(row.GetComponent<UnityEngine.UI.Image>().color == (Color)new Color32(82, 82, 82, 255), "custom rooms retain original grey in every variant");
                Check(!((UnityEngine.UI.Image)fields.FindProperty("accentRail").objectReferenceValue).gameObject.activeSelf, "custom rooms have no ranked stripe");
                Check(!((UnityEngine.UI.Button)fields.FindProperty("SpectateButton").objectReferenceValue).interactable, "missing game id disables spectate");
            }
        } finally { UnityEngine.Object.DestroyImmediate(parent); }
        return checks + " spectator lobby checks passed";
    }
}
#endif
