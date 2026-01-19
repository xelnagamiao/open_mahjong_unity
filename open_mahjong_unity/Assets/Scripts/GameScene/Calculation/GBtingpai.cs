using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

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
public class Chinese_Tingpai_Check
{
    private static readonly HashSet<int> yaojiu = new HashSet<int> { 11, 19, 21, 29, 31, 39, 41, 42, 43, 44, 45, 46, 47 };
    private static readonly HashSet<int> zipai = new HashSet<int> { 41, 42, 43, 44, 45, 46, 47 };

    private HashSet<int> waiting_tiles;
    private HashSet<int> temp_waiting_tiles;
    private bool debug;

    public Chinese_Tingpai_Check(bool debug = false)
    {
        waiting_tiles = new HashSet<int>();
        temp_waiting_tiles = new HashSet<int>();
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
        temp_waiting_tiles.Clear();

        // 13张牌检查特殊牌型
        if (player_tiles.hand_tiles.Count == 13)
        {
            GS_check(player_tiles.hand_tiles);  // 国士无双检查
            QD_check(player_tiles.hand_tiles);  // 七对子检查
        }

        if (QBK_check(player_tiles))  // 全不靠检查
        {
            return waiting_tiles;
        }

        normal_check(player_tiles); // 一般型检查

        DebugPrint("等待牌: {0}", string.Join(", ", waiting_tiles));

        return waiting_tiles;
    }

    private void GS_check(List<int> hand_tiles)
    {
        // 检查国士
        HashSet<int> GS_step_set = new HashSet<int>();
        bool GS_allowed = true;
        // 如果牌存在于幺九集合,则加入手牌幺九集合
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
        // 如果牌都是幺九 并且满足12种或者13种 前者添加第十三种 后者添加全部十三种
        if (GS_allowed)
        {
            if (GS_step_set.Count == 12)
            {
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

    private bool QBK_check(PlayerTilesTingpai player_tiles)
    {
        int hand_kind_set = new HashSet<int>(player_tiles.hand_tiles).Count;
        // 如果手牌种类大于13种,则可能全不靠听牌,如果手牌种类大于10种,则可能组合龙
        if (hand_kind_set >= 13)
        {
            List<HashSet<int>> QBK_case_list = new List<HashSet<int>> {
                new HashSet<int> {11,14,17,22,25,28,33,36,39,41,42,43,44,45,46,47},
                new HashSet<int> {11,14,17,32,35,38,23,26,29,41,42,43,44,45,46,47},
                new HashSet<int> {21,24,27,12,15,18,33,36,39,41,42,43,44,45,46,47},
                new HashSet<int> {21,24,27,32,35,38,13,16,19,41,42,43,44,45,46,47},
                new HashSet<int> {31,34,37,22,25,28,13,16,19,41,42,43,44,45,46,47},
                new HashSet<int> {31,34,37,12,15,18,23,26,29,41,42,43,44,45,46,47}
            };
            // 遍历手牌如果在对应的全不靠组合当中则加入全不靠集合
            foreach (var case_set in QBK_case_list)
            {
                HashSet<int> QBK_set = new HashSet<int>();
                foreach (int i in player_tiles.hand_tiles)
                {
                    if (case_set.Contains(i))
                    {
                        QBK_set.Add(i);
                    }
                }
                // 如果全不靠集合满足,就将手牌中不在全不靠组合中的牌加入等待牌,并返回True
                if (QBK_set.Count == 13)
                {
                    List<int> need_tile_list = new List<int>();
                    foreach (int i in case_set)
                    {
                        if (!player_tiles.hand_tiles.Contains(i))
                        {
                            need_tile_list.Add(i);
                        }
                    }
                    foreach (int i in need_tile_list)
                    {
                        waiting_tiles.Add(i);
                    }
                    return true;
                }
            }
        }
        else if (hand_kind_set >= 8)
        {
            List<HashSet<int>> ZHL_case_list = new List<HashSet<int>> {
                new HashSet<int> {11,14,17,22,25,28,33,36,39},
                new HashSet<int> {11,14,17,32,35,38,23,26,29},
                new HashSet<int> {21,24,27,12,15,18,33,36,39},
                new HashSet<int> {21,24,27,32,35,38,13,16,19},
                new HashSet<int> {31,34,37,22,25,28,13,16,19},
                new HashSet<int> {31,34,37,12,15,18,23,26,29}
            };
            foreach (var case_set in ZHL_case_list)
            {
                HashSet<int> ZHL_set = new HashSet<int>();
                foreach (int i in player_tiles.hand_tiles)
                {
                    if (case_set.Contains(i))
                    {
                        ZHL_set.Add(i);
                    }
                }
                // 如果组合龙集合 = 9或者8 则在一向听的前提下 如果的确听牌 和牌必然包含组合龙 直接移除后进入一般型检测
                // 完整情况的组合龙正常删除就行 当做一个正常的顺子和刻子来处理
                if (ZHL_set.Count == 9)
                {
                    player_tiles.complete_step += 9;
                    player_tiles.combination_list.Add($"z{string.Join(",", case_set)}");
                    foreach (int i in case_set)
                    {
                        player_tiles.hand_tiles.Remove(i);
                    }
                    return false;
                }
                // 不完整情况的组合龙将缺张牌加入temp_waiting_tiles 如果在一般型检测中和牌步数达到14 代表缺的那张就是组合龙的缺张，将缺张牌加入waiting_tiles
                else if (ZHL_set.Count == 8)
                {
                    player_tiles.complete_step += 9;
                    player_tiles.combination_list.Add($"z{string.Join(",", case_set)}");
                    foreach (int i in case_set)
                    {
                        if (player_tiles.hand_tiles.Contains(i))
                        {
                            player_tiles.hand_tiles.Remove(i);
                        }
                        else
                        {
                            temp_waiting_tiles.Add(i);
                        }
                    }
                    return false;
                }
            }
        }
        return false;
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

        // 剩余的手牌有五种组成方式 
        // 1.单吊听牌型(无雀头型)[n] 2.有雀头剩余对子型(对碰)[n,n] 3.剩余两面型[n,n+1] 4.剩余坎张型[n,n+2] 5.无效型[n,m] 特殊情况:组合龙型 complete_step == 14 [temp_waiting_tiles]
        if (end_list.Count > 0)
        {
            List<int> waiting_tiles_list = new List<int>();
            foreach (var i in end_list)
            {
                DebugPrint("手牌: {0}, 步数: {1}, 组合: {2}",
                    string.Join(", ", i.hand_tiles),
                    i.complete_step,
                    string.Join(", ", i.combination_list));
                // 如果听牌步数是14 则代表缺的那张牌就是组合龙的缺张 直接返回缺张牌
                if (i.complete_step == 14)
                {
                    DebugPrint("组合龙型");
                    waiting_tiles = new HashSet<int>(temp_waiting_tiles);
                    return;
                }

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

// 国标麻将听牌检查入口类
public static class GBtingpai
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
        var checker = new Chinese_Tingpai_Check(debug);
        return checker.TingpaiCheck(hand_tile_list, combination_list);
    }
}

