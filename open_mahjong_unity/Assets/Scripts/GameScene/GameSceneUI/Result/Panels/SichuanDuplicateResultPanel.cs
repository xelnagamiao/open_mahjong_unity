using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Board IMP comparison and the private asynchronous match-wait screen.</summary>
public sealed class SichuanDuplicateResultPanel : MonoBehaviour {
    private TMP_Text title, subtitle, waiting, footer, buttonText;
    private TMP_Text[,] values;
    private GameObject table;
    private Button readyButton;
    private bool comparison, canReady, submitted, readyEnabled;
    private bool comparisonPlaying;
    private string comparisonMatch;
    private int comparisonBoard;
    private double? comparisonDeadline;
    public int? ComparisonRemainingSeconds => comparisonPlaying && comparisonDeadline.HasValue
        ? (int)System.Math.Ceiling(System.Math.Max(0, comparisonDeadline.Value - SettlementSync.Now())) : null;

    public static DuplicateBoardResult Comparison(DuplicateMatchInfo info) =>
        info?.sync_mode == true && info.current_board > 0
            ? info.boards?.FirstOrDefault(board => board.number == info.current_board) : null;

    public static bool ShouldShow(DuplicateMatchInfo info) => info != null
        && info.status != "interrupted" && (info.waiting || Comparison(info) != null);

    public void Show(DuplicateMatchInfo info, double? receivedAt = null) {
        gameObject.SetActive(true);
        var board = Comparison(info);
        comparison = board != null;
        comparisonPlaying = comparison && info.waiting_for == "comparison";
        if (comparisonPlaying && info.comparison_remaining_ms.HasValue) {
            double deadline = (receivedAt ?? SettlementSync.Now()) + System.Math.Max(0, info.comparison_remaining_ms.Value) / 1000.0;
            bool sameBoard = comparisonMatch == info.id && comparisonBoard == info.current_board;
            comparisonDeadline = sameBoard && comparisonDeadline.HasValue
                ? System.Math.Min(comparisonDeadline.Value, deadline) : deadline;
        } else comparisonDeadline = null;
        comparisonMatch = info.id; comparisonBoard = info.current_board;
        canReady = comparison && !comparisonPlaying && !info.waiting && info.status != "completed";
        readyEnabled = submitted = false;
        table.SetActive(comparison);
        waiting.gameObject.SetActive(!comparison);
        readyButton.gameObject.SetActive(canReady || comparisonPlaying);
        readyButton.interactable = false;
        if (!comparison) {
            bool wholeMatch = info.waiting_for == "match" || info.sync_mode == false;
            title.text = wholeMatch ? "本桌已完成" : $"第 {info.current_board} 副已结束";
            subtitle.text = wholeMatch ? "非同步模式" : "同步模式";
            waiting.text = wholeMatch ? "等待另一桌结束\n<size=24>两桌全部结束后公布最终成绩</size>"
                                     : "等待另一桌完成本副\n<size=24>完成后展示本副 IMP 对比</size>";
            footer.text = "无需退出，结果将自动更新";
            return;
        }
        title.text = $"第 {board.number} 副 · IMP 对比";
        subtitle.text = "同步模式";
        for (int team = 0; team < 2; team++) {
            values[0, team].text = TableScore(board, "open", team);
            values[1, team].text = TableScore(board, "closed", team);
            values[2, team].text = Score(board.raw_difference, team, true);
            values[3, team].text = Score(board.imp_scores ?? (info.scoring == "imp" ? board.scores : null), team, false);
            values[4, team].text = Score(info.imp_scores ?? (info.scoring == "imp" ? info.scores : null), team, false);
        }
        footer.text = info.status == "completed" ? "两桌均已结束，即将显示最终结果"
            : comparisonPlaying ? "两桌完成本副对比后共同进入下一副"
            : info.waiting_for == "ready" ? "本桌对比已完成，等待另一桌" : "两桌确认后共同进入下一副";
        if (info.scoring == "raw_difference") footer.text += " · 血流按副分判胜负";
        buttonText.text = "等待确认";
        Update();
    }

    public void EnableReady() {
        if (!gameObject.activeInHierarchy || !canReady) return;
        readyEnabled = true;
        Update();
    }

    private void OnDisable() {
        comparisonDeadline = null;
        comparisonPlaying = false;
    }

    private void Update() {
        if (comparisonPlaying) {
            readyButton.interactable = false;
            int? remaining = ComparisonRemainingSeconds;
            buttonText.text = remaining > 0 ? $"对比展示（{remaining}）" : "等待同步";
            return;
        }
        if (!comparison || !canReady || !readyEnabled) return;
        bool confirmed = submitted || SettlementSync.SelfReady;
        readyButton.interactable = !confirmed;
        buttonText.text = confirmed ? "已准备" : SettlementSync.RemainingSeconds is int seconds
            ? $"继续（{seconds}）" : "继续";
    }

