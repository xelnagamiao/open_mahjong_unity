using System;
using System.Collections.Generic;
using System.Linq;
#if UNITY_5_3_OR_NEWER
using UnityEngine;
#endif

namespace Qingque13
{
    // 玩家手牌数据类
    public class PlayerTilesTingpai
    {
        public List<int> hand_tiles;
        public List<string> combination_list;
        public int complete_step; // +3 +3 +3 +3 +2 = 14

        public PlayerTilesTingpai(List<int> tiles_list, List<string> combination_list, int complete_step)
        {
            hand_tiles = new List<int>(tiles_list);
            hand_tiles.Sort();
            this.combination_list = new List<string>(combination_list);
            this.complete_step = complete_step;
        }

        public PlayerTilesTingpai DeepCopy()
        {
            return new PlayerTilesTingpai(
                new List<int>(hand_tiles),
                new List<string>(combination_list),
                complete_step
            );
        }
    }

    // 中国麻将听牌检查类
    public class Qingque_Tingpai_Check
    {
        // 十三幺 (国士无双) / 混幺九 检查使用的幺九牌集合
        private static readonly HashSet<int> yaojiu = new HashSet<int> { 11, 19, 21, 29, 31, 39, 41, 42, 43, 44, 45, 46, 47 };

        private HashSet<int> waiting_tiles;
        private bool debug;

        public Qingque_Tingpai_Check(bool debug = false)
        {
            waiting_tiles = new HashSet<int>();
            this.debug = debug;
        }

        private void DebugPrint(string message, params object[] args)
        {
            if (debug)
            {
#if UNITY_5_3_OR_NEWER
                Debug.Log(string.Format(message, args));
#else
                Console.WriteLine(string.Format(message, args));
#endif
            }
        }

        public HashSet<int> CheckWaitingTiles(PlayerTilesTingpai player_tiles)
        {
            // 清空之前的结果
            waiting_tiles.Clear();

            if (player_tiles.hand_tiles.Count == 13)
            {
                GS_check(player_tiles.hand_tiles);  // 十三幺检查
                QD_check(player_tiles.hand_tiles);  // 七对子检查
            }

            normal_check(player_tiles); // 一般型检查

            // Count physical tiles in both the concealed hand and declared melds.
            // Shape decomposition alone can otherwise propose a fifth copy.
            var counts = new Dictionary<int, int>();
            void CountTile(int tile)
            {
                counts.TryGetValue(tile, out int count);
                counts[tile] = count + 1;
            }
            foreach (int tile in player_tiles.hand_tiles) CountTile(tile);
            foreach (string meld in player_tiles.combination_list)
            {
                int tile = int.Parse(meld.Substring(1));
                switch (char.ToLowerInvariant(meld[0]))
                {
                    case 's':
                        CountTile(tile - 1);
                        CountTile(tile);
                        CountTile(tile + 1);
                        break;
                    case 'k':
                    case 'g':
                        int copies = char.ToLowerInvariant(meld[0]) == 'g' ? 4 : 3;
                        for (int i = 0; i < copies; i++) CountTile(tile);
                        break;
                }
            }
            if (counts.Values.Any(count => count > 4)) waiting_tiles.Clear();
            else waiting_tiles.RemoveWhere(tile => counts.TryGetValue(tile, out int count) && count >= 4);

            DebugPrint("等待牌: {0}", string.Join(", ", waiting_tiles));

            return waiting_tiles;
        }

        private void GS_check(List<int> hand_tiles)
        {
            // 检查十三幺 (国士无双)
            // 手牌必须全部是幺九牌 (1,9 和字牌)
            HashSet<int> GS_step_set = new HashSet<int>();
            bool GS_allowed = true;

            foreach (int tile_id in hand_tiles)
            {
                if (yaojiu.Contains(tile_id))
                {
                    GS_step_set.Add(tile_id);
                }
                else
                {
                    GS_allowed = false;
                }
            }

            // 如果牌都是幺九，并且满足12种（听第13种）或13种（听任意幺九牌）
            if (GS_allowed)
            {
                if (GS_step_set.Count == 12)
                {
                    // 12 unique terminal/honour tiles: waiting for the missing 13th
                    foreach (int i in yaojiu)
                    {
                        if (!hand_tiles.Contains(i))
                        {
                            waiting_tiles.Add(i);
                        }
                    }
                }
                else if (GS_step_set.Count == 13)
                {
                    // 13 unique terminal/honour tiles: waiting for any of them (pair wait)
                    foreach (int i in yaojiu)
                    {
                        waiting_tiles.Add(i);
                    }
                }
            }
        }

        private void QD_check(List<int> hand_tiles)
        {
            // 检查七对子
            Dictionary<int, int> tile_counts = new Dictionary<int, int>();

            // 统计每种牌的数量
            foreach (int tile_id in hand_tiles)
            {
                if (tile_counts.ContainsKey(tile_id))
                {
                    tile_counts[tile_id]++;
                }
                else
                {
                    tile_counts[tile_id] = 1;
                }
            }

            int single = 0;
            int? waiting_tile = null;

            foreach (var kvp in tile_counts)
            {
                int tile_id = kvp.Key;
                int count = kvp.Value;
                if (count == 1 || count == 3)
                {
                    single++;
                    waiting_tile = tile_id;
                }
                // 如果有超过2张相同的牌，则不可能是七对子
                else if (single >= 2)
                {
                    return;
                }
            }

            if (single == 1 && waiting_tile.HasValue)
            {
                waiting_tiles.Add(waiting_tile.Value);
            }
        }

