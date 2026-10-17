using System;
using System.Collections.Generic;

[Serializable]
public sealed class FuzhouDeclaration {
    public int youjin, youjin_tile;
    public bool golden_middle, discarded_gold;
}
[Serializable]
public sealed class FuzhouInfo {
    public string rule_version, phase;
    public int base_hand_tiles, hand_number, dealer, dealer_streak, dealer_changes, gold_tile, reserved_tiles, sea_remaining;
    public int? youjin_owner, double_youjin_owner;
    public bool indicator_claimed, last_four, match_finishing, self_has_draw_slot;
    public int[] dice, seat_to_original, start_scores, round_changes;
    public FuzhouDeclaration[] declarations;
    public Dictionary<int,int[]> final_hands;
    public Newtonsoft.Json.Linq.JObject ledger;
}
[Serializable]
public sealed class FuzhouWaitDetail {
    public string kind;
    public int points;
}
[Serializable]
public sealed class FuzhouHints {
    public Newtonsoft.Json.Linq.JObject local_request;
    public int[] source_hand_tiles, waiting_tiles;
    public string[] source_melds;
    public Dictionary<int,int[]> waiting_by_discard;
    public Dictionary<int,FuzhouWaitDetail> waiting_details;
    public Dictionary<int,Dictionary<int,FuzhouWaitDetail>> waiting_details_by_discard;
}
