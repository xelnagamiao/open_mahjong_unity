using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>《新编 MCR》第七章的阅读用番表；不参与对局计番。</summary>
public static class GuobiaoFanCatalog {
    public static readonly int[] FanValues = { 1, 2, 4, 5, 6, 8, 12, 16, 24, 32, 48, 64, 88 };

    [Serializable]
    public sealed class Entry {
        public int number;
        public string name;
        public int fan;
        public string category;
        public string valueText;
        public int sourcePage;
        public string description;
        public string note;
        public string context;
        public string example;
        // Flattened example indices; the scoring tiles remain unchanged.
        public int[] faceDownTiles;
        public string CategoryKey => string.IsNullOrEmpty(category) ? fan.ToString() : category;

        public int[][] ExampleGroups() {
            if (string.IsNullOrWhiteSpace(example)) return Array.Empty<int[]>();
            string[] groups = example.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            var result = new int[groups.Length][];
            for (int i = 0; i < groups.Length; i++) {
                string group = groups[i];
                if (group[0] >= 'A' && group[0] <= 'G') {
                    string[] codes = group.Split(',');
                    result[i] = new int[codes.Length];
                    for (int j = 0; j < codes.Length; j++) {
                        result[i][j] = HongqueTileVisual.FromCode(codes[j]);
                        if (result[i][j] == 0) throw new FormatException("Invalid Hongque tile: " + codes[j]);
                    }
                    continue;
                }
                char suit = group[group.Length - 1];
                result[i] = new int[group.Length - 1];
                for (int j = 0; j < group.Length - 1; j++) {
                    int value = group[j] - '0';
                    // 本项目牌号：万 11–19，饼 21–29，条 31–39，中 45、白 46、发 47。
                    switch (suit) {
                        case 'm': result[i][j] = 10 + value; break;
                        case 'p': result[i][j] = 20 + value; break;
                        case 's': result[i][j] = 30 + value; break;
                        case 'z': result[i][j] = value == 6 ? 47 : value == 7 ? 46 : 40 + value; break;
                        case 'f': result[i][j] = 50 + value; break;
                        default: throw new FormatException("Unknown example suit: " + suit);
                    }
                }
            }
            return result;
        }
    }

    [Serializable] private sealed class Document { public string source; public Entry[] entries; }
    private static Document document;
    public static IReadOnlyList<Entry> Entries {
        get {
            if (document == null) {
                var asset = Resources.Load<TextAsset>("GuobiaoFanCatalog");
                if (!asset) throw new InvalidOperationException("Missing GuobiaoFanCatalog.json");
                document = JsonUtility.FromJson<Document>(asset.text);
            }
            return document.entries;
        }
    }
}
