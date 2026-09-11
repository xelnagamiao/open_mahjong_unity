using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>The approved flat center designs; archived experimental shells are not game options.</summary>
public static class CenterDisplayStyles
{
    public const string Classic = "classic";
    public const string SimpleNavy = "simple_navy";
    public const string OriginalFlat = "original_flat";

    public sealed class Entry
    {
        public string Id { get; }
        public string Name { get; }
        public int Index { get; }
        internal Entry(string id, string name, int index) { Id = id; Name = name; Index = index; }
    }

    public static IReadOnlyList<Entry> All { get; } = Array.AsReadOnly(new[]
    {
        new Entry(Classic, "项目默认", 0),
        new Entry("refined", "原版微调", 1),
        new Entry("ink", "青墨轻色", 2),
        new Entry("ivory", "月白轻色", 3),
        new Entry("violet", "靛紫轻色", 4),
        new Entry(SimpleNavy, "简洁深蓝", 5),
        new Entry(OriginalFlat, "原版·简化", 6)
    });

    public static Entry Get(string id)
    {
        foreach (var style in All)
            if (string.Equals(style.Id, id, StringComparison.Ordinal)) return style;
        return All[0];
    }

    public static string Normalize(string id) => Get(id).Id;
    public static int IndexOf(string id) => Get(id).Index;
    public static Texture2D GetPreview(string id) => Resources.Load<Texture2D>(
        "image/Board/CenterDisplay/" + Normalize(id) + "_preview");
}
