using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;

[Serializable]
public sealed class BeijingInfo {
    public string rule_version, phase;
    public int indicator, hun, base_hand_tiles=13, hand_number, repeat;
    public int[] dice, seat_to_original, start_scores;
    public bool[] tian_ting, discarded_hun;
    public bool match_finishing, self_has_draw_slot;
    public JObject ledger;
    public double? clock_remaining_ms, clock_step_remaining_ms;
    public bool? clock_active;
}

[Serializable]
public sealed class BeijingHints {
    public JObject local_request;
    public int[] source_hand_tiles, waiting_tiles;
    public string[] source_melds;
    public bool win_blocked;
}
