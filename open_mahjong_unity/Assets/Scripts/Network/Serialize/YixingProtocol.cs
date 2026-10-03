using System;

[Serializable]
public sealed class YixingInfo {
    public string rule_version, phase;
    public int base_hand_tiles, hand_number, dealer_streak;
    public int[] dice, last_draw_passes, seat_to_original, start_scores;
    public bool match_finishing;
    public YixingLedger ledger;
}
[Serializable]
public sealed class YixingLedger {
    public int? winner, payer, tile;
    public string source;
    public int[] changes;
    public YixingScoreInfo score;
}
[Serializable]
public sealed class YixingScoreInfo {
    public string shape;
    public int qualifying_flowers, base_flowers, multiplier, points, pair;
    public int? winning_triplet;
    public YixingFlowerItem[] patterns, extras;
    public object[][] multipliers;
}
[Serializable]
public sealed class YixingFlowerItem {
    public string id, name;
    public int flowers;
}
