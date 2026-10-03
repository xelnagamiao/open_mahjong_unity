using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

[Serializable]
public sealed class ChangchunInfo {
    public int version, bao_revision, bao_tile;
    public string window, phase;
    public bool bao_visible, bao_exhausted;
    public bool self_has_draw_slot;
    public int self_last_drawn_tile;
    public int? bao_owner, tail_remaining;
    public ChangchunCandidate[] special_candidates, added_candidates;
    public ChangchunPlayerSnapshot[] players;
    [JsonProperty("event")] public JObject Event;
    public JObject[] tail_tiles, payments;
    public string bao;
    public int? represented_win;
    public Dictionary<int,int> round_score_changes;
}
[Serializable]
public sealed class ChangchunCandidate {
    public int token, tile, represented, position;
    public string code, label;
    public int[] physical, logical;
}
[Serializable]
public sealed class ChangchunPlayerSnapshot {
    public int player, hand_count, score, last_drawn_tile;
    public bool has_draw_slot;
    public int[] hand;
    public string[] melds;
    public int[][] masks;
}
