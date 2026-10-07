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
    /// <summary>待计算玩家由牌谱状态事件恢复的公开标签（如已报敲）。</summary>
    public List<string> SelfTags;
    /// <summary>待计算玩家本人已知的暗扣弃牌；其他家的暗扣不公开。</summary>
    public List<int> SelfKnownConcealedDiscards;
    public List<int[]> SelfCombinationMasks;
    public bool SelfIsRiichi;
    public bool SelfIsDaburuRiichi;
    public bool RedDora = true;
    public string ReadyQualification;
    public List<int> DoraIndicators;
    public int SelfDingqueSuit;
    public Dictionary<string, object> DetailedConfig;
    public Dictionary<string, RecordTipsPlayerVisible> PlayersByPosition;
}

public class RecordTipsPlayerVisible {
    public List<int> DiscardTiles;
    public List<string> CombinationTiles;
    public List<int[]> CombinationMasks;
}
