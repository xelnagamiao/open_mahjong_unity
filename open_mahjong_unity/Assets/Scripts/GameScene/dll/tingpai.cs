using System;
using System.Collections.Generic;
using System.Linq;

public class ChineseTingpaiCheck
{
    // 麻将牌定义：万11-19, 饼21-29, 条31-39, 字牌41-47
    private static readonly HashSet<int> YaoJiu = new HashSet<int>
    {
        11, 19, 21, 29, 31, 39, 41, 42, 43, 44, 45, 46, 47
    };

    /// <summary>
    /// 主接口：检查听牌牌型
    /// </summary>
    /// <param name="handTileList">手牌列表，如 {11,11,12,12,13,15,...}</param>
    /// <param name="combinationList">已组成的顺子/刻子（可为空）</param>
    /// <returns>听牌集合（去重）</returns>
    public static HashSet<int> TingpaiCheck(List<int> handTileList, List<string> combinationList = null)
    {
        var waitingTiles = new HashSet<int>();

        if (combinationList == null)
            combinationList = new List<string>();

        int completeStep = combinationList.Count * 3;
        var handTiles = new List<int>(handTileList);
        handTiles.Sort();

        // 13张牌检查特殊牌型
        if (handTiles.Count == 13)
        {
            // 国士无双
            GS_Check(handTiles, waitingTiles);
            // 七对子
            QD_Check(handTiles, waitingTiles);
            if (waitingTiles.Count > 0)
                return RemoveInvalidTiles(waitingTiles);
        }

        // 全不靠 & 组合龙 检查
        if (QBK_Check(handTiles, ref completeStep, combinationList, waitingTiles))
            return RemoveInvalidTiles(waitingTiles);

        // 一般型听牌检查
        NormalCheck(handTiles, completeStep, combinationList, waitingTiles);

        return RemoveInvalidTiles(waitingTiles);
    }

    private static void GS_Check(List<int> handTiles, HashSet<int> waitingTiles)
    {
        var gsSet = new HashSet<int>();
        bool allYaojiu = true;

        foreach (int tile in handTiles)
        {
            if (YaoJiu.Contains(tile))
                gsSet.Add(tile);
            else
                allYaojiu = false;
        }

        if (!allYaojiu) return;

        if (gsSet.Count == 12)
        {
            foreach (int tile in YaoJiu)
                if (!handTiles.Contains(tile))
                    waitingTiles.Add(tile);
        }
        else if (gsSet.Count == 13)
        {
            foreach (int tile in YaoJiu)
                waitingTiles.Add(tile);
        }
    }

    private static void QD_Check(List<int> handTiles, HashSet<int> waitingTiles)
    {
        var countMap = new Dictionary<int, int>();
        foreach (int tile in handTiles)
        {
            if (countMap.ContainsKey(tile))
                countMap[tile]++;
            else
                countMap[tile] = 1;
        }

        int singleCount = 0;
        int waitingTile = -1;

        foreach (var kvp in countMap)
        {
            int count = kvp.Value;
            if (count == 1 || count == 3)
            {
                singleCount++;
                waitingTile = kvp.Key;
            }
        }

        if (singleCount == 1 && waitingTile != -1)
            waitingTiles.Add(waitingTile);
    }

