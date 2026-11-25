using System;
using System.Collections.Generic;

[Serializable]
public sealed class NewChineseInfo {
    public string phase;
    public bool[] exchange_choices;
    public int exchange_remaining_ms, exchange_duration_ms;
    public int[] exchange_outgoing, exchange_incoming;
    public int[][] exchange_packets;
    public bool bao_initial;
    public int? bao_remaining_ms;
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

[System.Serializable]
public sealed class SanhuaInfo {
    public int indicator, skin, joker;
    public int? wall_front_draws, wall_back_draws;
    public int[] dice, liable;
    public double[] table_fans;
    public bool[] opened, declared, after_kong;
    public bool self_has_draw_slot;
    public int? payer;
    public string phase;
}

