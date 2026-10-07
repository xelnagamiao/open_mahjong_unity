#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>在预览场景中验证房间预制体及 MainScene 的罚符面板副本，不保存当前场景。</summary>
public static class SanmaRoomDrawValidation {
    private static T Field<T>(object target, string name) =>
        (T)target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(target);

    public static object Run() {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Run sanma UI validation with Play Mode stopped.");
        var checks = new List<string>();
        void Check(bool condition, string name) {
            if (!condition) throw new InvalidOperationException("Sanma UI: " + name);
            checks.Add(name);
        }
        var source = Resources.FindObjectsOfTypeAll<PenaltyPanel>().First(p => p.gameObject.scene.IsValid()
            && !EditorSceneManager.IsPreviewScene(p.gameObject.scene));
        var preview = EditorSceneManager.NewPreviewScene();
        var previousConfig = ConfigManager.Instance;
        var configInstance = typeof(ConfigManager).GetProperty("Instance");
        try {
            var holder = new GameObject("SanmaUiValidation");
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(holder, preview);
            configInstance.SetValue(null, holder.AddComponent<ConfigManager>());
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Room/RoomItem.prefab");
            var roomObject = (GameObject)PrefabUtility.InstantiatePrefab(prefab, preview);
            var roomItem = roomObject.GetComponent<RoomItem>();
            foreach (var spec in new[] {
                ("guobiao", "guobiao/sanma", 3), ("riichi", "riichi/sanma", 3),
                ("riichi", "riichi/standard", 4), ("guobiao", "guobiao/sanma", 0),
                ("riichi", "riichi/sanma", 0), ("guobiao", "guobiao/standard", 4),
            }) {
                int capacity = spec.Item3 > 0 ? spec.Item3 : 3;
                roomItem.SetRoomInfo(new RoomInfo {
                    room_rule = spec.Item1, sub_rule = spec.Item2, max_player = spec.Item3,
                    room_id = "sanma-ui-validation", player_list = new[] {101, 102}, host_user_id = 101,
                });
                for (int seat = 0; seat < 4; seat++)
                    Check(roomObject.transform.Find("PlayerSeat" + (seat + 1)).gameObject.activeSelf == (seat < capacity),
                        spec.Item2 + " capacity " + spec.Item3 + " seat " + seat);
                var tooltip = roomObject.GetComponentInChildren<RoomInfoTooltip>(true);
                string playerDetails = Field<string>(tooltip, "body").Split(new[] {"完整房间配置"}, StringSplitOptions.None)[0];
                Check(playerDetails.Contains("\n4  ") == (capacity == 4), spec.Item2 + " details capacity " + spec.Item3);
                Check(Field<TMP_Text[]>(roomItem, "playerNames")[2].text == "等待加入", spec.Item2 + " third vacancy");
            }

            var panel = UnityEngine.Object.Instantiate(source, holder.transform);
            string[] positions = {"self", "right", "left"};
            var finishAnimation = typeof(PenaltyPanel).GetMethod("RunAfterDrawNotenThreePhase", BindingFlags.Instance | BindingFlags.NonPublic);
            TMP_Text topName = Field<TMP_Text>(panel, "topUserName");
            for (int viewer = 0; viewer < 3; viewer++) {
                for (int mask = 0; mask < 8; mask++) {
                    int tenpaiCount = Enumerable.Range(0, 3).Count(seat => (mask & (1 << seat)) != 0);
                    var users = new Dictionary<string, string>();
                    var after = new Dictionary<string, int>();
                    var deltas = new Dictionary<string, int>();
                    for (int relative = 0; relative < 3; relative++) {
                        int seat = (viewer + relative) % 3;
                        bool tenpai = (mask & (1 << seat)) != 0;
                        int delta = tenpaiCount == 0 || tenpaiCount == 3 ? 0
                            : tenpai ? 3000 / tenpaiCount : -3000 / (3 - tenpaiCount);
                        string pos = positions[relative];
                        users[pos] = "Player " + seat;
                        deltas[pos] = delta;
                        after[pos] = 35000 + delta;
                    }
                    string label = "viewer " + viewer + " tenpai " + mask;
                    panel.PreparePenaltyPanel(users, after, deltas, PenaltyPresentation.AfterDrawNotenThreePhase);
                    Check(!topName.transform.parent.gameObject.activeSelf && topName.text == "", label + " top hidden and cleared");
                    foreach (string pos in positions) {
                        TMP_Text username = Field<TMP_Text>(panel, pos + "UserName");
                        Check(username.transform.parent.gameObject.activeSelf && username.text == users[pos], label + " " + pos + " name");
                        Check(Field<TMP_Text>(panel, pos + "Score").text == "35000", label + " " + pos + " before");
                        Check(Field<TMP_Text>(panel, pos + "Delta").text == (deltas[pos] > 0 ? "+" : "") + deltas[pos], label + " " + pos + " delta");
                    }
                    float duration = Time.deltaTime > 0f ? 6f * Time.deltaTime : 0f;
                    var animation = (IEnumerator)finishAnimation.Invoke(panel, new object[] {users, after, deltas, duration});
                    bool sawIntermediateScore = false;
                    int steps = 0;
                    while (animation.MoveNext()) {
                        if (++steps > 20) throw new InvalidOperationException("Sanma UI animation did not finish.");
                        if (topName.transform.parent.gameObject.activeSelf)
                            throw new InvalidOperationException("Sanma UI animation restored the empty top row.");
                        foreach (string pos in positions) {
                            int shown = int.Parse(Field<TMP_Text>(panel, pos + "Score").text);
                            sawIntermediateScore |= shown != 35000 && shown != after[pos];
                        }
                    }
                    Check(duration == 0f || tenpaiCount == 0 || tenpaiCount == 3 || sawIntermediateScore,
                        label + " three-player score interpolation");
                    foreach (string pos in positions)
                        Check(Field<TMP_Text>(panel, pos + "Score").text == after[pos].ToString()
                            && Field<TMP_Text>(panel, pos + "Delta").text == "0", label + " " + pos + " after animation");
                    Check(!topName.transform.parent.gameObject.activeSelf, label + " top remains hidden");
                    panel.PreparePenaltyPanel(users, after, deltas);
                    Check(!topName.transform.parent.gameObject.activeSelf
                        && Field<TMP_Text>(panel, "rightUserName").text == users["right"], label + " standard three rows");
                }
            }
            var fourUsers = new Dictionary<string, string> { ["self"] = "Self", ["left"] = "Left", ["top"] = "Top", ["right"] = "Right" };
            var fourScores = fourUsers.Keys.ToDictionary(pos => pos, pos => 25000);
            var fourDeltas = fourUsers.Keys.ToDictionary(pos => pos, pos => 0);
            foreach (var mode in new[] {PenaltyPresentation.Standard, PenaltyPresentation.AfterDrawNotenThreePhase}) {
                panel.PreparePenaltyPanel(fourUsers, fourScores, fourDeltas, mode);
                Check(topName.transform.parent.gameObject.activeSelf && topName.text == "Top", mode + " four rows restored");
                var fourAnimation = (IEnumerator)finishAnimation.Invoke(panel, new object[] {fourUsers, fourScores, fourDeltas, 0f});
                while (fourAnimation.MoveNext()) { }
                Check(Field<TMP_Text>(panel, "topScore").text == "25000", mode + " four-player animation");
                var threeUsers = fourUsers.Where(pair => pair.Key != "top").ToDictionary(pair => pair.Key, pair => pair.Value);
                panel.PreparePenaltyPanel(threeUsers, fourScores, fourDeltas, mode);
                Check(!topName.transform.parent.gameObject.activeSelf && Field<TMP_Text>(panel, "topScore").text == ""
                    && Field<TMP_Text>(panel, "topDelta").text == "", mode + " four to three clears stale row");
            }
            return new {passed = checks.Count, checks};
        } finally {
            configInstance.SetValue(null, previousConfig);
            EditorSceneManager.ClosePreviewScene(preview);
        }
    }
}
#endif
