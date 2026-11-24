using System.Collections.Generic;

// 换六张消息按专用类型解析；普通Response未声明state，会丢失这份局内状态。
public sealed class ChangshaExchangeSixResponse : Response {
    public ChangshaExchangeSixState state;
    private static readonly Newtonsoft.Json.JsonSerializerSettings LosslessRecordSettings = new Newtonsoft.Json.JsonSerializerSettings {
        DateParseHandling = Newtonsoft.Json.DateParseHandling.None
    };

    public static string ReadMessageType(string json) {
        using (var reader = new Newtonsoft.Json.JsonTextReader(new System.IO.StringReader(json))) {
            while (reader.Read()) {
                if (reader.TokenType != Newtonsoft.Json.JsonToken.PropertyName || reader.Depth != 1) continue;
                bool isType = (string)reader.Value == "type";
                if (!reader.Read()) break;
                if (isType) return reader.Value as string;
                reader.Skip();
            }
        }
        return null;
    }

    public static Response Decode(string json) => Decode(json, ReadMessageType(json));

    public static Response Decode(string json, string type) {
        if (type == "gamestate/changsha/exchange_six_state")
            return Newtonsoft.Json.JsonConvert.DeserializeObject<ChangshaExchangeSixResponse>(json, LosslessRecordSettings);
        return Newtonsoft.Json.JsonConvert.DeserializeObject<Response>(json, LosslessRecordSettings);
    }
}

public sealed class ChangshaExchangeSixState {
    public string phase;
    public bool restoring;
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
    public ChangshaExchangeSixChoice exchange_choice;
    public int[] submitted_seats;
    public int[] disqualified_seats;
    public int[] reported_ready_seats;
    public int[] open_hand_seats;
    public Dictionary<int, int[]> open_hands;
    public Dictionary<int, int[]> four_joy_revealed;
    public int[] claim_tiles;
    public int[] kong_tiles;
    public ChangshaExchangeSixPendingKong pending_kong;
    public int[] kong_dice;
    public int[] kong_positions;
    public bool blind_kong;
    public bool hidden_draw_pending;
    public int current_player_index;
    public Dictionary<int, int> hand_counts;
    public int remaining_tiles;
    public ChangshaExchangeSixSettlement[] settlements;
}

public sealed class ChangshaExchangeSixPendingKong {
    public int player_index;
    public int river_start;
    public bool discarded;
}

public sealed class ChangshaExchangeSixChoice {
    public int? seat;
    public string action;
    public int round;
}

public sealed class ChangshaExchangeSixPrivate {
    public int[] hand_tiles;
    public int[] selected_tiles;
    public int[] received_tiles;
    public string[] action_list;
    public Dictionary<string, int[]> targets;
    public int[] cut_tiles;
    public bool has_draw_slot;
    public bool open_hand_pass_hint;
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
