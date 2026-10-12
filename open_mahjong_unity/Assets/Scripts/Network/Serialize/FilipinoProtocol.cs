using System;
[Serializable]
public sealed class FilipinoInfo {
    public string rule_version, phase;
    public int base_hand_tiles, hand_number, hand_multiplier;
    public bool match_finishing, self_has_draw_slot, opening_complete;
    public int[] dice, round_changes;
}
