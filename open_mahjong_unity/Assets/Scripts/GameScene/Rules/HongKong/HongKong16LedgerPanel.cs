using System;
using System.Linq;
using TMPro;
using UnityEngine;

public sealed class HongKong16LedgerPanel : MonoBehaviour {
    [Serializable]
    private sealed class DebtRow {
        public GameObject root;
        public TMP_Text debtor, debtorSeat, creditor, creditorSeat, points, mouths;
        public UnityEngine.UI.Image background;
    }

    [SerializeField] private UnityEngine.UI.Button openButton, closeButton, backdropButton, cutButton;
    [SerializeField] private TMP_Text entryCount, summary, footer, cutCaption;
    [SerializeField] private GameObject cutNotice, dialog, empty;
    [SerializeField] private RectTransform card;
    [SerializeField] private UnityEngine.UI.ScrollRect scroll;
    [SerializeField] private DebtRow[] rows;
    [SerializeField] private Color rowColor, cutRowColor;

    private static HongKong16LedgerPanel instance;
    private static readonly string[] Seats = { "东起", "南起", "西起", "北起" };
    private HongKongInfo info;
    private Action cutAction;

    private void Awake() {
        instance = this;
        openButton.onClick.AddListener(Open);
        closeButton.onClick.AddListener(Close);
        backdropButton.onClick.AddListener(Close);
        cutButton.onClick.AddListener(Cut);
        Hide();
    }

    private void OnDisable() { if (instance == this) Hide(); }
    private void OnDestroy() { if (instance == this) instance = null; }
    private void OnRectTransformDimensionsChange() { FitCard(); }

    internal static void Show(string subRule, HongKongInfo value, string[] names,
        int selfOriginalIndex = -1, Action cut = null) {
        if (instance == null) return;
        if (subRule != HongKongGameState.New16 || value?.base_hand_tiles != 16) { Hide(); return; }
        if (instance.info?.phase == "waiting_ready" && value.phase != "waiting_ready") Close();
        instance.info = value;
        instance.cutAction = value.phase == "waiting_ready" ? cut : null;
        instance.Refresh(names, selfOriginalIndex);
        instance.openButton.gameObject.SetActive(true);
    }

    internal static void Hide() {
        if (instance == null) return;
        Close();
        instance.openButton.gameObject.SetActive(false);
        instance.cutNotice.SetActive(false);
        instance.cutButton.gameObject.SetActive(false);
        instance.info = null;
        instance.cutAction = null;
    }

    internal static void Close() {
        if (instance != null) instance.dialog.SetActive(false);
    }

    private void Open() {
        if (info == null) return;
        dialog.SetActive(true);
        Canvas.ForceUpdateCanvases();
        scroll.verticalNormalizedPosition = 1;
        FitCard();
    }

    private void Cut() {
        var action = cutAction;
        if (action == null) return;
        cutAction = null;
        cutButton.interactable = false;
        Close();
        action();
    }

    private void Refresh(string[] names, int selfOriginalIndex) {
        var debts = (info.pulls ?? Array.Empty<HongKongPull>())
            .Where(d => d != null && d.debtor >= 0 && d.debtor < 4 && d.creditor >= 0 && d.creditor < 4)
            .OrderBy(d => d.debtor).ThenBy(d => d.creditor).ToArray();
        long cutPoints = 0;
        int cutCount = 0;
        for (int i = 0; i < rows.Length; i++) {
            var row = rows[i];
            bool visible = i < debts.Length;
            row.root.SetActive(visible);
            if (!visible) continue;
            var debt = debts[i];
            bool eligible = cutAction != null && debt.debtor == selfOriginalIndex && debt.mouths >= 3;
            if (eligible) { cutPoints += debt.points; cutCount++; }
            row.debtor.text = PlayerName(names, debt.debtor);
            row.debtorSeat.text = Seats[debt.debtor];
            row.creditor.text = PlayerName(names, debt.creditor);
            row.creditorSeat.text = Seats[debt.creditor];
            row.points.text = debt.points.ToString("N0");
            row.mouths.text = $"{debt.mouths}口";
            row.background.color = eligible ? cutRowColor : rowColor;
        }
        bool canCut = cutAction != null && cutCount > 0;
        if (!canCut) cutAction = null;
        entryCount.text = debts.Length == 0 ? "暂无欠分" : $"{debts.Length}笔待结算";
        entryCount.gameObject.SetActive(!canCut);
        summary.text = debts.Length == 0 ? "当前没有未结清的拉账" : $"{debts.Length}笔欠分 · 尚未计入总分";
        empty.SetActive(debts.Length == 0);
        scroll.gameObject.SetActive(debts.Length > 0);
        cutNotice.SetActive(canCut);
        cutButton.gameObject.SetActive(canCut);
        cutButton.interactable = canCut;
        cutCaption.text = $"斩拉 · {cutPoints:N0}分";
        footer.text = canCut ? $"结清标出的{cutCount}笔三口及以上欠分" : "东起等标记表示开局座位";
        card.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, 224 + Math.Max(2, Math.Min(6, debts.Length)) * 58);
        FitCard();
    }

    private static string PlayerName(string[] names, int originalIndex) =>
        names != null && originalIndex < names.Length && !string.IsNullOrEmpty(names[originalIndex])
            ? names[originalIndex] : Seats[originalIndex];

    private void FitCard() {
        if (card == null) return;
        var bounds = ((RectTransform)transform).rect;
        if (bounds.width <= 0 || bounds.height <= 0) return;
        float scale = Mathf.Min(1, (bounds.width - 32) / card.rect.width, (bounds.height - 64) / card.rect.height);
        card.localScale = Vector3.one * Mathf.Max(.1f, scale);
    }
}
