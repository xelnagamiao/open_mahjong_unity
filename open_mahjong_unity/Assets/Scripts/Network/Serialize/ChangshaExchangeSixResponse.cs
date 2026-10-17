using System.Collections.Generic;

// 换六张消息按专用类型解析；普通Response未声明state，会丢失这份局内状态。
public sealed class ChangshaExchangeSixResponse : Response {
    public ChangshaExchangeSixState state;

    public static Response Decode(string json) {
        return Newtonsoft.Json.Linq.JObject.Parse(json).Value<string>("type") == "gamestate/changsha/exchange_six_state"
            ? Newtonsoft.Json.JsonConvert.DeserializeObject<ChangshaExchangeSixResponse>(json)
            : Newtonsoft.Json.JsonConvert.DeserializeObject<Response>(json);
    }
}

public sealed class ChangshaExchangeSixState {
    public string phase;
    public int revision;
    public int action_tick;
    public int remaining_time;
    public int step_remaining;
    public ChangshaExchangeSixPublic public_state;
    public ChangshaExchangeSixPrivate private_state;
}

public sealed class ChangshaExchangeSixPublic {
    public int rule_version;
    public int[] deal_dice;
    public int? small_hu_forbidden_seat;
    public int[] dice;
    public int source_offset;
    public int exchange_round;
    public int completed_exchange_rounds;
    public int exchange_selection_seconds = 30;
    public int[] submitted_seats;
    public int[] disqualified_seats;
    public int[] reported_ready_seats;
    public int[] open_hand_seats;
    public Dictionary<int, int[]> open_hands;
    public Dictionary<int, int[]> four_joy_revealed;
    public int[] claim_tiles;
    public int[] kong_tiles;
    public int[] kong_dice;
    public int[] kong_positions;
    public bool blind_kong;
    public bool hidden_draw_pending;
    public int current_player_index;
    public Dictionary<int, int> hand_counts;
    public int remaining_tiles;
    public ChangshaExchangeSixSettlement[] settlements;
}

public sealed class ChangshaExchangeSixPrivate {
    public int[] hand_tiles;
    public int[] selected_tiles;
    public string[] action_list;
    public Dictionary<string, int[]> targets;
    public int[] cut_tiles;
    public bool has_draw_slot;
    public Newtonsoft.Json.Linq.JObject local_hint_context;
}

public sealed class ChangshaExchangeSixWait {
    public int tile;
    public int score;
    public string[] fans;
    public bool blocked_small_hu;
}

public sealed class ChangshaExchangeSixGroup {
    public string[] patterns;
    public string[] context;
    public string[] fans;
    public int[] tiles;
    public int[] hand;
}

public sealed class ChangshaExchangeSixPayer {
    public int payer;
    public int[] group_bases;
    public int @base;
    public int hit_count;
    public int payment;
}

public sealed class ChangshaExchangeSixSettlement {
    public int winner;
    public ChangshaExchangeSixGroup[] groups;
    public ChangshaExchangeSixPayer[] payer_details;
    public int payment;
    public int[][] combination_masks;
}
