using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

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

    // 青雀麻将听牌检查类（去除 全不靠、组合龙、十三幺）
    public class Qingque_Tingpai_Check
    {
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
                Debug.Log(string.Format(message, args));
            }
        }

        public HashSet<int> CheckWaitingTiles(PlayerTilesTingpai player_tiles)
        {
            // 清空之前的结果
            waiting_tiles.Clear();

            // 13张牌检查特殊牌型（仅保留七对子）
            if (player_tiles.hand_tiles.Count == 13)
            {
                QD_check(player_tiles.hand_tiles);  // 七对子检查
            }

            normal_check(player_tiles); // 一般型检查

            DebugPrint("等待牌: {0}", string.Join(", ", waiting_tiles));

            return waiting_tiles;
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
            // 为节约性能 如果卡牌有不相邻的七组卡牌 说明无法和牌 直接返回False
            if (!normal_check_block(player_tiles))
            {
                return;
            }
            // 获取所有的雀头可能以及没有雀头的情况

            DebugPrint("手牌: {0}", string.Join(", ", player_tiles.hand_tiles));

            List<PlayerTilesTingpai> all_list = normal_check_traverse_quetou(player_tiles);
            List<PlayerTilesTingpai> end_list = new List<PlayerTilesTingpai>();
            DebugPrint("所有列表: {0}", string.Join(" | ", all_list.Select(x => $"[{string.Join(",", x.hand_tiles)}]")));
            // 345567
            int count_count = 0;
            while (all_list.Count > 0)
            {
                count_count++;
                PlayerTilesTingpai temp_list = all_list[all_list.Count - 1];
                all_list.RemoveAt(all_list.Count - 1);
                // 使用temp_list而不是player_tiles
                normal_check_traverse_kezi(temp_list, all_list);
                normal_check_traverse_dazi(temp_list, all_list);
                if (temp_list.complete_step >= 11)
                {
                    end_list.Add(temp_list);
                }
            }
            DebugPrint("计算次数：{0}", count_count);
            foreach (var i in end_list)
            {
                DebugPrint("手牌: {0}, 胡牌步数: {1}, 胡牌组合: {2}",
                    string.Join(", ", i.hand_tiles),
                    i.complete_step,
                    string.Join(", ", i.combination_list));
            }

            // 只保留complete_step大于等于11的列表
            end_list = end_list.Where(i => i.complete_step >= 11).ToList();
            DebugPrint("处理后的列表: {0}", string.Join(" | ", end_list.Select(x => $"[{string.Join(",", x.hand_tiles)}]")));
            DebugPrint("列表长度: {0}", end_list.Count);

            // 剩余的手牌有四种组成方式
            // 1.单吊听牌型(无雀头型)[n] 2.有雀头剩余对子型(对碰)[n,n] 3.剩余两面型[n,n+1] 4.剩余坎张型[n,n+2]
            if (end_list.Count > 0)
            {
                List<int> waiting_tiles_list = new List<int>();
                foreach (var i in end_list)
                {
                    DebugPrint("手牌: {0}, 步数: {1}, 组合: {2}",
                        string.Join(", ", i.hand_tiles),
                        i.complete_step,
                        string.Join(", ", i.combination_list));

                    if (i.hand_tiles.Count == 1)
                    {
                        waiting_tiles_list.Add(i.hand_tiles[0]); // 单吊型
                    }
                    else if (i.hand_tiles.Count == 2)
                    {
                        if (i.hand_tiles[0] == i.hand_tiles[1])
                        {
                            waiting_tiles_list.Add(i.hand_tiles[0]); // 对碰型
                        }
                        else if (i.hand_tiles[0] == i.hand_tiles[1] - 1)
                        {
                            waiting_tiles_list.Add(i.hand_tiles[0] - 1);
                            waiting_tiles_list.Add(i.hand_tiles[0] + 2); // 两面型
                        }
                        else if (i.hand_tiles[0] == i.hand_tiles[1] - 2)
                        {
                            waiting_tiles_list.Add(i.hand_tiles[0] + 1); // 坎张型
                        }
                    }
                }
                // 去重
                if (waiting_tiles_list.Count > 0)
                {
                    foreach (int i in waiting_tiles_list)
                    {
                        waiting_tiles.Add(i);
                    }
                }
            }
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
                if (tile_id <= 40)
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
            bool debug = true)
        {
            var checker = new Qingque_Tingpai_Check(debug);
            return checker.TingpaiCheck(hand_tile_list, combination_list);
        }
    }
}
