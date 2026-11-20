using TMPro;
using UnityEngine;

/// <summary>Compact team score row inside the existing Sichuan Round Panel content bounds.</summary>
public sealed class SichuanRoundScorePanel : MonoBehaviour {
    private readonly SichuanRoundScoreState state = new SichuanRoundScoreState();
    [SerializeField] private TMP_Text blueScore, redScore;
    private bool? arrangedPaired;

    public static SichuanRoundScorePanel Create(TMP_Text roundLabel) {
        var existing = roundLabel.transform.parent.Find("SichuanTeamScores");
        var host = existing ? existing.gameObject : new GameObject("SichuanTeamScores", typeof(RectTransform));
        if (!existing) host.transform.SetParent(roundLabel.transform.parent, false);
        host.layer = roundLabel.gameObject.layer;
        var rect = (RectTransform)host.transform;
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = new Vector2(9, 5);
        rect.offsetMax = new Vector2(-9, -34);
        var panel = host.GetComponent<SichuanRoundScorePanel>() ?? host.AddComponent<SichuanRoundScorePanel>();
        panel.Build(roundLabel);
        return panel;
    }

    public void Initialize(GameInfo info, string subRule) {
        state.Initialize(info, subRule);
        gameObject.SetActive(state.Enabled);
        if (state.Enabled) Render();
    }

    public void UpdateDuplicate(DuplicateMatchInfo info) {
        state.UpdateDuplicate(info);
        if (state.Enabled) Render();
    }

    public void UpdateReplay(MatchReplayProjection.Scores scores) {
        state.UpdateReplay(scores);
        if (state.Enabled) Render();
    }

    public void ApplySettlement(ShowResultInfo info) {
        state.ApplySettlement(info);
        if (state.Enabled) Render();
    }

    private void Render() {
        // A withheld cross-table score must not hide the team's identity.
        blueScore.transform.parent.gameObject.SetActive(true);
        redScore.transform.parent.gameObject.SetActive(true);
        blueScore.text = state.Format(state.ShowScores ? state.Blue : null);
        redScore.text = state.Format(state.ShowScores ? state.Red : null);
        Arrange();
    }

    private void Build(TMP_Text template) {
        blueScore = Team("BlueTeam", "蓝队", new Color32(24, 90, 159, 255), new Color32(219, 233, 246, 255), 0, .485f, template);
        redScore = Team("RedTeam", "红队", new Color32(174, 38, 48, 255), new Color32(247, 224, 226, 255), .515f, 1, template);
        arrangedPaired = null;
        Arrange();
    }

    private void Arrange() {
        if (arrangedPaired == state.Paired) return;
        arrangedPaired = state.Paired;
        ArrangeTeam(blueScore, state.Unit);
        ArrangeTeam(redScore, state.Unit);
    }

    private static void ArrangeTeam(TMP_Text score, string unitText) {
        var cell = (RectTransform)score.transform.parent;
        var name = cell.Find("TeamName").GetComponent<TMP_Text>();
        cell.offsetMin = Vector2.zero;
        cell.offsetMax = Vector2.zero;
        name.color = Color.black;
        var unit = Label(cell, "Unit", score, 16, unitText == "IMP" ? Color.black : score.color);
        Stretch(name.rectTransform, new Vector2(0, .62f), new Vector2(.53f, 1), new Vector2(8, 0), Vector2.zero);
        Stretch(unit.rectTransform, new Vector2(.53f, .62f), Vector2.one, Vector2.zero, new Vector2(-8, 0));
        Stretch(score.rectTransform, Vector2.zero, new Vector2(1, .62f), new Vector2(4, 0), new Vector2(-4, 0));
        name.alignment = TextAlignmentOptions.MidlineLeft;
        unit.alignment = TextAlignmentOptions.MidlineRight;
        score.alignment = TextAlignmentOptions.Center;
        name.textWrappingMode = TextWrappingModes.NoWrap;
        SetSize(name, 18, 12);
        SetSize(unit, 16, 11);
        SetSize(score, 34, 14);
        TableText.Set(unit, unitText);
    }

    private static void SetSize(TMP_Text text, float size, float minimum) {
        text.enableAutoSizing = true;
        text.fontSize = text.fontSizeMax = size;
        text.fontSizeMin = minimum;
        var locale = text.GetComponent<LocalizedPlatformLabel>();
        if (locale) {
            locale.authoredSize = size;
            locale.minimumSize = minimum;
            locale.authoredAutoSize = true;
            locale.Refresh();
        }
    }

    private TMP_Text Team(string name, string title, Color ink, Color background, float left, float right, TMP_Text template) {
        var existing = transform.Find(name);
        var cell = existing ? existing.gameObject : new GameObject(name, typeof(RectTransform), typeof(UnityEngine.UI.Image));
        if (!existing) cell.transform.SetParent(transform, false);
        cell.layer = gameObject.layer;
        Stretch((RectTransform)cell.transform, new Vector2(left, 0), new Vector2(right, 1), Vector2.zero, Vector2.zero);
        var image = cell.GetComponent<UnityEngine.UI.Image>();
        image.color = background;
        image.raycastTarget = false;
        var label = Label(cell.transform, "TeamName", template, 16, ink);
        label.text = title;
        var locale = label.GetComponent<LocalizedPlatformLabel>() ?? label.gameObject.AddComponent<LocalizedPlatformLabel>();
        locale.key = title;
        var value = Label(cell.transform, "Score", template, 30, ink);
        value.text = "—";
        return value;
    }

    private static TMP_Text Label(Transform parent, string name, TMP_Text template, float size, Color color) {
        var existing = parent.Find(name);
        var text = existing ? existing.GetComponent<TextMeshProUGUI>() : DynamicTextRow.SingleLabel(parent, name);
        var go = text.gameObject;
        go.layer = parent.gameObject.layer;
        text.font = PlatformTypography.Resolve(template.font, "红蓝队");
        text.fontSharedMaterial = text.font.material;
        text.fontStyle = FontStyles.Normal;
        text.fontWeight = FontWeight.Regular;
        text.fontSize = size;
        text.enableAutoSizing = true;
        text.fontSizeMin = size - 2;
        text.fontSizeMax = size;
        text.textWrappingMode = TextWrappingModes.NoWrap;
        text.overflowMode = TextOverflowModes.Overflow;
        text.alignment = TextAlignmentOptions.Center;
        text.color = color;
        text.raycastTarget = false;
        var binding = go.GetComponent<PlatformFontBinding>() ?? go.AddComponent<PlatformFontBinding>();
        binding.authoredFont = TableHudTypography.Body;
        binding.authoredMaterial = TableHudTypography.Body.material;
        binding.authoredStyle = FontStyles.Normal;
        binding.authoredWeight = FontWeight.Regular;
        binding.styleCaptured = true;
        binding.preserveAuthoredTypeface = true;
        binding.Apply();
        return text;
    }

    private static void Stretch(RectTransform rect, Vector2 min, Vector2 max, Vector2 offsetMin, Vector2 offsetMax) {
        rect.anchorMin = min;
        rect.anchorMax = max;
        rect.offsetMin = offsetMin;
        rect.offsetMax = offsetMax;
    }
}
