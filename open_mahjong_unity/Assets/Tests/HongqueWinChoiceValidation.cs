#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using UnityEngine;

public static class HongqueWinChoiceValidation {
    public static object Run() {
        var checks = new List<string>();
        void Check(bool passed, string label) {
            if (!passed) throw new InvalidOperationException(label);
            checks.Add(label);
        }
        var offered = JsonConvert.DeserializeObject<HongqueCandidateInfo[]>(@"[
            { 'id':'win-max', 'kind':'win', 'base':9, 'fan_total':17, 'points':153,
              'groups':[['CX1','CY1','DX1','DY1'],['DY7','EX7','EY7','FX7','FY7','GX7','GY7','AX7']],
              'kong_steps':[{'meld_index':1,'hand_tiles':['AX7'],'tiles':['FY7','GX7','GY7','AX7']}] },
            { 'id':'win-four', 'kind':'win', 'base':6, 'fan_total':18, 'points':108,
              'groups':[['DY7','EX7','EY7','FX7'],['CX1','CY1','DX1','DY1'],['FY7','GX7','GY7','AX7']] },
            { 'id':'win-three', 'kind':'win', 'base':6, 'fan_total':17, 'points':102,
              'groups':[['DY7','EX7','EY7'],['CX1','CY1','DX1','DY1'],['FY7','GX7','GY7','AX7','FX7']] }
        ]");
        HongqueActionWords.RegisterAll();
        var self = HongqueActionWords.BuildWinChoices(offered, true);
        var ron = HongqueActionWords.BuildWinChoices(offered, false);
        Check(self.Count == 3 && ron.Count == 3, "all high and low score choices remain selectable");
        Check(self.Select(c => c.Caption).SequenceEqual(new[] { "17番  153分", "18番  108分", "17番  102分" }),
            "headers preserve server fan and points without extra labels");
        Check(self.Select(c => c.TileGroups.Length).SequenceEqual(new[] { 2, 3, 3 }), "final group boundaries are preserved");
        Check(self.All(c => c.TileIds.Length == 12 && c.TileIds.Distinct().Count() == 12), "each preview contains every actual tile exactly once");
        Check(self[1].ActionType == HongqueActionWords.WinPrefix + "win-four"
            && ron[1].ActionType == HongqueActionWords.ClaimPrefix + "win-four", "lower score choice retains identity and native action route");
        Check(offered[0].kong_steps[0].hand_tiles[0] == "AX7", "wire payload retains kong execution steps");
        foreach (bool draw in new[] { true, false }) {
            string group = HongqueActionWords.WinAction(offered, draw);
            Check(ActionWords.LabelOf(group) == "和" && ActionWords.RequiresManualChoice(group), "multiple results require choice without changing the button label: " + draw);
            string only = HongqueActionWords.WinAction(new[] { offered[1] }, draw);
            Check(only == (draw ? HongqueActionWords.WinPrefix : HongqueActionWords.ClaimPrefix) + "win-four", "single result executes its explicit candidate: " + draw);
            Check(!ActionWords.TryExpand(only, out _), "single result does not open a selector: " + draw);
        }
        Check(HongqueActionWords.WinAction(Array.Empty<HongqueCandidateInfo>(), true) == HongqueActionWords.Win,
            "older self-draw payload remains compatible");
        return new { count = checks.Count, checks };
    }

    public static object RunLayout() {
        var checks = new List<string>();
        var root = new GameObject("HongqueWinChoiceLayoutValidation", typeof(RectTransform), typeof(Canvas));
        var canvas = root.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        try {
            foreach (float viewport in new[] { 720f, 1280f, 1920f }) {
                canvas.scaleFactor = Screen.width / viewport;
                var popup = new GameObject("Popup", typeof(RectTransform));
                popup.transform.SetParent(root.transform, false);
                ((RectTransform)popup.transform).anchoredPosition = new Vector2(0, -100);
                for (int i = 0; i < 12; i++) {
                    var row = new GameObject("Choice" + i, typeof(RectTransform), typeof(ActionBlock));
                    row.transform.SetParent(popup.transform, false);
                    ((RectTransform)row.transform).sizeDelta = new Vector2(i % 2 == 0 ? 1140 : 1200, 170);
                    row.GetComponent<ActionBlock>().actionType = HongqueActionWords.WinPrefix + i;
                }
                Canvas.ForceUpdateCanvases();
                var layout = UpwardPopupLayout.ForActions(popup.transform, null, true);
                var seen = new List<string>();
                for (int page = 0; page < layout.PageCount; page++) {
                    layout.SetPage(page);
                    var visible = popup.GetComponentsInChildren<ActionBlock>().Select(b => (RectTransform)b.transform).ToArray();
                    if (visible.Select(r => r.anchoredPosition.y).Distinct().Count() != visible.Length)
                        throw new InvalidOperationException("Win choices must be one per row");
                    for (int i = 1; i < visible.Length; i++) {
                        if (visible[i - 1].anchoredPosition.y <= visible[i].anchoredPosition.y)
                            throw new InvalidOperationException("Win choices must run from top to bottom");
                    }
                    foreach (var row in visible) {
                        var corners = new Vector3[4];
                        row.GetWorldCorners(corners);
                        if (corners.Any(p => p.x < -.1f || p.x > Screen.width + .1f || p.y < -.1f || p.y > Screen.height + .1f))
                            throw new InvalidOperationException("Win choice is outside viewport");
                        seen.Add(row.name);
                    }
                }
                if (!seen.SequenceEqual(Enumerable.Range(0, 12).Select(i => "Choice" + i)))
                    throw new InvalidOperationException("Every win choice must remain reachable in server order");
                checks.Add("single column, full ordered coverage and screen bounds: " + viewport);
                UnityEngine.Object.DestroyImmediate(popup);
            }
            return new { count = checks.Count, checks };
        } finally {
            UnityEngine.Object.DestroyImmediate(root);
        }
    }

    public static object RunVirtualPagination() => ActionChoicePanelValidation.Run();
}
#endif
