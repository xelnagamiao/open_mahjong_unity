using UnityEngine;
using UnityEngine.UI;
using TMPro;
public class TipsFanCount : MonoBehaviour
{
    private TMP_Text thirdHintText;
    private TipsCornerFrame thirdChip;
    private const float RowHeight = 33f;
    private const float RowGap = 11f;
    private const float SecondRowOffset = RowHeight + RowGap;

    [SerializeField] private TMP_Text tipsFanCountText; // 提示番数文本
    [SerializeField] private Image backgroundImage; // 背景图片组件
    [SerializeField] private TMP_Text secondHintText;
    [SerializeField] private TipsCornerFrame firstChip;
    [SerializeField] private TipsCornerFrame secondChip;

    [Header("颜色设置")]
    public Color dianheColor = Color.green; // 点和颜色（绿色，可和牌）
    public Color zimoColor = Color.yellow; // 自摸颜色（黄色，仅自摸）
    public Color wuyiColor = new Color(1f, 0.647f, 0f); // 无役颜色（橙色，未起和）
    public Color exhaustedColor = new Color(0.55f, 0.55f, 0.55f, 1f); // 听牌张已用尽（灰色）

    public void SetTipsFanCount(string fanCount, string colorType) {
        string[] lines = (fanCount ?? "").Split(new[] {'\n'}, 3);
        tipsFanCountText.text = lines[0];
        bool twoLines = lines.Length > 1;
        secondHintText.text = twoLines ? lines[1] : "";
        secondHintText.gameObject.SetActive(twoLines);
        secondChip.gameObject.SetActive(twoLines);
        // A count-only hint occupies the first row, but must not inherit the fan status color.
        string firstLine = lines[0].Trim();
        bool countOnly = !twoLines && firstLine.EndsWith("枚")
            && int.TryParse(firstLine.Substring(0, firstLine.Length - 1), out _);
        Color countColor = new Color32(70,86,126,255);
        Color fanColor = colorType == "exhausted" ? exhaustedColor
            : colorType == "zimo" ? zimoColor
            : colorType == "wuyi" ? wuyiColor
            : colorType == "dianhe" ? dianheColor : countColor;
        SetChipColor(firstChip, countOnly ? countColor : fanColor);
        bool selfDrawLine = twoLines && lines[1].StartsWith("自摸");
        SetChipColor(secondChip, selfDrawLine ? (colorType == "exhausted" ? exhaustedColor : zimoColor) : countColor);
        if (lines.Length > 2 && thirdHintText == null) {
            thirdHintText = Instantiate(secondHintText, transform);
            thirdHintText.name = "AdditionalCountText";
            thirdChip = Instantiate(secondChip, transform);
            thirdChip.name = "AdditionalCountChip";
            thirdChip.transform.SetAsFirstSibling();
            thirdHintText.rectTransform.anchoredPosition += new Vector2(0, -SecondRowOffset);
            thirdChip.rectTransform.anchoredPosition += new Vector2(0, -SecondRowOffset);
        }
        if (thirdHintText != null) {
            thirdHintText.text = lines.Length > 2 ? lines[2] : "";
            thirdHintText.gameObject.SetActive(lines.Length > 2);
            thirdChip.gameObject.SetActive(lines.Length > 2);
            SetChipColor(thirdChip, countColor);
        }
        ((RectTransform)transform).SetSizeWithCurrentAnchors(
            RectTransform.Axis.Vertical, RowHeight * lines.Length + RowGap * (lines.Length - 1));
    }

#if UNITY_EDITOR
    private void ConfigureText() {
        if (tipsFanCountText == null) return;
        tipsFanCountText.enableAutoSizing = true;
        tipsFanCountText.fontSizeMax = 23f;
        tipsFanCountText.fontSizeMin = 12f;
        tipsFanCountText.enableWordWrapping = false;
        tipsFanCountText.overflowMode = TextOverflowModes.Truncate;
        tipsFanCountText.alignment = TextAlignmentOptions.Center;
    }
#endif


    private static void SetChipColor(TipsCornerFrame chip, Color color) {
        chip.fill = chip.color = color;
        chip.SetVerticesDirty();
    }

#if UNITY_EDITOR
    public void ApplyStyle() {
        ConfigureText();
        if (backgroundImage != null) backgroundImage.enabled = false;
        if (secondHintText == null) secondHintText = Instantiate(tipsFanCountText, transform);
        if (firstChip == null) firstChip = CreateChip("FanChip", 0);
        if (secondChip == null) secondChip = CreateChip("CountChip", SecondRowOffset);
        firstChip.corner = secondChip.corner = 0;
        firstChip.SetVerticesDirty();
        secondChip.SetVerticesDirty();
        PlaceRow(firstChip.rectTransform, 0);
        PlaceRow(secondChip.rectTransform, SecondRowOffset);
        if (firstChip.GetComponent<CanvasRenderer>() == null) firstChip.gameObject.AddComponent<CanvasRenderer>();
        if (secondChip.GetComponent<CanvasRenderer>() == null) secondChip.gameObject.AddComponent<CanvasRenderer>();
        ConfigureRow(tipsFanCountText, 0);
        ConfigureRow(secondHintText, SecondRowOffset);
        tipsFanCountText.transform.SetAsLastSibling();
        secondHintText.transform.SetAsLastSibling();
    }

    private TipsCornerFrame CreateChip(string label, float top) {
        var go = new GameObject(label, typeof(RectTransform), typeof(TipsCornerFrame));
        go.layer = gameObject.layer; go.transform.SetParent(transform, false);
        var chip = go.GetComponent<TipsCornerFrame>();
        chip.corner = 0; chip.border = 0;
        chip.fill = chip.color = new Color32(70,86,126,255); chip.raycastTarget = false;
        PlaceRow(go.GetComponent<RectTransform>(), top);
        return chip;
    }
    private static void PlaceRow(RectTransform rect, float top) {
        rect.anchorMin = new Vector2(0,1); rect.anchorMax = Vector2.one; rect.pivot = new Vector2(.5f,1);
        rect.anchoredPosition = new Vector2(0,-top); rect.sizeDelta = new Vector2(0,RowHeight);
    }
    private static void ConfigureRow(TMP_Text text, float top) {
        PlaceRow(text.rectTransform, top);
        text.color = new Color32(240,240,240,255); text.raycastTarget = false;
        text.enableAutoSizing = true; text.fontSizeMin = 12; text.fontSizeMax = 23;
        text.textWrappingMode = TextWrappingModes.NoWrap; text.alignment = TextAlignmentOptions.Center;
        text.lineSpacing = 0; text.margin = new Vector4(4,0,4,0);
    }
#endif
}
