using System;
using UnityEngine;

/// <summary>规则阅读页。正文摘自规则书；房间配置注明实现出处，不参与计番。</summary>
public static class MahjongRuleGuide {
    [Serializable] public sealed class Section {
        public string id;
        public string title;
        public string body;
        public int[] sourcePages;
        public string[] sources;
    }
    [Serializable] public sealed class Page {
        public string id;
        public string title;
        public Section[] sections;
    }
    [Serializable] public sealed class Document {
        public string id;
        public string source;
        public Page[] pages;
    }
    private static Document guobiao;
    public static Document Guobiao {
        get {
            if (guobiao != null) return guobiao;
            var asset = Resources.Load<TextAsset>("GuobiaoRuleGuide");
            if (!asset) throw new InvalidOperationException("Missing GuobiaoRuleGuide.json");
            guobiao = JsonUtility.FromJson<Document>(asset.text);
            return guobiao;
        }
    }
}
