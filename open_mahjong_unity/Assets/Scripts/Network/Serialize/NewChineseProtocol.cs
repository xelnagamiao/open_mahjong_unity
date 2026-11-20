using System;
using System.Collections.Generic;

[Serializable]
public sealed class NewChineseInfo {
    public string phase;
    public int hand_number, repeat, bao, revision, supplements, pass_level;
    public int? payer;
    public int cut_tile;
    public int? acting;
    public bool self_has_draw_slot, after_kong, win_blocked;
    public bool rob_from_discard;
    public bool[] declared, after_kongs, blocked;
    public int[] chains, best_chains, start_scores, levels, passed_ron;
    public int[][] passed;
    public Dictionary<int,int> visible;
}
