using TMPro;
using UnityEngine;

/// <summary>Compact team score row inside the existing Sichuan Round Panel content bounds.</summary>
public sealed class SichuanRoundScorePanel : MonoBehaviour {
    private readonly SichuanRoundScoreState state = new SichuanRoundScoreState();
    [SerializeField] private TMP_Text blueScore, redScore, caption;

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
        TableText.Set(caption, state.Caption);
        Arrange(!string.IsNullOrEmpty(state.Caption));
    }

    private void Build(TMP_Text template) {
        blueScore = Team("BlueTeam", "蓝队", new Color32(24, 90, 159, 255), new Color32(219, 233, 246, 255), 0, .485f, template);
        redScore = Team("RedTeam", "红队", new Color32(174, 38, 48, 255), new Color32(247, 224, 226, 255), .515f, 1, template);
        caption = Label(transform, "ScoreCaption", template, 14, new Color32(61, 68, 76, 255));
        TableText.Set(caption, "");
        Arrange(false);
    }

    private void Arrange(bool showCaption) {
        caption.gameObject.SetActive(showCaption);
        Stretch(caption.rectTransform, Vector2.zero, new Vector2(1, 0), Vector2.zero, new Vector2(0, 20));
        foreach (var score in new[] { blueScore, redScore }) {
            var cell = (RectTransform)score.transform.parent;
            cell.offsetMin = new Vector2(0, showCaption ? 22 : 0);
            cell.offsetMax = Vector2.zero;
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
        label.enableAutoSizing = false;
        label.overflowMode = TextOverflowModes.Overflow;
        Stretch(label.rectTransform, new Vector2(0, 1), Vector2.one, new Vector2(3, -28), new Vector2(-3, 0));
        var locale = label.GetComponent<LocalizedPlatformLabel>() ?? label.gameObject.AddComponent<LocalizedPlatformLabel>();
        locale.key = title;
        locale.authoredSize = 16;
        locale.minimumSize = 14;
        locale.authoredAutoSize = false;
        var value = Label(cell.transform, "Score", template, 30, ink);
        value.text = "—";
        value.fontSizeMin = 14;
        Stretch(value.rectTransform, Vector2.zero, Vector2.one, new Vector2(4, 1), new Vector2(-4, -28));
        return value;
    }

    private static TMP_Text Label(Transform parent, string name, TMP_Text template, float size, Color color) {
        var existing = parent.Find(name);
        var go = existing ? existing.gameObject : new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
        if (!existing) go.transform.SetParent(parent, false);
        go.layer = parent.gameObject.layer;
        var text = go.GetComponent<TextMeshProUGUI>();
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
