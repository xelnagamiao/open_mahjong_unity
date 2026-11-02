using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;

[Serializable]
public sealed class DuocaiMeld {
    public int[] physical=Array.Empty<int>();
    public bool concealed;
    public string declared="peng";
    public int source=-1;
}
[Serializable]
public sealed class DuocaiInfo {
    public string rule_version,variant,phase;
    public int base_hand_tiles=13,hand_number,honba,riichi_sticks,deck,opening;
    public int[] start_scores,seat_to_original,dice,indicators,ura_indicators,reserves;
    public int[][] retired;
    public bool match_finishing,self_has_draw_slot;
    public DuocaiWallTile[] wall;
    public JObject ledger;
    public double? clock_remaining_ms,clock_step_remaining_ms;
    public bool? clock_active;
}
[Serializable]
public sealed class DuocaiWallTile {
    public int position,back,side,stack,layer;
    public bool removed,dead;
    public int? tile;
}
[Serializable]
public sealed class DuocaiChoice {
    public string id,action,kind;
    public int[] take;
    public int[][] faces;
    public int meld_index;
}
[Serializable]
public sealed class DuocaiHints {
    public JObject local_request;
    public int[] source_hand_tiles,waiting_tiles;
    public string[] source_melds;
    public bool win_blocked;
    public Dictionary<int,DuocaiWait> waiting_details;
    public Dictionary<int,int[]> waiting_by_discard;
    public Dictionary<int,Dictionary<int,DuocaiWait>> waiting_details_by_discard;
}
[Serializable]
public sealed class DuocaiWait {
    public int tile,points,ron_points,self_points,fan,remaining;
    public string kind,label;
    public bool can_ron,can_self,furiten;
}
