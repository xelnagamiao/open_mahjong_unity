using System.Collections.Generic;
using System.Text;

/// <summary>国标局终扩展：局终亮杠快照。</summary>
public class GuobiaoEndResultExtras {
    public Dictionary<int, int[][]> RevealedAngangMasks;
}

/// <summary>
/// 国标结算/流局面板底部附注："某某暗杠: 5m 东"。通过 RuleManifest.SettlementFootnote 接入，
/// 数据来自 GuobiaoGameState.LastEndExtras；错和局不显示。
/// </summary>
internal static class GuobiaoAngangCheck {
    public static string Footnote(SettlementTotalQuery q) {
        if (HuFanHasCuohe(q.HuFan)) return null;
        GuobiaoEndResultExtras extras = GuobiaoGameState.Active?.LastEndExtras;
        if (extras?.RevealedAngangMasks == null || extras.RevealedAngangMasks.Count == 0) return null;
        return BuildText(extras.RevealedAngangMasks);
    }

    static string BuildText(Dictionary<int, int[][]> masksByPlayer) {
        TableMirror mirror = TableMirror.Current;
        var indices = new List<int>(masksByPlayer.Keys);
        indices.Sort();
        var sb = new StringBuilder();
        foreach (int idx in indices) {
            if (!mirror.IndexToPosition.TryGetValue(idx, out string pos)) continue;
            PlayerInfoClass info = mirror.Info(pos);
            if (info == null) continue;
            string tiles = FormatMasks(masksByPlayer[idx]);
            if (string.IsNullOrEmpty(tiles)) continue;
            if (sb.Length > 0) sb.Append('\n');
            sb.Append(info.username).Append("暗杠:").Append(tiles);
        }
        return sb.Length > 0 ? sb.ToString() : null;
    }

    static string FormatMasks(int[][] masks) {
        if (masks == null) return null;
        var sb = new StringBuilder();
        foreach (var mask in masks) {
            if (mask == null) continue;
            for (int i = 1; i < mask.Length; i += 2) {
                if (mask[i] <= 0) continue;
                if (sb.Length > 0) sb.Append(' ');
                sb.Append(TileShort(mask[i]));
            }
        }
        return sb.ToString();
    }

    static string TileShort(int id) {
        if (id == 105) return "5m";
        if (id == 205) return "5p";
        if (id == 305) return "5s";
        if (id >= 41 && id <= 47) {
            string[] z = { "", "东", "南", "西", "北", "中", "白", "发" };
            return z[id - 40];
        }
        int n = id % 10;
        if (id >= 11 && id <= 19) return n + "m";
        if (id >= 21 && id <= 29) return n + "p";
        if (id >= 31 && id <= 39) return n + "s";
        return id.ToString();
    }

    static bool HuFanHasCuohe(string[] huFan) {
        if (huFan == null) return false;
        for (int i = 0; i < huFan.Length; i++) {
            if (huFan[i] == "错和") return true;
        }
        return false;
    }
}
