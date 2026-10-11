using System;
using System.Collections.Generic;

[Serializable]
public sealed class JiujiangInfo { public bool self_has_draw_slot; }
[Serializable]
public sealed class JiujiangWaitDetail { public int points; public string kind; }
[Serializable]
public sealed class JiujiangHints {
    public int[] source_hand_tiles, waiting_tiles;
    public string[] source_melds;
    public Dictionary<int,int[]> waiting_by_discard;
    public Dictionary<int,JiujiangWaitDetail> waiting_details;
    public Dictionary<int,Dictionary<int,JiujiangWaitDetail>> waiting_details_by_discard;
}
