using System;
using System.Collections.Generic;

[Serializable]
public sealed class SingaporeInfo {
    public bool self_has_draw_slot, settled;
    public int dead_wall, round_index;
    public string rule_version;
    public Dictionary<int,int> scores, liability;
    public List<SingaporePayment> payments;
}
[Serializable]
public sealed class SingaporePayment {
    public int winner, amount;
    public string reason;
    public Dictionary<int,int> changes;
}
[Serializable]
public sealed class SingaporeWaitDetail { public int points; public string kind; }
[Serializable]
public sealed class SingaporeHints {
    public int[] source_hand_tiles, waiting_tiles;
    public string[] source_melds;
    public Dictionary<int,int[]> waiting_by_discard;
    public Dictionary<int,SingaporeWaitDetail> waiting_details;
    public Dictionary<int,Dictionary<int,SingaporeWaitDetail>> waiting_details_by_discard;
}