    private static bool QBK_Check(List<int> handTiles, ref int completeStep, List<string> combinationList, HashSet<int> waitingTiles)
    {
        var handKindSet = new HashSet<int>(handTiles).Count;

        if (handKindSet >= 13)
        {
            var qbkCases = new[]
            {
                new HashSet<int> {11,14,17,22,25,28,33,36,39,41,42,43,44,45,46,47},
                new HashSet<int> {11,14,17,32,35,38,23,26,29,41,42,43,44,45,46,47},
                new HashSet<int> {21,24,27,12,15,18,33,36,39,41,42,43,44,45,46,47},
                new HashSet<int> {21,24,27,32,35,38,13,16,19,41,42,43,44,45,46,47},
                new HashSet<int> {31,34,37,22,25,28,13,16,19,41,42,43,44,45,46,47},
                new HashSet<int> {31,34,37,12,15,18,23,26,29,41,42,43,44,45,46,47}
            };

            foreach (var caseSet in qbkCases)
            {
                var qbkSet = new HashSet<int>();
                foreach (int tile in handTiles)
                    if (caseSet.Contains(tile))
                        qbkSet.Add(tile);

                if (qbkSet.Count == 13)
                {
                    foreach (int tile in caseSet)
                        if (!handTiles.Contains(tile))
                            waitingTiles.Add(tile);
                    return true;
                }
            }
        }
        else if (handKindSet >= 8)
        {
            var zhlCases = new[]
            {
                new HashSet<int> {11,14,17,22,25,28,33,36,39},
                new HashSet<int> {11,14,17,32,35,38,23,26,29},
                new HashSet<int> {21,24,27,12,15,18,33,36,39},
                new HashSet<int> {21,24,27,32,35,38,13,16,19},
                new HashSet<int> {31,34,37,22,25,28,13,16,19},
                new HashSet<int> {31,34,37,12,15,18,23,26,29}
            };

            foreach (var caseSet in zhlCases)
            {
                var zhlSet = new HashSet<int>();
                foreach (int tile in handTiles)
                    if (caseSet.Contains(tile))
                        zhlSet.Add(tile);

                if (zhlSet.Count == 9)
                {
                    completeStep += 9;
                    combinationList.Add($"z{string.Join(",", caseSet)}");
                    foreach (int tile in caseSet)
                        handTiles.RemoveAll(t => t == tile);
                    return false;
                }
                else if (zhlSet.Count == 8)
                {
                    completeStep += 9;
                    combinationList.Add($"z{string.Join(",", caseSet)}");
                    foreach (int tile in caseSet)
                    {
                        if (handTiles.Contains(tile))
                            handTiles.RemoveAll(t => t == tile);
                        else
                            waitingTiles.Add(tile);
                    }
                    return false;
                }
            }
        }

        return false;
    }

    private static void NormalCheck(List<int> handTiles, int completeStep, List<string> combinationList, HashSet<int> waitingTiles)
    {
        if (!NormalCheckBlock(handTiles))
            return;

        var allStates = NormalCheckTraverseQuetou(handTiles, completeStep, combinationList);
        var endStates = new List<State>();

        while (allStates.Count > 0)
        {
            var state = allStates[allStates.Count - 1];
            allStates.RemoveAt(allStates.Count - 1);

            NormalCheckTraverseKezi(state, allStates);
            NormalCheckTraverseDazi(state, allStates);

            if (state.CompleteStep >= 11)
                endStates.Add(state);
        }

        // 过滤有效状态
        endStates = endStates.Where(s => s.CompleteStep >= 11).ToList();

        var tempWaiting = new HashSet<int>(); // 用于组合龙

        foreach (var state in endStates)
        {
            if (state.CompleteStep == 14)
            {
                waitingTiles.UnionWith(tempWaiting);
                return;
            }

            var tiles = state.HandTiles;
            if (tiles.Count == 1)
            {
                waitingTiles.Add(tiles[0]);
            }
            else if (tiles.Count == 2)
            {
                if (tiles[0] == tiles[1])
                {
                    waitingTiles.Add(tiles[0]);
                }
                else if (tiles[0] == tiles[1] - 1)
                {
                    if (tiles[0] % 10 != 1) waitingTiles.Add(tiles[0] - 1);
                    if (tiles[1] % 10 != 9) waitingTiles.Add(tiles[1] + 1);
                }
                else if (tiles[0] == tiles[1] - 2)
                {
                    waitingTiles.Add(tiles[0] + 1);
                }
            }
        }
    }

    private static bool NormalCheckBlock(List<int> handTiles)
    {
        int blockCount = 0;
        int lastTile = -10;

        foreach (int tile in handTiles)
        {
            if (tile == lastTile || tile == lastTile + 1)
            {
                // 连续或相同，属于同一块
            }
            else
            {
                blockCount++;
            }
            lastTile = tile;
        }

        return blockCount <= 6;
    }

