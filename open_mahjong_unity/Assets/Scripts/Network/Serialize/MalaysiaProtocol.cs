using System;
using System.Collections.Generic;

[Serializable]
public sealed class MalaysiaInfo {
    public string rule_version, phase;
    public int player_count, hand_number, max_hands;
    public int[] dice, seat_to_original;
    public bool self_has_draw_slot;
    public Dictionary<int,int> start_scores, round_changes;
    public MalaysiaMeld[][] melds;
    public MalaysiaSettlement settlement;
}
[Serializable]
public sealed class MalaysiaMeld {
    public string kind;
    public int[] physical, logical;
    public int source;
    public bool concealed;
}
[Serializable]
public sealed class MalaysiaSettlement {
    public int? winner, payer, tile;
    public string source;
    public bool automatic;
    public int[] changes, sides, flies;
    public MalaysiaQuote quote;
    public MalaysiaPayment[] ledger;
}
[Serializable]
public sealed class MalaysiaQuote {
    public int fan, points;
    public string[] maximums;
    public bool automatic;
}
[Serializable]
public sealed class MalaysiaPayment {
    public int payer, receiver, points;
    public string reason;
}
[Serializable]
public sealed class MalaysiaHints {
    public Newtonsoft.Json.Linq.JObject local_request;
    public int[] source_hand_tiles, waiting_tiles;
    public string[] source_melds;
    public Dictionary<int,int[]> waiting_by_discard;
}
