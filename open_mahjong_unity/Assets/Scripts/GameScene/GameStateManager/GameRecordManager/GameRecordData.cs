using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 单个动作的可序列化包装类（用于 Inspector 显示）
/// </summary>
[Serializable]
public class ActionTickDisplay {
    public string actionString;

    public ActionTickDisplay(List<string> actionList) {
        actionString = string.Join(", ", actionList);
    }
}

/// <summary>
/// 单局数据
/// </summary>
[Serializable]
public class Round {
    public int roundIndex;
    public long roundRandomSeed;
    public int currentRound;
    public int p0UserId;
    public int p1UserId;
    public int p2UserId;
    public int p3UserId;
    public List<int> p0Tiles;
    public List<int> p1Tiles;
    public List<int> p2Tiles;
    public List<int> p3Tiles;
    public List<int> tilesList;
    public List<List<string>> actionTicks;

    [SerializeField] private List<ActionTickDisplay> _actionTicksDisplay;

    public void UpdateActionTicksDisplay() {
        _actionTicksDisplay = new List<ActionTickDisplay>();
        _actionTicksDisplay.Clear();
        foreach (var action in actionTicks) {
            _actionTicksDisplay.Add(new ActionTickDisplay(action));
        }
    }

    public Round() {
        roundIndex = 0;
        roundRandomSeed = 0;
        currentRound = 0;
        p0Tiles = new List<int>();
        p1Tiles = new List<int>();
        p2Tiles = new List<int>();
        p3Tiles = new List<int>();
        tilesList = new List<int>();
        actionTicks = new List<List<string>>();
    }
}

/// <summary>
/// 游戏轮次数据（包含所有局）
/// </summary>
[Serializable]
public class GameRound {
    public Dictionary<int, Round> rounds;

    public GameRound() {
        rounds = new Dictionary<int, Round>();
    }

    /// <summary>
    /// 获取所有局的列表（按 roundIndex 排序）
    /// </summary>
    public List<Round> GetRoundsList() {
        var result = new List<Round>(rounds.Values);
        result.Sort((a, b) => a.roundIndex.CompareTo(b.roundIndex));
        return result;
    }
}

/// <summary>
/// 完整牌谱数据
/// </summary>
[Serializable]
public class GameRecord {
    public Dictionary<string, object> gameTitle;
    public GameRound gameRound;

    public GameRecord() {
        gameRound = new GameRound();
    }
}

