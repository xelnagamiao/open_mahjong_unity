using System.Collections.Generic;

public sealed class SichuanSettlementPlayer {
    public int seat, original_seat, user_id, team_id, max_fan, win_tile, score, hu_order;
    public string username, status, table;
    public int[] hand, history;
    public int[][] melds;
}
public sealed class SichuanSettlementEvent {
    public string kind, detail, phase;
    public int order, duration_ms, win_tile, hu_order;
    public int[] hand;
    public int[][] melds;
    public int seat, points;
    public int? source;
    public int? fan;
    public bool per_payer;
    public string[] fans;
    public Dictionary<int,int> changes;
}
public sealed class SichuanBoardSettlementInfo {
    public int version, round, max_rounds, presentation_ms;
    public int? dealer_seat;
    public string reason;
    public List<SichuanSettlementPlayer> players;
    public List<SichuanSettlementEvent> events;
    public Dictionary<int,int> scores_before, scores_after, changes;
    public int[] team_changes, team_totals;
}
public sealed class SichuanMatchSettlementInfo {
    public bool duplicate;
    public string reason;
    public Dictionary<string,List<SichuanBoardSettlementInfo>> tables;
    public List<SichuanSettlementPlayer> players;
    public int[] scores;
    public float[] match_points;
    public DuplicateMatchInfo duplicate_info;
}
