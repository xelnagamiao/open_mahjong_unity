using System;
using System.Collections.Generic;

/// <summary>权威服务端的广东 MIL 花鬼状态；真实手牌始终保留 55–58。</summary>
[Serializable]
public sealed class GuangdongPublicState {
    public string edition;
    public int[] ghost_discard_counts;
    public List<GuangdongKongEntry> kong_ledger;
}
[Serializable]
public sealed class GuangdongKongEntry {
    public int player;
    public string kind;
    public int? tile, payer;
    public int[] changes;
}
[Serializable]
public sealed class GuangdongWait {
    public int tile, fan, score;
    public int? ron_fan, self_draw_fan, ron_score, self_draw_score;
    public bool ron, self_draw;
}
[Serializable]
public sealed class GuangdongTips {
    public int[] hand;
    public string[] melds;
    public GuangdongWait[] waits;
    public Dictionary<int, GuangdongWait[]> discard_waits;
}
[Serializable]
public sealed class GuangdongSubstitution {
    public int physical, logical;
}
[Serializable]
public sealed class GuangdongHorses {
    public int winner;
    public int[] tiles, hits, changes;
    public int? payer;
}
[Serializable]
public sealed class GuangdongResult {
    public string edition;
    public bool draw;
    public int fan, base_score, coefficient, discarded_ghosts;
    public GuangdongSubstitution[] substitutions;
    public GuangdongHorses horses;
    public int[] win_changes, kong_changes, refund_changes;
}
