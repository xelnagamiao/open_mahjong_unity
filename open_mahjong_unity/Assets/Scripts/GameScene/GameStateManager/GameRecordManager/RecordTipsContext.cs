using System.Collections.Generic;

/// <summary>
/// 牌谱/观战听牌提示计算所需的牌桌快照（不依赖 NormalGameStateManager）。
/// </summary>
public class RecordTipsContext {
    public string RoomRule;
    public string SubRule;
    public int HepaiLimit;
    public int CurrentRound;
    public int SelfPlayerIndex;
    public int RemainTiles;
    public List<int> SelfHuapaiList;
    public List<int[]> SelfCombinationMasks;
    public bool SelfIsRiichi;
    public List<int> DoraIndicators;
    public int SelfDingqueSuit;
    public Dictionary<string, RecordTipsPlayerVisible> PlayersByPosition;
}

public class RecordTipsPlayerVisible {
    public List<int> DiscardTiles;
    public List<string> CombinationTiles;
}
