using System;
using System.Collections.Generic;

[Serializable]
public sealed class NanjingInfo {
    public string rule_version, phase;
    public int base_hand_tiles, hand_number;
    public int[] dice, seat_to_original, start_scores, round_changes;
    public int[][] contracts;
    public bool match_finishing, self_has_draw_slot;
    public Dictionary<int,int[]> final_hands;
    public NanjingLedger ledger;
    public NanjingScoreEvent[] events;
}
[Serializable]
public sealed class NanjingLedger {
    public int[] winners, changes;
    public string source;
}
[Serializable]
public sealed class NanjingScoreEvent {
    public string kind;
    public int actor;
    public int[] changes;
}
[Serializable]
public sealed class NanjingWaitDetail {
    public string kind;
    public int points, pattern_points, flower_count;
}
[Serializable]
public sealed class NanjingHints {
    public int[] source_hand_tiles, waiting_tiles;
    public string[] source_melds;
    public Dictionary<int,int[]> waiting_by_discard;
    public Dictionary<int,NanjingWaitDetail> waiting_details;
    public Dictionary<int,Dictionary<int,NanjingWaitDetail>> waiting_details_by_discard;
}
