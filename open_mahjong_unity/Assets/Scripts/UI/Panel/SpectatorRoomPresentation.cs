using System;
using UnityEngine;

/// <summary>Presentation of public spectator metadata; never infers teams from seating.</summary>
public static class SpectatorRoomPresentation {
    public enum RoomFilter { All, Custom, Ranked }
    public enum FormatFilter { All, Individual, Doubles }
    public static readonly string[] TierKeys = { "", "mcrpl", "advanced", "intermediate", "beginner", "elo" };

    public static bool IsRanked(SpectatorInfo info) => info != null &&
        (string.Equals(info.room_type, "match", StringComparison.OrdinalIgnoreCase) ||
         string.IsNullOrEmpty(info.room_type) && !string.IsNullOrEmpty(info.match_queue_type));

    public static string Tier(SpectatorInfo info) {
        if (!IsRanked(info)) return "";
        foreach (string key in TierKeys) {
            if (key.Length == 0) continue;
            if (string.Equals(info.match_tier, key, StringComparison.OrdinalIgnoreCase)) return key;
        }
        foreach (string token in (info.match_queue_type ?? "").ToLowerInvariant().Split('_'))
            foreach (string key in TierKeys) if (key.Length > 0 && token == key) return key;
        return "";
    }

    public static bool IsDoubles(SpectatorInfo info) => info != null &&
        (info.sub_rule == "sichuan/doubles" || info.sub_rule == "sichuan/xueliu_doubles");

    public static bool HasTeams(SpectatorInfo info) {
        if (!IsDoubles(info) || info.team_by_player_index == null || info.team_by_player_index.Length != 4) return false;
        int blue = 0, red = 0;
        foreach (int team in info.team_by_player_index) { if (team == 0) blue++; else if (team == 1) red++; }
        return blue == 2 && red == 2;
    }

    public static int Priority(SpectatorInfo info) {
        if (!IsRanked(info)) return -1;
        switch (Tier(info)) {
            case "mcrpl": return 5;
            case "advanced": return 4;
            case "intermediate": return 3;
            case "beginner": return 2;
            case "elo": return 1;
            default: return 0;
        }
    }

    public static bool Matches(SpectatorInfo info, RoomFilter room, string tier, FormatFilter format) {
        if (info == null) return false;
        if (room == RoomFilter.Ranked && !IsRanked(info) || room == RoomFilter.Custom && IsRanked(info)) return false;
        if (!string.IsNullOrEmpty(tier) && Tier(info) != tier) return false;
        return format == FormatFilter.All || (format == FormatFilter.Doubles ? IsDoubles(info) : !IsDoubles(info));
    }

    // Same hues as the authored ranked lobby cards.
    public static Color Accent(SpectatorInfo info) {
        switch (Tier(info)) {
            case "mcrpl": return new Color32(255, 3, 0, 255);
            case "advanced": return new Color32(255, 143, 0, 255);
            case "intermediate": return new Color32(56, 162, 176, 255);
            case "beginner": case "elo": return new Color32(85, 144, 98, 255);
            default: return new Color32(144, 144, 144, 255);
        }
    }
}
