using System.Collections.Generic;

/// <summary>
/// 立直麻将结算所需的附加信息，由服务器 show_result 广播中的字段填充，
/// 交由 EndResultPanel 在日麻分支下渲染。
/// </summary>
public class RiichiEndResultExtras {
    public int Han;
    public int Fu;
    public int AkaCount;
    public int DoraCount;
    public int UraDoraCount;
    public List<int> DoraIndicators = new List<int>();
    public List<int> UraDoraIndicators = new List<int>();
    public int Honba;
    public int RiichiSticksCollected;
    public Dictionary<int, int> ScoreChanges;
    /// <summary>荒牌流局时各家听牌张，键为 player_index，值为听张 ID 列表；未听家不出现。</summary>
    public Dictionary<int, int[]> TenpaiTiles;
    /// <summary>荒牌流局时听牌家的实际手牌，用于四家统一倒牌展示。</summary>
    public Dictionary<int, int[]> TenpaiHands;
    /// <summary>荒牌流局是否发生不听罚符点棒。</summary>
    public bool NotenPenaltyAfterDraw;
}
