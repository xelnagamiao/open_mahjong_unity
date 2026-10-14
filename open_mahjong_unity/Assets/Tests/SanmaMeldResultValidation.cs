#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.UI;

/// <summary>三人桌副露的来源方向、加杠及结算空席；同时保留四人行为。</summary>
public static class SanmaMeldResultValidation {
    private static readonly BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static T Field<T>(object target, string name) => (T)target.GetType().GetField(name, Private).GetValue(target);

    public static object Run() {
        var checks = new List<string>();
        void Check(bool value, string label) {
            if (!value) throw new InvalidOperationException("Sanma meld/result: " + label);
            checks.Add(label);
        }
        var sides = new[] {"self", "right", "top", "left"};
        foreach (int count in new[] {3, 4})
        for (int viewer = 0; viewer < count; viewer++)
        for (int actor = 0; actor < count; actor++)
        for (int source = 0; source < count; source++) {
            if (actor == source) continue;
            string caller = TableMirror.RelativeSeat(viewer, actor, count);
            string discarder = TableMirror.RelativeSeat(viewer, source, count);
            int direction = (Array.IndexOf(sides, discarder) - Array.IndexOf(sides, caller) + 4) % 4;
            foreach (string action in new[] {"p", "g", "jg"}) {
                int[] hand = action == "g" ? new[] {205, 25, 25} : new[] {205, 25};
                var mask = GameRecordMeldCodec.BuildMingpaiMask(action == "jg" ? "p" : action,
                    25, hand, TableMirror.RelativeSeat(actor, source, count)).ToList();
                if (action == "jg") mask.InsertRange(mask.FindIndex(value => value == 1), new[] {3, 25});
                int[] original = mask.ToArray();
                int[] shown = GameRecordMeldCodec.ForTableDisplay(original, caller, count);
                int[] baseSigns = Enumerable.Range(0, shown.Length / 2).Select(i => shown[i * 2]).Where(s => s != 3).ToArray();
                int expectedSlot = direction == 3 ? 0 : direction == 2 ? 1 : baseSigns.Length - 1;
                string label = count + "/" + viewer + "/" + actor + "/" + source + "/" + action;
                Check(Array.IndexOf(baseSigns, 1) == expectedSlot, label + " physical source");
                Check(original.SequenceEqual(mask), label + " authoritative mask unchanged");
                Check(Enumerable.Range(0, shown.Length / 2).Select(i => shown[i * 2 + 1]).OrderBy(i => i)
                    .SequenceEqual(Enumerable.Range(0, original.Length / 2).Select(i => original[i * 2 + 1]).OrderBy(i => i)),
                    label + " exact/red tiles preserved");
                if (action == "jg") {
                    int added = Array.IndexOf(shown, 3);
                    Check(shown[added + 2] == 1, label + " added kong follows original source");
                }
            }
        }
        foreach (int[] special in new[] {new[] {2,25,2,25,2,25,2,25}, new[] {0,25,101,25,1,25,0,25}, new[] {0,25,1}})
            Check(ReferenceEquals(special, GameRecordMeldCodec.ForTableDisplay(special, "right", 3)), "special mask unchanged " + special.Length);
        return new {passed = checks.Count, checks};
    }

#if UNITY_EDITOR
    public static object RunSceneContracts() {
        if (UnityEditor.EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Run scene contracts outside Play Mode.");
        var source = Resources.FindObjectsOfTypeAll<EndResultPanel>().First(p => p.gameObject.scene.IsValid()
            && !UnityEditor.SceneManagement.EditorSceneManager.IsPreviewScene(p.gameObject.scene));
        var preview = UnityEditor.SceneManagement.EditorSceneManager.NewPreviewScene();
        var originalManager = NormalGameStateManager.Instance;
        var instanceProperty = typeof(NormalGameStateManager).GetProperty("Instance");
        var originalMap = new Dictionary<int, string>(TableMirror.Current.IndexToPosition);
        var checks = new List<string>();
        try {
            var holder = new GameObject("SanmaMeldResultValidation");
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(holder, preview);
            var panel = UnityEngine.Object.Instantiate(source, holder.transform);
            var manager = holder.AddComponent<NormalGameStateManager>();
            instanceProperty.SetValue(null, manager);
            var recordPopulate = typeof(EndResultPanel).GetMethod("PopulateRecordNamesHandsAndScores", Private);
            var applyLive = typeof(EndResultPanel).GetMethod("ApplySeatVisuals", Private);
            // Repeated three/four switches also verify stale hidden seats are restored.
            foreach (string rule in new[] {"riichi/sanma", "guobiao/sanma"})
            foreach (int count in new[] {4, 3, 4, 3})
            for (int viewer = 0; viewer < count; viewer++) {
                var map = Enumerable.Range(0, count).ToDictionary(i => i, i => TableMirror.RelativeSeat(viewer, i, count));
                manager.indexToPosition.Clear();
                foreach (var pair in map) manager.indexToPosition[pair.Key] = pair.Value;
                foreach (bool replay in new[] {false, true}) {
                    if (replay) {
                        recordPopulate.Invoke(panel, new object[] {0, new RecordSettlementView {
                            IndexToPosition = map, PositionToUsername = map.Values.ToDictionary(p => p, p => "QA " + p)
                        }, null, null, EndResultPanel.EndResultTileLayout.HuWithWinTile});
                    } else applyLive.Invoke(panel, null);
                    foreach (string side in TableMirror.Seats) {
                        string field = char.ToUpperInvariant(side[0]) + side.Substring(1) + "PanelBackground";
                        var root = Field<Image>(panel, field).gameObject;
                        if (root.activeSelf != map.ContainsValue(side))
                            throw new InvalidOperationException(rule + " " + count + " " + replay + " " + side);
                        checks.Add(rule + "/" + count + "/" + viewer + "/" + replay + "/" + side);
                    }
                }
            }
            return new {passed = checks.Count, checks};
        } finally {
            instanceProperty.SetValue(null, originalManager);
            TableMirror.Current.IndexToPosition.Clear();
            foreach (var pair in originalMap) TableMirror.Current.IndexToPosition[pair.Key] = pair.Value;
            UnityEditor.SceneManagement.EditorSceneManager.ClosePreviewScene(preview);
        }
    }
#endif
}
#endif