    private static List<State> NormalCheckTraverseQuetou(List<int> handTiles, int completeStep, List<string> combinationList)
    {
        var allStates = new List<State>();
        int lastQuetou = -1;

        for (int i = 0; i < handTiles.Count; i++)
        {
            int tile = handTiles[i];
            if (i < handTiles.Count - 1 && handTiles[i + 1] == tile && tile != lastQuetou)
            {
                var newState = new State
                {
                    HandTiles = new List<int>(handTiles),
                    CompleteStep = completeStep,
                    CombinationList = new List<string>(combinationList)
                };
                newState.HandTiles.Remove(tile);
                newState.HandTiles.Remove(tile);
                newState.CompleteStep += 2;
                newState.CombinationList.Add($"q{tile}");
                allStates.Add(newState);
                lastQuetou = tile;
            }
        }

        // 无雀头情况
        allStates.Add(new State
        {
            HandTiles = new List<int>(handTiles),
            CompleteStep = completeStep,
            CombinationList = new List<string>(combinationList)
        });

        return allStates;
    }

    private static void NormalCheckTraverseKezi(State state, List<State> allStates)
    {
        int lastKezi = -1;
        for (int i = 0; i < state.HandTiles.Count; i++)
        {
            int tile = state.HandTiles[i];
            if (state.HandTiles.FindAll(t => t == tile).Count >= 3 && tile != lastKezi)
            {
                var newState = new State
                {
                    HandTiles = new List<int>(state.HandTiles),
                    CompleteStep = state.CompleteStep,
                    CombinationList = new List<string>(state.CombinationList)
                };
                newState.HandTiles.RemoveAll(t => t == tile);
                newState.CompleteStep += 3;
                newState.CombinationList.Add($"k{tile}");
                allStates.Add(newState);
                lastKezi = tile;
            }
        }
    }

    private static void NormalCheckTraverseDazi(State state, List<State> allStates)
    {
        int lastDazi = -1;
        for (int i = 0; i < state.HandTiles.Count; i++)
        {
            int tile = state.HandTiles[i];
            if (tile >= 41) continue; // 字牌不能组成顺子
            if (tile % 10 >= 8) continue; // 8,9,字牌不能做顺子中间

            if (state.HandTiles.Contains(tile + 1) && state.HandTiles.Contains(tile + 2) && tile != lastDazi)
            {
                var newState = new State
                {
                    HandTiles = new List<int>(state.HandTiles),
                    CompleteStep = state.CompleteStep,
                    CombinationList = new List<string>(state.CombinationList)
                };
                newState.HandTiles.Remove(tile);
                newState.HandTiles.Remove(tile + 1);
                newState.HandTiles.Remove(tile + 2);
                newState.CompleteStep += 3;
                newState.CombinationList.Add($"s{tile + 1}");
                allStates.Add(newState);
                lastDazi = tile;
            }
        }
    }

    private static HashSet<int> RemoveInvalidTiles(HashSet<int> tiles)
    {
        var invalid = new HashSet<int> { 10, 20, 30, 40 };
        return new HashSet<int>(tiles.Where(t => !invalid.Contains(t)));
    }

    // 状态类，模拟 Python 中的 PlayerTiles
    private class State
    {
        public List<int> HandTiles { get; set; }
        public int CompleteStep { get; set; }
        public List<string> CombinationList { get; set; }
    }


// 测试方法 - 可以在 Unity 中调用
public static void TestTingpai()
{
    // 示例：七对子听牌
    var hand = new List<int> { 11, 11, 12, 12, 13, 13, 14, 14, 15, 15, 16, 16, 17 };
    var result = ChineseTingpaiCheck.TingpaiCheck(hand);

    UnityEngine.Debug.Log("听牌: " + string.Join(",", result)); 
    // 输出：听牌: 17
}


}