        private void normal_check(PlayerTilesTingpai player_tiles)
        {
            if (player_tiles.complete_step + player_tiles.hand_tiles.Count != 13)
            {
                return;
            }
            StandardMahjongSolver.CollectStandardWaitingTiles(
                player_tiles.hand_tiles,
                waiting_tiles,
                allowHonors: true,
                allowFifthCopy: false);
        }

        private bool normal_check_block(PlayerTilesTingpai player_tiles)
        {
            int block_count = player_tiles.combination_list.Count;
            int tile_id_pointer = player_tiles.hand_tiles[0];
            foreach (int tile_id in player_tiles.hand_tiles)
            {
                if (tile_id == tile_id_pointer || tile_id == tile_id_pointer + 1)
                {
                    // pass
                }
                else
                {
                    block_count++;
                }
                tile_id_pointer = tile_id;
            }
            if (block_count > 6)
            {
                return false;
            }
            else
            {
                return true;
            }
        }

        private List<PlayerTilesTingpai> normal_check_traverse_quetou(PlayerTilesTingpai player_tiles)
        {
            List<PlayerTilesTingpai> all_list = new List<PlayerTilesTingpai>();
            int quetou_id_pointer = 0;
            foreach (int tile_id in player_tiles.hand_tiles)
            {
                int count = player_tiles.hand_tiles.Count(x => x == tile_id);
                if (count >= 2 && tile_id != quetou_id_pointer)
                {
                    PlayerTilesTingpai temp_list = player_tiles.DeepCopy();
                    temp_list.hand_tiles.Remove(tile_id);
                    temp_list.hand_tiles.Remove(tile_id);
                    temp_list.complete_step += 2;
                    temp_list.combination_list.Add($"q{tile_id}");
                    all_list.Add(temp_list);
                    quetou_id_pointer = tile_id;
                }
            }
            PlayerTilesTingpai temp_list_no_quetou = player_tiles.DeepCopy();
            all_list.Add(temp_list_no_quetou);
            return all_list;
        }

        private void normal_check_traverse_kezi(PlayerTilesTingpai player_tiles, List<PlayerTilesTingpai> all_list)
        {
            int same_tile_id = 0;
            foreach (int tile_id in player_tiles.hand_tiles)
            {
                int count = player_tiles.hand_tiles.Count(x => x == tile_id);
                if (count >= 3 && tile_id != same_tile_id)
                {
                    PlayerTilesTingpai temp_list = player_tiles.DeepCopy();
                    temp_list.hand_tiles.Remove(tile_id);
                    temp_list.hand_tiles.Remove(tile_id);
                    temp_list.hand_tiles.Remove(tile_id);
                    temp_list.complete_step += 3;
                    temp_list.combination_list.Add($"k{tile_id}");
                    all_list.Add(temp_list);
                    same_tile_id = tile_id;
                }
            }
        }

        private void normal_check_traverse_dazi(PlayerTilesTingpai player_tiles, List<PlayerTilesTingpai> all_list)
        {
            int same_tile_id = 0;
            foreach (int tile_id in player_tiles.hand_tiles)
            {
                // Only suit tiles (11-19, 21-29, 31-39) can form sequences
                // Ensure tile_id+2 doesn't exceed x9 boundary (19, 29, 39) or reach honor tiles (40+)
                if (tile_id <= 37 && (tile_id % 10) <= 7)
                {
                    if (player_tiles.hand_tiles.Contains(tile_id + 1) &&
                        player_tiles.hand_tiles.Contains(tile_id + 2) &&
                        tile_id != same_tile_id)
                    {
                        PlayerTilesTingpai temp_list = player_tiles.DeepCopy();
                        temp_list.hand_tiles.Remove(tile_id);
                        temp_list.hand_tiles.Remove(tile_id + 1);
                        temp_list.hand_tiles.Remove(tile_id + 2);
                        temp_list.complete_step += 3;
                        temp_list.combination_list.Add($"s{tile_id + 1}");
                        all_list.Add(temp_list);
                        same_tile_id = tile_id;
                    }
                }
            }
        }

        // 外部调用时传参手牌、组合 返回听牌集合
        public HashSet<int> TingpaiCheck(List<int> hand_tile_list, List<string> combination_list)
        {
            PlayerTilesTingpai test_tiles = new PlayerTilesTingpai(hand_tile_list, combination_list, combination_list.Count * 3);
            CheckWaitingTiles(test_tiles);
            // 排除 10 20 30 40这四种集合成员
            HashSet<int> exclude_set = new HashSet<int> { 10, 20, 30, 40 };
            waiting_tiles.RemoveWhere(x => exclude_set.Contains(x));
            return new HashSet<int>(waiting_tiles);  // 返回set的副本，避免引用问题
        }
    }

    // 青雀麻将听牌检查入口类
    public static class Qingque13Tingpai
    {
        /// <summary>
        /// 听牌检查方法，类似 Python 版本的 tingpai_check
        /// </summary>
        /// <param name="hand_tile_list">手牌列表</param>
        /// <param name="combination_list">已组成的组合列表</param>
        /// <param name="debug">是否启用调试日志，默认为 false</param>
        /// <returns>返回听牌集合</returns>
        public static HashSet<int> TingpaiCheck(
            List<int> hand_tile_list,
            List<string> combination_list,
            bool debug = false)
        {
            var checker = new Qingque_Tingpai_Check(debug);
            return checker.TingpaiCheck(hand_tile_list, combination_list);
        }
    }
}
