using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;

[Serializable]
public sealed class WenzhouInfo {
    public string rule_version, phase;
    public int caishen, indicator, indicator_index, white_natural, dealer_streak;
    public int base_hand_tiles = 16;
    public int hand_number;
    public bool match_finishing, self_has_draw_slot;
    public int[] dice, seat_to_original, start_scores, next_dealer_dice;
    public int next_dealer_shift, next_dealer_streak;
    public int[] caishen_counts, caishen_changes, round_changes;
    public JArray ledger;
    public JObject score_details;
    public Dictionary<string, WenzhouWait[]> waits;
    public int? remain_tiles, dead_wall_count;
    public double? clock_remaining_ms, clock_step_remaining_ms;
    public bool? clock_active;
}

[Serializable]
public sealed class WenzhouWait {
    public int tile, ron_multiplier, self_draw_multiplier;
    public bool ron, self_draw;
}

[Serializable]
public sealed class WenzhouMeldRecord {
    public string code;
    public int[] physical, logical;
}
