using System;

/// <summary>MIL 贵州 2023 的公开事件与局终账本；客户端不据此裁定合法性。</summary>
[Serializable]
public sealed class GuizhouInfo {
    public string rule_version, phase;
    public int hand_number, total_hands;
    public bool match_finishing, opening_revealed;
    public bool self_has_draw_slot;
    public int[] seat_to_original, start_scores;
    public GuizhouChicken[] chickens;
    public GuizhouKong[] kongs;
    public GuizhouLedger ledger;
}

[Serializable]
public sealed class GuizhouChicken {
    public int tile, supplier;
    public int? claimant;
    public bool won;
}

[Serializable]
public sealed class GuizhouKong {
    public int owner, tile;
    public string kind;
    public int? supplier;
    public bool hanbao;
}

[Serializable]
public sealed class GuizhouLedger {
    public int? indicator, chicken_tile;
    public bool[] ready;
    public int[] score_changes;
    public GuizhouTransfer[] transfers;
}

[Serializable]
public sealed class GuizhouTransfer {
    public int payer, payee, points;
    public string category, reason;
    public int? tile;
}

[Serializable]
public sealed class GuizhouFan {
    public string id, name;
    public int points;
}
