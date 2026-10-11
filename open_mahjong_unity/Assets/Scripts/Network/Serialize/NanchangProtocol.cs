using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;

[Serializable]
public sealed class NanchangInfo {
    public string rule_version, phase;
    public int indicator, secondary, indicator_index, base_hand_tiles=13, hand_number;
    public int[] dice, jing_dice, seat_to_original, start_scores;
    public bool[] declared_jingdiao;
    public bool match_finishing, self_has_draw_slot;
    public JObject ledger;
    public double? clock_remaining_ms, clock_step_remaining_ms;
    public bool? clock_active;
}

[Serializable]
public sealed class NanchangWait {
    public int tile, points, points_min, points_max, self_draw_points;
    public string status;
    public bool self_draw;
    public string[] melds;
    public Dictionary<int,string> ron_status;
    public Dictionary<int,int> ron_points;
}
