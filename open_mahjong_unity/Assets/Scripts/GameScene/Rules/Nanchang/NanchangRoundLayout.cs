using System.Collections.Generic;
using UnityEngine;

/// <summary>Fits two labelled jing faces and the hand counter into the existing round panel.</summary>
internal static class NanchangRoundLayout {
    private struct Placement {
        public Vector2 min, max, pivot, position, size;
    }
    private static readonly Dictionary<RectTransform, Placement> placements = new Dictionary<RectTransform, Placement>();
    private static readonly Dictionary<GameObject, bool> visibility = new Dictionary<GameObject, bool>();

    private static void Remember(RectTransform rect) {
        if (rect == null || placements.ContainsKey(rect)) return;
        placements[rect] = new Placement {min=rect.anchorMin,max=rect.anchorMax,pivot=rect.pivot,position=rect.anchoredPosition,size=rect.sizeDelta};
    }
    private static void Box(RectTransform rect, Vector2 position, Vector2 size) {
        if (rect == null) return;
        Remember(rect);
        rect.anchorMin=rect.anchorMax=rect.pivot=new Vector2(0,1);
        rect.anchoredPosition=position;rect.sizeDelta=size;
    }
    private static void Hide(Transform item) {
        if (item == null) return;
        if (!visibility.ContainsKey(item.gameObject)) visibility[item.gameObject]=item.gameObject.activeSelf;
        item.gameObject.SetActive(false);
    }
    public static void Apply() {
        var panel=RoundPanel.Instance?.GetComponentInChildren<RiichiRoundPanel>(true);
        var root=panel?.transform as RectTransform;
        if (root == null) return;
        float width=root.rect.width;
        Box(root,Vector2.zero,new Vector2(width,193));
        var header=root.Find("Header") as RectTransform;
        Box(header,Vector2.zero,new Vector2(width,43));
        var content=root.Find("Content") as RectTransform;
        Box(content,new Vector2(0,-43),new Vector2(width,150));
        if (content == null) return;
        Box(content.Find("DoraContainer") as RectTransform,new Vector2(10,-4),new Vector2(width-20,84));
        Box(content.Find("NowRound") as RectTransform,new Vector2(12,-108),new Vector2(94,32));
        Box(content.Find("IndicatorCounter") as RectTransform,new Vector2(112,-108),new Vector2(width-122,32));
        Hide(content.Find("IndicatorCaption"));
        Hide(content.Find("RandomSeed"));
        Hide(root.Find("RandomorFushi"));
        TileFaceFit.FitRowHeight(content.Find("DoraContainer"));
    }
    public static void Restore() {
        foreach (var pair in placements) {
            var rect=pair.Key;if (rect == null) continue;
            var p=pair.Value;
            rect.anchorMin=p.min;rect.anchorMax=p.max;rect.pivot=p.pivot;rect.anchoredPosition=p.position;rect.sizeDelta=p.size;
        }
        foreach (var pair in visibility) if (pair.Key != null) pair.Key.SetActive(pair.Value);
        placements.Clear();visibility.Clear();
    }
}