    private void Confirm() {
        if (!canReady || !readyEnabled || submitted) return;
        submitted = true;
        readyButton.interactable = false;
        GameStateNetworkManager.Instance.SendAction("ready", 0);
    }

    private static string TableScore(DuplicateBoardResult board, string room, int team) =>
        board.tables != null && board.tables.TryGetValue(room, out DuplicateTableResult result)
            ? Score(result.delta, team, true) : "—";
    private static string Score(int[] scores, int team, bool signed) => scores?.Length == 2
        ? (signed && scores[team] > 0 ? "+" : "") + scores[team] : "—";

    public void Build(TMP_Text template) {
        var root = (RectTransform)transform;
        root.anchorMin = root.anchorMax = root.pivot = new Vector2(.5f, .5f);
        root.anchoredPosition = Vector2.zero;
        root.sizeDelta = new Vector2(900, 510);
        var background = gameObject.AddComponent<Image>();
        background.color = new Color32(22, 31, 48, 248);
        background.raycastTarget = true;
        title = Label(transform, "Title", template, 34, Color.white, 30, 438, 840, 46);
        subtitle = Label(transform, "Mode", template, 22, new Color32(192, 205, 222, 255), 30, 401, 840, 32);
        waiting = Label(transform, "Waiting", template, 34, Color.white, 40, 160, 820, 180);
        waiting.textWrappingMode = TextWrappingModes.Normal;
        footer = Label(transform, "Status", template, 21, new Color32(208, 216, 230, 255), 20, 55, 860, 36);
        table = new GameObject("TeamComparison", typeof(RectTransform));
        table.transform.SetParent(transform, false);
        var tableRect = (RectTransform)table.transform;
        tableRect.anchorMin = Vector2.zero; tableRect.anchorMax = Vector2.one;
        tableRect.offsetMin = tableRect.offsetMax = Vector2.zero;
        var blue = new Color32(117, 190, 255, 255);
        var red = new Color32(255, 130, 140, 255);
        Label(table.transform, "Blue", template, 28, blue, 350, 347, 220, 42).text = "蓝队";
        Label(table.transform, "Red", template, 28, red, 620, 347, 220, 42).text = "红队";
        string[] names = { "开室队分", "闭室队分", "两室分差", "本副 IMP", "累计 IMP" };
        values = new TMP_Text[5, 2];
        for (int row = 0; row < names.Length; row++) {
            float y = 300 - 44 * row;
            var label = Label(table.transform, "Label" + row, template, 25, Color.white, 60, y, 260, 40);
            label.alignment = TextAlignmentOptions.MidlineLeft;
            label.text = names[row];
            values[row, 0] = Label(table.transform, "Blue" + row, template, 29, blue, 350, y, 220, 40);
            values[row, 1] = Label(table.transform, "Red" + row, template, 29, red, 620, y, 220, 40);
        }
        var button = new GameObject("Continue", typeof(RectTransform), typeof(Image), typeof(Button));
        button.transform.SetParent(transform, false);
        Place((RectTransform)button.transform, 340, 7, 220, 42);
        button.GetComponent<Image>().color = new Color32(30, 105, 190, 255);
        readyButton = button.GetComponent<Button>();
        readyButton.onClick.AddListener(Confirm);
        buttonText = Label(button.transform, "Text", template, 24, Color.white, 0, 0, 220, 42);
    }

    private static TMP_Text Label(Transform parent, string name, TMP_Text template, float size, Color color,
        float x, float y, float width, float height) {
        var host = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
        host.transform.SetParent(parent, false);
        var text = host.GetComponent<TextMeshProUGUI>();
        text.font = template.font; text.fontSharedMaterial = template.fontSharedMaterial;
        text.color = color; text.fontSize = text.fontSizeMax = size; text.fontSizeMin = size - 5;
        text.enableAutoSizing = true; text.textWrappingMode = TextWrappingModes.NoWrap;
        text.alignment = TextAlignmentOptions.Center; text.raycastTarget = false;
        Place(text.rectTransform, x, y, width, height);
        return text;
    }

    private static void Place(RectTransform rect, float x, float y, float width, float height) {
        rect.anchorMin = rect.anchorMax = rect.pivot = Vector2.zero;
        rect.anchoredPosition = new Vector2(x, y); rect.sizeDelta = new Vector2(width, height);
    }
}
