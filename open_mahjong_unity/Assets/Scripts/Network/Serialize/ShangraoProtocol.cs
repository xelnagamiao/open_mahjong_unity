using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;

[Serializable]
public sealed class ShangraoInfo {
    public string rule_version, phase;
    public int indicator, bao, indicator_index, indicator_position, base_hand_tiles=13, hand_number;
    public int[] dice, seat_to_original, start_scores, burnt_tiles, flying_bao;
    public int[][] burnt_river_indices;
    public bool match_finishing, self_has_draw_slot;
    public JObject ledger;
    public double? clock_remaining_ms, clock_step_remaining_ms;
    public bool? clock_active;
}

[Serializable]
public sealed class ShangraoHints {
    public JObject local_request;
    public int[] source_hand_tiles, waiting_tiles;
    public string[] source_melds;
    public bool win_blocked;
    public Dictionary<int, ShangraoWait> waiting_details;
    public Dictionary<int, int[]> waiting_by_discard;
    public Dictionary<int, Dictionary<int, ShangraoWait>> waiting_details_by_discard;
}

[Serializable]
public sealed class ShangraoWait {
    public int tile, points;
    public string kind, label;
}
