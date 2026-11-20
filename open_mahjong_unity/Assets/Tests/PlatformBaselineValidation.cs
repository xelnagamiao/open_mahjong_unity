using System;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public static class PlatformBaselineValidation
{
    public static int CheckMatchCard(GameObject prefab, bool hasScores)
    {
        var canvas = new GameObject("Card baseline validation", typeof(RectTransform), typeof(Canvas));
        try
        {
            canvas.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            var card = UnityEngine.Object.Instantiate(prefab, canvas.transform, false);
            card.SetActive(true);
            var closed = card.transform.Find("ClosedPlayers");
            closed.gameObject.SetActive(true);
            var labels = card.GetComponentsInChildren<TextMeshProUGUI>(true);
            var rule = labels.Single(t => t.name == "GameRule");
            var id = labels.Single(t => t.name == "GameStateID");
            var progress = labels.Single(t => t.name == "RoundProgress");
            PlatformBaselineRow Row(TextMeshProUGUI label)
            {
                var member = label.GetComponent<PlatformTextBaseline>();
                if (!member || member.mode != PlatformTextBaseline.LayoutMode.Row || !member.row)
                    throw new Exception(prefab.name + " missing authored row on " + label.name);
                return member.row;
            }
            var title = Row(rule);
            if (Row(id) != title || Row(progress) != title) throw new Exception(prefab.name + " title row split");
            var players = labels.Where(t => t.name.StartsWith("PlayerName", StringComparison.Ordinal)).ToArray();
            var scores = labels.Where(t => t.name.StartsWith("Score", StringComparison.Ordinal)).ToArray();
            if (players.Length != 8 || scores.Length != (hasScores ? 8 : 0)) throw new Exception(prefab.name + " roster size");
            var members = players.Concat(scores).ToArray();
            var openRows = members.Where(t => !t.transform.IsChildOf(closed)).Select(Row).Distinct().ToArray();
            var closedRows = members.Where(t => t.transform.IsChildOf(closed)).Select(Row).Distinct().ToArray();
            if (openRows.Length != 1 || closedRows.Length != 1 || openRows[0] == closedRows[0]
                || openRows[0] == title || closedRows[0] == title) throw new Exception(prefab.name + " rooms share a guide");
            id.text = "比赛 test-123";
            rule.text = "四川";
            progress.text = "1/2";
            foreach (var member in members) member.text = "123";
            var checkedLabels = new[] { rule, id, progress }.Concat(members).ToArray();
            void Render()
            {
                Canvas.ForceUpdateCanvases();
                foreach (var label in checkedLabels) label.ForceMeshUpdate(true, true);
            }
            float Y(TextMeshProUGUI label) => card.transform.InverseTransformPoint(
                label.transform.TransformPoint(new Vector3(0, label.textInfo.lineInfo[0].baseline, 0))).y;
            Render();
            var before = checkedLabels.Select(Y).ToArray();
            title.verticalOffset += 5;
            id.fontSize *= .85f;
            id.font = PlatformTypography.LatinBold;
            Render();
            for (int i = 0; i < checkedLabels.Length; i++)
            {
                var label = checkedLabels[i];
                if (!label.GetComponent<PlatformTextBaseline>().Applied ||
                    Mathf.Abs(Y(label) - (i < 3 ? Y(rule) : before[i])) > .05f)
                    throw new Exception(prefab.name + " rendered baseline mismatch on " + label.name);
            }
            if (Mathf.Abs(Y(rule) - before[0] - 5) > .05f)
                throw new Exception(prefab.name + " title guide failed to move all members");
            return checkedLabels.Length;
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(canvas);
        }
    }

    public static string Run()
    {
        int checks = 0;
        void Check(bool condition, string message)
        {
            if (!condition) throw new Exception(message);
            checks++;
        }

        var root = new GameObject("Baseline validation", typeof(RectTransform), typeof(Canvas));
        try
        {
            root.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            var host = new GameObject("Card", typeof(RectTransform)).GetComponent<RectTransform>();
            host.SetParent(root.transform, false);
            host.sizeDelta = new Vector2(960, 64);
            var header = host.gameObject.AddComponent<PlatformBaselineRow>();
            header.referenceSize = 25;
            var other = host.gameObject.AddComponent<PlatformBaselineRow>();
            other.referenceSize = 25;
            other.verticalOffset = -32;

            TextMeshProUGUI Label(string name, Vector2 position, float size, PlatformBaselineRow row = null)
            {
                var label = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI)).GetComponent<TextMeshProUGUI>();
                label.transform.SetParent(host, false);
                label.rectTransform.sizeDelta = new Vector2(280, 64);
                label.rectTransform.anchoredPosition = position;
                label.font = PlatformTypography.ChineseRegular;
                label.fontSize = size;
                label.alignment = TextAlignmentOptions.MidlineLeft;
                label.textWrappingMode = TextWrappingModes.NoWrap;
                label.text = "比赛 ID 123";
                if (row) label.gameObject.AddComponent<PlatformTextBaseline>().Configure(PlatformTextBaseline.LayoutMode.Row, row);
                return label;
            }
            void Render(params TextMeshProUGUI[] labels)
            {
                Canvas.ForceUpdateCanvases();
                foreach (var label in labels) label.ForceMeshUpdate(true, true);
            }
            float Y(TextMeshProUGUI label) => host.InverseTransformPoint(
                label.transform.TransformPoint(new Vector3(0, label.textInfo.lineInfo[0].baseline, 0))).y;
            bool Near(float a, float b) => Mathf.Abs(a - b) < 0.02f;

            var rule = Label("Rule", new Vector2(-300, 0), 25, header);
            var id = Label("ID", new Vector2(0, 8), 23, header);
            var progress = Label("Progress", new Vector2(300, 0), 20, header);
            var score = Label("Second row", new Vector2(0, 8), 23, other);
            var plain = Label("Unbound", new Vector2(0, 8), 50);
            var policy = id.GetComponent<PlatformTextBaseline>();
            Vector2 originalPosition = id.rectTransform.anchoredPosition;
            Vector4 originalMargin = id.margin;
            foreach (string value in new[] { "ABC123", "gypq_123", "比赛 test-pai", "比赛 888888", "abc", "999999" })
            {
                id.text = value;
                Render(rule, id, progress, score, plain);
                Check(Near(Y(rule), Y(id)) && Near(Y(id), Y(progress)), "Authored row: " + value);
            }
            Check(Near(Y(id) - Y(score), 32), "Independent authored rows on the same parent");
            Check(!plain.GetComponent<PlatformTextBaseline>(), "Unconfigured text must retain native TMP layout");
            foreach (var font in new[] { PlatformTypography.ChineseRegular, PlatformTypography.LatinBold, TMP_Settings.defaultFontAsset })
            {
                id.font = font;
                Render(rule, id);
                Check(Near(Y(rule), Y(id)), "Font switch");
            }
            id.font = PlatformTypography.ChineseRegular;
            id.fontSize = 16;
            Render(rule, id);
            Check(Near(Y(rule), Y(id)), "Changed font size");
            id.enableAutoSizing = true;
            id.fontSizeMin = 10;
            id.fontSizeMax = 23;
            id.text = new string('8', 45);
            Render(rule, id);
            Check(id.fontSize < 23 && Near(Y(rule), Y(id)), "Long ID actually shrinks and stays on the authored line");
            id.enableAutoSizing = false;
            id.fontSize = 23;
            id.text = "gyp";
            Render(id);
            float previous = Y(id);
            header.verticalOffset += 7;
            Render(rule, id, progress, score);
            Check(Near(Y(rule), previous + 7) && Near(Y(id), previous + 7) && Near(Y(progress), previous + 7), "Moving the row guide");
            Check(Near(Y(score), previous - 32), "Moving one guide leaves the other row in place");
            for (int i = 0; i < 3; i++)
            {
                id.gameObject.SetActive(false);
                id.gameObject.SetActive(true);
                Render(id);
                Check(Near(Y(id), previous + 7), "Pooled label retained authored row");
            }
            Check(id.rectTransform.anchoredPosition == originalPosition && id.margin == originalMargin, "Authored transforms and margins preserved");
            id.text = "first\nsecond";
            Render(id);
            Check(!policy.Applied, "Paragraph retains TMP layout");
            id.text = "ID";
            policy.Configure(PlatformTextBaseline.LayoutMode.Preserve, null);
            Render(id);
            Check(!policy.Applied, "Explicit preserve");
            var control = new GameObject("Input", typeof(RectTransform), typeof(TMP_InputField));
            control.transform.SetParent(host, false);
            id.transform.SetParent(control.transform, false);
            policy.Configure(PlatformTextBaseline.LayoutMode.Row, header);
            Render(id);
            Check(!policy.Applied, "Input caret layout protected");
            id.transform.SetParent(host, false);
            id.text = "<u>gyp</u>";
            id.richText = true;
            Render(rule, id);
            Check(Near(Y(rule), Y(id)), "Underline and fallback text");
            Check(Near(id.textInfo.characterInfo[0].baseLine, id.textInfo.lineInfo[0].baseline), "Text hit-test matches glyph baseline");
            host.localScale = new Vector3(.75f, .75f, 1);
            Render(rule, id);
            Check(Near(Y(rule), Y(id)), "Scaled canvas");
            var first = DynamicTextRow.Create(host, "Score row 1");
            var second = DynamicTextRow.Create(host, "Score row 2");
            var a = first.Member(first.transform, "A");
            var b = first.Member(first.transform, "B");
            var c = second.Member(second.transform, "C");
            var firstGuide = first.GetComponent<PlatformBaselineRow>();
            var secondGuide = second.GetComponent<PlatformBaselineRow>();
            Check(firstGuide && a.GetComponent<PlatformTextBaseline>().row == firstGuide &&
                b.GetComponent<PlatformTextBaseline>().row == firstGuide, "Spawned members use their row instance");
            Check(secondGuide && secondGuide != firstGuide && c.GetComponent<PlatformTextBaseline>().row == secondGuide,
                "Each generated score row has an independent authored guide");
            var single = DynamicTextRow.SingleLabel(host, "Status");
            Check(single.GetComponent<PlatformTextBaseline>().mode == PlatformTextBaseline.LayoutMode.SingleLine,
                "Standalone dynamic label inherits prefab policy");
            return Newtonsoft.Json.JsonConvert.SerializeObject(new { passed = checks, failed = 0 });
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(root);
        }
    }
}
