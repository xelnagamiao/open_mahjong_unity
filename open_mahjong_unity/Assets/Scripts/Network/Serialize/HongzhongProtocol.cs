using System;
using System.Collections.Generic;

[Serializable]
public sealed class HongzhongInfo {
    public bool self_has_draw_slot;
    public int? last_drawn_tile;
    public int[] bird_tiles;
    public int bird_hits;
    public HongzhongDetail detail;
    public Dictionary<int,int> start_scores, round_score_changes;
    public HongzhongKong[] kong_ledger;
}
[Serializable]
public sealed class HongzhongDetail {
    public string rule_version, shape;
    public int fan, raw_fan, base_score, winning_logical_tile;
    public int[] logical_hand;
    public int[][] joker_substitutions;
    public string[] fan_names;
}
[Serializable]
public sealed class HongzhongKong {
    public int player, tile;
    public string kind;
    public int? payer;
    public bool drawn;
    public Dictionary<int,int> changes;
}
[Serializable]
public sealed class HongzhongWaitDetail {
    public int fan, base_score;
}
[Serializable]
public sealed class HongzhongHints {
    public int hint_version;
    public bool win_blocked;
    public int[] source_hand_tiles, waiting_tiles;
    public string[] source_melds;
    public Dictionary<int,int[]> waiting_by_discard;
    public Dictionary<int,HongzhongWaitDetail> waiting_details;
    public Dictionary<int,Dictionary<int,HongzhongWaitDetail>> waiting_details_by_discard;
}
[Serializable]
public sealed class HongzhongBirdEvent {
    public int[] tiles;
    public int hits;
    public HongzhongDetail detail;
}
