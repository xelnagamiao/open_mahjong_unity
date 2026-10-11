#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>三人桌保留四边几何：座位、空席清理、计分板及牌谱副露来源回归。</summary>
public static class SanmaSeatingValidation {
    public static object Run() {
        var checks = new List<string>();
        void Check(bool condition, string label) {
            if (!condition) throw new InvalidOperationException("Sanma seating: " + label);
            checks.Add(label);
        }
        string[][] expected = {
            new[] {"self", "right", "top", "left"}, new[] {"left", "self", "right", "top"},
            new[] {"top", "left", "self", "right"}, new[] {"right", "top", "left", "self"}
        };
        var mirror = new TableMirror();
        string previousSubRule = GameSession.Current.SubRule;
        try {
            foreach (string rule in new[] {"riichi", "guobiao"}) {
                foreach (int count in new[] {3, 4}) {
                    GameSession.Current.SubRule = rule + (count == 3 ? "/sanma" : "/standard");
                    for (int viewer = 0; viewer < count; viewer++) {
                        // Reuse the same mirror and deliberately seed every side with stale four-player data.
                        foreach (string pos in TableMirror.Seats) {
                            mirror.PlayerToInfo[pos] = new PlayerInfoClass {
                                username = "stale", original_player_index = 3, hand_tiles_count = 13,
                                discard_tiles = new List<int> {44}, huapai_list = new List<int> {44},
                                combination_tiles = new List<string> {"k45"},
                                combination_masks = new List<int[]> {new[] {0,45,1,45,0,45}}
                            };
                        }
                        mirror.BuildSeatMap(viewer, Enumerable.Range(0, count).Reverse().ToArray());
                        string label = rule + " " + count + " viewer " + viewer;
                        Check(mirror.IndexToPosition.Count == count && Enumerable.Range(0, count)
                            .All(i => mirror.IndexToPosition[i] == expected[viewer][i]), label + " snapshot map");
                        foreach (var pair in mirror.IndexToPosition)
                            mirror.Info(pair.Value).original_player_index = pair.Key;
                        mirror.ClearUnoccupiedSeatData();
                        if (count == 3) {
                            string empty = expected[viewer][3];
                            var info = mirror.Info(empty);
                            Check(info.original_player_index == -1 && info.hand_tiles_count == 0
                                && string.IsNullOrEmpty(info.username) && info.discard_tiles.Count == 0
                                && info.huapai_list.Count == 0 && info.combination_masks.Count == 0,
                                label + " clears " + empty);
                        }
                        Check(ScoreHistoryPanel.SelectScoreboardPlayers(mirror.PlayerToInfo, count == 3)
                            .Select(p => p.original_player_index).SequenceEqual(Enumerable.Range(0, count)),
                            label + " scoreboard identities");
                        mirror.BuildSeatMap(viewer);
                        Check(mirror.IndexToPosition.Count == count && Enumerable.Range(0, count)
                            .All(i => mirror.IndexToPosition[i] == expected[viewer][i]), label + " fallback map");
                    }
                }
            }
            for (int actor = 0; actor < 4; actor++) {
                for (int source = 0; source < 4; source++) {
                    if (actor == source) continue;
                    string direction = expected[actor][source];
                    Check(TableMirror.RelativeSeat(actor, source) == direction, "source direction " + actor + "/" + source);
                    foreach (string action in new[] {"p", "cl", "cm", "cr", "g"}) {
                        int[] hand = action == "g" ? new[] {105,15,15} : new[] {105,15};
                        int[] mask = GameRecordMeldCodec.BuildMingpaiMask(action, 15, hand, direction);
                        int slot = direction == "left" ? 0 : direction == "top" ? 1 : hand.Length;
                        string label = action + " actor " + actor + " source " + source;
                        Check(mask[slot * 2] == 1 && mask[slot * 2 + 1] == 15
                            && Enumerable.Range(0, mask.Length / 2).Count(i => mask[2*i] == 1) == 1,
                            label + " called tile position");
                        Check(GameRecordMeldCodec.ExtractHandTilesFromMask(mask).SequenceEqual(hand),
                            label + " physical hand IDs preserved");
                    }
                }
            }
            foreach (string family in new[] {"ttsmaker_204_xiaoxiao", "ttsmaker_1513_qiuqiu"}) {
                var clip = Resources.Load<AudioClip>("Sound/" + family + "/nuki");
                Check(clip != null && clip.length > 0.1f, family + " north voice imported");
            }
            return new {passed = checks.Count, checks};
        } finally { GameSession.Current.SubRule = previousSubRule; }
    }
}
#endif
