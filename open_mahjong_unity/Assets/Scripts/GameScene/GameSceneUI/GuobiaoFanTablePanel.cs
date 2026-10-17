using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>按规则和分类切换，复用场景模板；规则书入口直接显示当前规则番表。</summary>
public sealed class GuobiaoFanTablePanel : MonoBehaviour {
    private static readonly Color HeaderColor = new Color(.17f, .24f, .38f);
    [SerializeField] private PanelPopupTransition popup;
    [SerializeField] private Button closeButton;
    [SerializeField] private Button backdropButton;
    [SerializeField] private Button returnButton;
    [SerializeField] private Button[] fanButtons;
    [SerializeField] private ScrollRect scroll;
    [SerializeField] private GuobiaoFanRow rowTemplate;
    [SerializeField] private TMP_Dropdown ruleDropdown;
    [SerializeField] private Button fullRulebookButton;
    [SerializeField] private GridLayoutGroup tabsLayout;
    [SerializeField] private RulebookPanelController rulebookController;
    [SerializeField] private Button[] pageButtons;
    [SerializeField] private RectTransform pageNavigation;
    [SerializeField] private MahjongRuleGuideRow guideTemplate;
    private readonly List<GuobiaoFanRow> rows = new List<GuobiaoFanRow>();
    private readonly List<MahjongRuleGuideRow> guideRows = new List<MahjongRuleGuideRow>();
    private readonly float[] pageScroll = { 1f, 1f, 1f };
    private int fanRowCount;
    private bool initialized;
    private MahjongFanCatalog.Document catalog;
    private string entryRuleId;
    private string entryRulebookPath;
    private bool dropdownWasExpanded;
    public string CurrentRuleId => catalog?.id;
    public string CurrentCategory { get; private set; }
    public string CurrentRulebookPath { get; private set; }
    public int CurrentFan { get; private set; }
    public int VisibleCount { get; private set; }
    public int CurrentPage { get; private set; }
    public bool IsOpen => gameObject.activeSelf;

    private void Awake() => Initialize();

    private void Initialize() {
        if (initialized) return;
        initialized = true;
        rowTemplate.gameObject.SetActive(false);
        guideTemplate.gameObject.SetActive(false);
        closeButton.onClick.AddListener(Close);
        backdropButton.onClick.AddListener(Close);
        fullRulebookButton.onClick.AddListener(OpenFullRulebook);
        ruleDropdown.ClearOptions();
        ruleDropdown.AddOptions(new List<string>(MahjongFanCatalog.RuleNames));
        ruleDropdown.onValueChanged.AddListener(index => SelectRule(MahjongFanCatalog.RuleIds[index]));
        for (int i = 0; i < fanButtons.Length; i++) {
            int index = i;
            fanButtons[i].onClick.AddListener(() => SelectCategory(index));
        }
        for (int i = 0; i < pageButtons.Length; i++) {
            int index = i;
            pageButtons[i].onClick.AddListener(() => SelectPage(index));
        }
    }

    public void Open(string path) => Open("guobiao", path);

    public void Open(string ruleId, string path) {
        Initialize();
        entryRuleId = ruleId;
        entryRulebookPath = path;
        transform.SetAsLastSibling();
        popup.Show();
        SelectRule(ruleId);
        if (EventSystem.current) EventSystem.current.SetSelectedGameObject(pageButtons[0].gameObject);
    }

