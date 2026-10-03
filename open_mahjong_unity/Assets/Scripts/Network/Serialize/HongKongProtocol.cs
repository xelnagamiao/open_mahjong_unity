using System.Collections.Generic;

/// <summary>规则元数据与固定玩家身份的拉踢账本；不参与客户端结算裁定。</summary>
public sealed class HongKongInfo {
    public string rule_version;
    public string phase;
    public int base_hand_tiles;
    public bool opening_complete;
    public int dealer_streak;
    public int starting_score;
    public int[] negative_score_grace;
    public bool match_finishing;
    public HongKongPull[] pulls;
}

public sealed class HongKongPull {
    public int creditor;
    public int debtor;
    public int points;
    public int mouths;
}

public sealed class HongKongWait {
    public int tile;
    public bool ron;
    public bool self_draw;
    public int ron_fan;
    public int self_draw_fan;
}
