using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;

[Serializable]
public class HangzhouHints {
    public int[] source_hand_tiles, waiting_tiles;
    public string[] source_melds;
    public Dictionary<int, int[]> waiting_by_discard;
}
[Serializable]
public sealed class HangzhouInfo : HangzhouHints {
    public string rule_version, phase;
    public int base_hand_tiles, hand_number, dealer_streak, dealer_multiplier, wall_count, burned_count;
    public int[] dice, joker_tiles, cai_discard_locks, cai_piao_counts, chi_counts, ten_winds_counts, seat_to_original;
    public bool[] forced_draw_discard;
    public bool match_finishing, self_has_draw_slot;
    public JObject ledger;
}
