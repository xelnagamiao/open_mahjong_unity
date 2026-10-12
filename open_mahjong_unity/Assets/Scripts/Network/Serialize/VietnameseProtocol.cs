using System;
[Serializable]
public sealed class VietnameseInfo {
    public string rule_version,phase;
    public int base_hand_tiles,hand_number;
    public bool match_finishing,self_has_draw_slot,opening_complete,nfnl_eligible;
    public int[] dice,round_changes,replacement_candidates,bounce_candidates;
    public int[][] discarded_bonus;
    public VietnameseClaimOption[] claim_options;
}
[Serializable]
public sealed class VietnameseClaimOption {
    public int id,face,claimed_face,joker_count,versatility;
    public string kind;
    public int[] tiles;
}
