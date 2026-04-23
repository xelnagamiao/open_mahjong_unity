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
}