    public void SelectRule(string ruleId) {
        Initialize();
        catalog = MahjongFanCatalog.Get(ruleId);
        CurrentRulebookPath = ruleId == entryRuleId && !string.IsNullOrEmpty(entryRulebookPath)
            ? entryRulebookPath : catalog.rulebookPath;
        ruleDropdown.SetValueWithoutNotify(Array.IndexOf(MahjongFanCatalog.RuleIds, ruleId));
        ruleDropdown.RefreshShownValue();
        ruleDropdown.image.color = HeaderColor;
        CurrentPage = 0;
        for (int i = 0; i < pageScroll.Length; i++) pageScroll[i] = 1f;
        int pageCount = ruleId == "guobiao" ? pageButtons.Length : 1;
        for (int i = 0; i < pageButtons.Length; i++) {
            pageButtons[i].gameObject.SetActive(i < pageCount);
            if (i >= pageCount) continue;
            pageButtons[i].navigation = new Navigation {
                mode = Navigation.Mode.Explicit,
                selectOnUp = i == 0 ? (Selectable)ruleDropdown : pageButtons[i - 1],
                selectOnDown = pageButtons[(i + 1) % pageCount], selectOnRight = fullRulebookButton
            };
        }
        int count = catalog.categories.Length;
        for (int i = 0; i < fanButtons.Length; i++) {
            fanButtons[i].gameObject.SetActive(i < count);
            if (i >= count) continue;
            fanButtons[i].GetComponentInChildren<TMP_Text>().text = catalog.categories[i].label;
            fanButtons[i].navigation = new Navigation {
                mode = Navigation.Mode.Explicit,
                selectOnLeft = fanButtons[(i + count - 1) % count],
                selectOnRight = fanButtons[(i + 1) % count],
                selectOnUp = ruleDropdown, selectOnDown = closeButton
            };
        }
        RefreshLayout();
        SelectCategory(0);
    }

    public void SelectFan(int fan) {
        Initialize();
        if (catalog == null) SelectRule("guobiao");
        int index = Array.FindIndex(catalog.categories, x => x.key == fan.ToString());
        if (index < 0) throw new ArgumentOutOfRangeException(nameof(fan));
        SelectCategory(index);
    }

    public void SelectCategory(int index) {
        if (catalog == null || index < 0 || index >= catalog.categories.Length) throw new ArgumentOutOfRangeException(nameof(index));
        if (CurrentPage != 0) pageScroll[CurrentPage] = scroll.verticalNormalizedPosition;
        CurrentPage = 0;
        CurrentCategory = catalog.categories[index].key;
        CurrentFan = int.TryParse(CurrentCategory, out var fan) ? fan : 0;
        int visible = 0;
        foreach (var entry in catalog.entries) {
            if (entry.CategoryKey != CurrentCategory) continue;
            if (visible == rows.Count) rows.Add(Instantiate(rowTemplate, scroll.content));
            var row = rows[visible++];
            row.gameObject.SetActive(true);
            row.Bind(entry, HeaderColor);
        }
        for (int i = visible; i < rows.Count; i++) rows[i].gameObject.SetActive(false);
        fanRowCount = VisibleCount = visible;
        foreach (var row in guideRows) row.gameObject.SetActive(false);
        RefreshPageNavigation();
        for (int i = 0; i < fanButtons.Length; i++)
            fanButtons[i].image.color = i == index ? new Color(.72f, .51f, .25f) : new Color(.14f, .21f, .33f);
        scroll.StopMovement();
        Canvas.ForceUpdateCanvases();
        LayoutRebuilder.ForceRebuildLayoutImmediate(scroll.content);
        scroll.verticalNormalizedPosition = 1f;
        pageScroll[0] = 1f;
    }

    public void SelectPage(int index) {
        if (catalog == null || index < 0 || index >= pageButtons.Length || (index > 0 && CurrentRuleId != "guobiao"))
            throw new ArgumentOutOfRangeException(nameof(index));
        if (CurrentPage == index) return;
        pageScroll[CurrentPage] = scroll.verticalNormalizedPosition;
        CurrentPage = index;
        for (int i = 0; i < rows.Count; i++) rows[i].gameObject.SetActive(index == 0 && i < fanRowCount);
        int visible = 0;
        if (index > 0) {
            foreach (var section in MahjongRuleGuide.Guobiao.pages[index - 1].sections) {
                if (visible == guideRows.Count) guideRows.Add(Instantiate(guideTemplate, scroll.content));
                var row = guideRows[visible++];
                row.gameObject.SetActive(true);
                row.Bind(section, HeaderColor);
            }
        }
        for (int i = visible; i < guideRows.Count; i++) guideRows[i].gameObject.SetActive(false);
        VisibleCount = index == 0 ? fanRowCount : visible;
        RefreshPageNavigation();
        scroll.StopMovement();
        Canvas.ForceUpdateCanvases();
        LayoutRebuilder.ForceRebuildLayoutImmediate(scroll.content);
        scroll.verticalNormalizedPosition = pageScroll[index];
    }

