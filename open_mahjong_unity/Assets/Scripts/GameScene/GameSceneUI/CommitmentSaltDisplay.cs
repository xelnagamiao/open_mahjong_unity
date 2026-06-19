using System.Collections.Generic;
using System.Numerics;

/// <summary>
/// 承诺值与盐值的 UI 展示辅助。
/// </summary>
public static class CommitmentSaltDisplay {
    public static string FormatPanelText(string commitment, string salt) {
        string c = NormalizeCommitment(commitment);
        string s = FormatSaltLabel(salt);
        return $"承诺: {c}\n盐: {s}";
    }

    public static string FormatCommitmentLabel(string commitment) {
        return NormalizeCommitment(commitment);
    }

    public static string FormatSaltLabel(string salt) {
        return string.IsNullOrEmpty(salt) ? "-" : salt.Trim();
    }

    public static string ReadCommitmentFromGameTitle(Dictionary<string, object> gameTitle) {
        if (gameTitle == null) return "";
        if (TryReadString(gameTitle, "commitment_hex", out string hex)) return hex;
        if (TryReadString(gameTitle, "commitment", out string commitment)) return commitment;
        return "";
    }

    public static string ReadSaltFromGameTitle(Dictionary<string, object> gameTitle) {
        if (gameTitle == null) return "";
        return TryReadString(gameTitle, "salt", out string salt) ? salt : "";
    }

    /// <summary>牌谱整局结束后写入的主种子（master_seed_hex 或 master_seed）。</summary>
    public static string ReadMasterSeedFromGameTitle(Dictionary<string, object> gameTitle) {
        if (gameTitle == null) return "";
        if (TryReadString(gameTitle, "master_seed_hex", out string hex)) return hex;
        if (TryReadString(gameTitle, "master_seed", out string seed)) return seed;
        return "";
    }

    public static string NormalizeMasterSeed(string masterSeed) {
        return NormalizeCommitment(masterSeed);
    }

    public static string NormalizeCommitment(string commitment) {
        if (string.IsNullOrWhiteSpace(commitment)) return "-";
        string value = commitment.Trim();
        if (value.Length == 64 && IsHexString(value)) return value.ToLowerInvariant();
        if (BigInteger.TryParse(value, out BigInteger parsed)) {
            return parsed.ToString("x64");
        }
        return value;
    }

    private static bool TryReadString(Dictionary<string, object> source, string key, out string result) {
        result = "";
        if (source == null || !source.TryGetValue(key, out object value) || value == null) {
            return false;
        }
        result = value.ToString().Trim().Trim('"');
        return !string.IsNullOrEmpty(result);
    }

    private static bool IsHexString(string value) {
        foreach (char c in value) {
            bool hex = (c >= '0' && c <= '9')
                || (c >= 'a' && c <= 'f')
                || (c >= 'A' && c <= 'F');
            if (!hex) return false;
        }
        return true;
    }
}
