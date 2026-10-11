using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;

[Serializable]
public sealed class ShenyangInfo {
    public string phase;
    public bool final_four, long_hair, self_has_draw_slot;
    public int self_last_drawn_tile;
    public int passed_fan=-1;
    public int? egg_offer;
    public ShenyangPlayerSnapshot[] players;
    public JObject @event;
    public Dictionary<int,int> tail_tiles, round_score_changes;
    public ShenyangPayment[] payments;
    public ShenyangHints hints;
}
[Serializable]
public sealed class ShenyangPlayerSnapshot {
    public int player, hand_count, score, last_drawn_tile;
    public bool opened, has_draw_slot;
    public int[] hand;
    public string[] melds;
    public int[][] masks;
}
[Serializable]
public sealed class ShenyangPayment {
    public int payer, fan, raw_fan, score;
    public string[] fan_names;
}
[Serializable]
public sealed class ShenyangHints {
    public int[] source_hand_tiles;
    public string[] source_melds;
    public Dictionary<int,ShenyangWaitDetail> waiting;
    public Dictionary<int,Dictionary<int,ShenyangWaitDetail>> by_discard;
}
[Serializable]
public sealed class ShenyangWaitDetail {
    public string status, label;
    public int self_draw_points, ron_min, ron_max;
}