    private void RefreshPageNavigation() {
        tabsLayout.gameObject.SetActive(CurrentPage == 0);
        for (int i = 0; i < pageButtons.Length; i++) {
            pageButtons[i].image.color = i == CurrentPage ? HeaderColor : new Color(.11f, .17f, .26f);
            pageButtons[i].GetComponentInChildren<TMP_Text>(true).color = i == CurrentPage
                ? new Color(.93f, .75f, .43f) : new Color(.77f, .82f, .89f);
            var navigation = pageButtons[i].navigation;
            navigation.selectOnRight = CurrentPage == 0 ? fanButtons[0] : fullRulebookButton;
            pageButtons[i].navigation = navigation;
        }
        RefreshLayout();
    }

    public void Close() {
        ruleDropdown.Hide();
        popup.Hide();
        if (returnButton && returnButton.gameObject.activeInHierarchy && EventSystem.current)
            EventSystem.current.SetSelectedGameObject(returnButton.gameObject);
    }

    public void HideImmediate() {
        if (ruleDropdown && ruleDropdown.gameObject.activeInHierarchy) ruleDropdown.Hide();
        popup.HideImmediate();
    }

    private void OpenFullRulebook() => rulebookController.OpenManualConfirmation(catalog.displayName, CurrentRulebookPath);

    public void Resume() {
        transform.SetAsLastSibling();
        popup.Show();
        RefreshLayout();
        if (EventSystem.current) EventSystem.current.SetSelectedGameObject(fullRulebookButton.gameObject);
    }

    private void Update() {
        bool expanded = ruleDropdown.IsExpanded;
        if (Input.GetKeyDown(KeyCode.Escape) && !expanded && !dropdownWasExpanded) Close();
        dropdownWasExpanded = expanded;
    }

    private void OnRectTransformDimensionsChange() => RefreshLayout();

    private void RefreshLayout() {
        if (!tabsLayout || !scroll || !fullRulebookButton || !pageNavigation) return;
        var tabs = (RectTransform)tabsLayout.transform;
        float frameWidth = ((RectTransform)tabs.parent).rect.width;
        float sidebarWidth = frameWidth < 800 ? 108 : 132;
        pageNavigation.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, sidebarWidth);
        float contentLeft = 24 + sidebarWidth + 18;
        tabs.offsetMin = new Vector2(contentLeft, tabs.offsetMin.y);
        var area = (RectTransform)scroll.transform;
        area.offsetMin = new Vector2(contentLeft, area.offsetMin.y);
        float width = tabs.rect.width;
        if (width <= 0) return;
        int count = catalog?.categories.Length ?? fanButtons.Length;
        float minimumCellWidth = 84f;
        for (int i = 0; i < count; i++) {
            var label = fanButtons[i].GetComponentInChildren<TMP_Text>(true);
            minimumCellWidth = Mathf.Max(minimumCellWidth, label.GetPreferredValues().x + 20);
        }
        int columns = Mathf.Min(count, Mathf.Max(1, Mathf.FloorToInt((width + 6) / (minimumCellWidth + 6))));
        tabsLayout.constraintCount = columns;
        tabsLayout.cellSize = new Vector2((width - (columns - 1) * 6) / columns, 46);
        float height = Mathf.CeilToInt((float)count / columns) * 52 - 6;
        tabs.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, height);
        float tabsTop = frameWidth < 800 ? 148 : 92;
        tabs.anchoredPosition = new Vector2(tabs.anchoredPosition.x, -tabsTop);
        pageNavigation.anchoredPosition = new Vector2(24, -tabsTop);
        var manual = (RectTransform)fullRulebookButton.transform;
        manual.anchoredPosition = frameWidth < 800 ? new Vector2(24, -84) : new Vector2(420, -24);
        area.offsetMax = new Vector2(area.offsetMax.x, -(tabsTop + (CurrentPage == 0 ? height + 14 : 0)));
    }

    private void OnDisable() { if (scroll) scroll.StopMovement(); }
}
