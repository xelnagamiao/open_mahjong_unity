using System.Collections.Generic;

/// <summary>Public doubles snapshot; scores are from completed boards only.</summary>
public class SichuanTeamsInfo {
    public int[] team_by_player_index;
    public Dictionary<int, int> settled_scores;
    public List<Dictionary<string, object>> round_history;
}
