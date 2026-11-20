#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>战术鸣牌申请是独立的可见回放步；跳转重建时仅忽略表现，不修改牌面。</summary>
public static class RecordTacticalClaimValidation {
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;

    public static object Run() {
        if (Application.isPlaying) throw new InvalidOperationException("Run in Edit Mode.");
        var checks = new List<string>();
        void Check(bool ok, string label) {
            if (!ok) throw new InvalidOperationException(label);
            checks.Add(label);
        }
        var scene = EditorSceneManager.NewPreviewScene();
        var root = new GameObject("Tactical claim replay validation");
        root.SetActive(false);
        UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(root, scene);
        var manager = root.AddComponent<GameRecordManager>();
        var table = root.AddComponent<Game3DManager>();
        var tableInstance = typeof(Game3DManager).GetProperty("Instance");
        var previousTable = Game3DManager.Instance;
        object Call(string method, params object[] args) => typeof(GameRecordManager).GetMethod(method, Hidden).Invoke(manager, args);
        try {
            tableInstance.SetValue(null, table);
            // HewPNHpZAR，第 13 局：座位 2 打八筒，座位 3 申请吃，座位 1 最终碰。
            var claim = new List<string> { "ca", "3", "cm", "28" };
            var round = new Round { actionTicks = new List<List<string>> {
                new List<string> { "reset", "2" },
                new List<string> { "c", "28", "F" },
                new List<string> { "ask_other" }, claim,
                new List<string> { "p", "28", "1", "28", "28" }
            } };
            var record = new GameRecord { gameTitle = new Dictionary<string, object> {
                ["rule"] = "guobiao", ["sub_rule"] = "guobiao/standard"
            } };
            record.gameRound.rounds[13] = round;
            typeof(GameRecordManager).GetProperty("gameRecord").SetValue(manager, record);
            manager.currentRoundIndex = 13;
            manager.currentNode = 2;
            manager.currentPlayerIndex = 3;
            Call("ConsumeRecordSilentTicks", round);
            Check(manager.currentNode == 3, "Silent asks stop before the tactical chi declaration");
            Check((string)Call("PeekNextTickAction") == "ca", "Playback sees chi before the winning peng");
            Check(GameRecordJsonDecoder.ResolveRecordActingPlayerIndex(claim, "ca", 0) == 3,
                "Declaration actor is the applicant, not the discarder or winning claimant");
            Check((int)Call("FindPreviousUserStepNode", 4) == 2, "Back one step returns before chi and its silent ask");
            Check((int)Call("FindPreviousUserStepNode", 5) == 4, "Back from peng preserves the preceding chi step");
            Check((float)Call("GetSpectatorDelay", "ca") > 0f, "Spectator playback gives declarations visible time");
            var delay = typeof(GameRecordManager).GetMethod("GetRecordAutoPlayDelay", BindingFlags.Static | BindingFlags.NonPublic);
            Check((float)delay.Invoke(null, new object[] { "ca" }) >= 0.6f, "Automatic replay pauses after the declaration");

            // 出牌的飞行/收拢尚未结束时，宣告点击也必须立即受理；最终碰仍须等待可认走的弃牌。
            var animations = (Dictionary<string, int>)typeof(Game3DManager)
                .GetField("_recordHandAnimationsByPlayer", Hidden).GetValue(table);
            manager.indexToPosition[1] = "left";
            manager.indexToPosition[2] = "top";
            manager.indexToPosition[3] = "right";
            typeof(GameRecordManager).GetField("lastDiscardPlayerIndex", Hidden).SetValue(manager, 2);
            foreach (string seat in new[] { "self", "left", "top", "right" }) {
                animations[seat] = 1;
                Check(table.HasPendingRecordTableAnimations, seat + ": regression starts with an unfinished animation");
                Check((bool)Call("CanAnimateNextRecordStep", false), seat + ": animation never blocks the tactical declaration");
                animations.Clear();
            }
            animations["top"] = 1;
            manager.currentNode = 4;
            Check(!(bool)Call("CanAnimateNextRecordStep", false), "The final peng still waits for the discard to become claimable");
            animations.Clear();
            Check((bool)Call("CanAnimateNextRecordStep", false), "The final peng can proceed after the discard animation");
            manager.currentNode = 3;

            var player = new GameRecordManager.RecordPlayer { playerIndex = 3, tileList = new List<int> { 27, 29 },
                discardTiles = new List<int> { 11 }, combinationTiles = new List<string> { "k41" } };
            manager.recordPlayer_to_info["right"] = player;
            manager.indexToPosition[3] = "right";
            foreach (string action in new[] { "cl", "cm", "cr", "p", "g", "hu_first" }) {
                var tick = new List<string> { "ca", "1", action, "28" };
                Check(!(bool)Call("IsRecordSilentTick", tick), action + ": unsuccessful claim remains visible");
                Call("ApplyActionToRecordState", tick);
                Check(manager.currentPlayerIndex == 3 && player.tileList.Count == 2
                    && player.discardTiles.Count == 1 && player.combinationTiles.Count == 1,
                    action + ": seeking across a declaration does not execute it");
            }
            round.actionTicks.RemoveAt(3);
            manager.currentNode = 2;
            Call("ConsumeRecordSilentTicks", round);
            Check((string)Call("PeekNextTickAction") == "p", "Records without a tactical claim still go straight to peng");
            return new { passed = checks.Count, checks };
        } finally {
            tableInstance.SetValue(null, previousTable);
            EditorSceneManager.ClosePreviewScene(scene);
        }
    }
}
#endif
