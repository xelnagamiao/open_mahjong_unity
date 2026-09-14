using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>Selectable original designs and explicitly restored trials. Retired IDs fall back to the project default.</summary>
public static class CenterDisplayStyles
{
    public const string Classic = "classic";
    public const string OriginalFlat = "original_flat";
    public const string StudioIndigo = "studio_indigo";
    public const string StudioPaper = "studio_paper";
    public const string StudioBamboo = "studio_bamboo";
    public const string TrialCobalt = "trial_cobalt";
    public const string TrialJade = "trial_jade";
    public const string TrialIvory = "trial_ivory";

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
        new Entry(StudioIndigo, "折光·靛蓝", 1),
        new Entry(StudioPaper, "花笺·绛红", 2),
        new Entry(StudioBamboo, "竹影·青", 3),
        new Entry(TrialCobalt, "试作·钴蓝", 4),
        new Entry(TrialJade, "试作·碧青", 5),
        new Entry(TrialIvory, "试作·象牙", 6),
        new Entry("refined", "原版微调", 7),
        new Entry("ink", "青墨轻色", 8),
        new Entry("ivory", "月白轻色", 9),
        new Entry("violet", "靛紫轻色", 10),
        new Entry(OriginalFlat, "原版·简化", 11)
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
