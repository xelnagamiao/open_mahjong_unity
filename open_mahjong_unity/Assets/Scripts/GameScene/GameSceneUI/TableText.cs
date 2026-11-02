using TMPro;
using System.Globalization;
using UnityEngine;

/// <summary>Updates table UI through the existing source-preserving localization binding.</summary>
public static class TableText {
    public static void Set(TMP_Text label, string source) {
        if (!label) return;
        var binding = label.GetComponent<LocalizedPlatformLabel>();
        if (!binding) {
            float size = label.fontSize, minimum = label.fontSizeMin, maximum = label.fontSizeMax;
            bool autoSize = label.enableAutoSizing;
            binding = label.gameObject.AddComponent<LocalizedPlatformLabel>();
            binding.authoredSize = size;
            binding.minimumSize = Mathf.Min(size, minimum);
            binding.authoredAutoSize = autoSize;
            binding.fitTranslation = false;
            label.fontSize = size; label.fontSizeMin = minimum; label.fontSizeMax = maximum;
            label.enableAutoSizing = autoSize;
        }
        binding.SetSource(source ?? string.Empty);
    }

    public static string SupplementalSource(string source) {
        if (ConfigManager.CurrentLanguage != AppLanguage.English || string.IsNullOrEmpty(source)) return source;
        string key = source.StartsWith("癞：") ? "table.round.joker"
            : source.StartsWith("皮：") ? "table.round.skin" : null;
        if (key == null) return source;
        string[] tiles = source.Substring(2).Split(new[] { ' ' }, System.StringSplitOptions.RemoveEmptyEntries);
        if (tiles.Length == 0) return source;
        for (int i = 0; i < tiles.Length; i++) {
            string tile = ShortTileName(tiles[i]);
            if (tile == null) return source;
            tiles[i] = tile;
        }
        return PlatformText.Get(key) + ": " + string.Join(" ", tiles);
    }

    private static string ShortTileName(string value) {
        if (value.Length == 2 && value[0] >= '1' && value[0] <= '9') {
            switch (value[1]) {
                case '万': return value[0] + "m";
                case '筒': return value[0] + "p";
                case '条': return value[0] + "s";
            }
        }
        switch (value) {
            case "东": return "E";
            case "南": return "S";
            case "西": return "W";
            case "北": return "N";
            case "中": return "R";
            case "白": return "Wh";
            case "发": return "G";
            default: return null;
        }
    }

    public static string FuValue(string source) {
        if (ConfigManager.CurrentLanguage == AppLanguage.English && !string.IsNullOrEmpty(source)
            && source.EndsWith("副") && int.TryParse(source.Substring(0, source.Length - 1),
                NumberStyles.Integer, CultureInfo.InvariantCulture, out int value))
            return value.ToString(CultureInfo.InvariantCulture) + " fu";
        return PlatformText.Get(source);
    }

    public static string SeatWindKey(string wind) {
        switch (wind) {
            case "东": return "table.wind.east";
            case "南": return "table.wind.south";
            case "西": return "table.wind.west";
            case "北": return "table.wind.north";
            default: return wind;
        }
    }

    public static string CompactActionKey(string source) {
        switch (source) {
            case "理": return "table.auto.sort.short";
            case "和": return "table.auto.win.short";
            case "补": return "table.auto.flower.short";
            case "鸣": return "table.auto.pass.short";
            case "切": return "table.auto.cut.short";
            case "展": return "table.auto.options.short";
            default: return source;
        }
    }
}
