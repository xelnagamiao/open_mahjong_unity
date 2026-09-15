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
        new Entry(Classic, "标准", 0),
        new Entry(StudioIndigo, "蓝色折角", 1),
        new Entry(StudioPaper, "米白细线", 2),
        new Entry(StudioBamboo, "绿色细线", 3),
        new Entry(TrialCobalt, "纯色深蓝", 4),
        new Entry(TrialJade, "纯色青绿", 5),
        new Entry(TrialIvory, "纯色米白", 6),
        new Entry("refined", "蓝灰双框", 7),
        new Entry("ink", "青灰双框", 8),
        new Entry("ivory", "浅灰双框", 9),
        new Entry("violet", "紫灰双框", 10),
        new Entry(OriginalFlat, "蓝灰窄框", 11)
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
