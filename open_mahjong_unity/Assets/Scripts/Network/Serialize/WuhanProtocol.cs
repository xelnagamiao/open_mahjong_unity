using System;
using System.Collections.Generic;

[Serializable]
public sealed class WuhanInfo {
    public int? indicator, joker, last_drawn_tile, previous_gold;
    public int[] skins, fan_by_player;
    public bool self_has_draw_slot, sea, push_gold;
    public int[] push_liability;
    public WuhanDetail detail;
    public Dictionary<int, int> start_scores, round_score_changes;
    public Dictionary<int, WuhanRain> rain;
}
[Serializable]
public sealed class WuhanRain {
    public int suit;
    public int[] payers;
}
[Serializable]
public sealed class WuhanDetail {
    public int fan, total;
    public bool gold;
    public string[] fan_names;
    public Dictionary<int, int> payments, fan_by_player;
}
[Serializable]
public sealed class WuhanWaitDetail {
    public string kind, label;
    public int points;
}
[Serializable]
public sealed class WuhanHints {
    public bool win_blocked;
    public int[] source_hand_tiles, waiting_tiles;
    public string[] source_melds;
    public Dictionary<int, int[]> waiting_by_discard;
    public Dictionary<int, WuhanWaitDetail> waiting_details;
    public Dictionary<int, Dictionary<int, WuhanWaitDetail>> waiting_details_by_discard;
}
