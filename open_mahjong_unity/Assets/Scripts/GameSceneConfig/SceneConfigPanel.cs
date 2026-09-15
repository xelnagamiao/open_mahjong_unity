using TMPro;
using UnityEngine;
using UnityEngine.UI;

public partial class SceneConfigPanel : MonoBehaviour
{
    [SerializeField] private TableClothPanel tableClothPanel;
    [SerializeField] private TableEdgePanel tableEdgePanel;
    [SerializeField] private CharacterPanel characterPanel;
    [SerializeField] private CardBackConfigPanel cardBackPanel;
    [SerializeField] private CardEdgePanel cardEdgePanel;
    [SerializeField] private CardFaceConfigPanel cardFacePanel;
    [SerializeField] private CardFaceBackgroundPanel cardFaceBgPanel;
    [SerializeField] private CenterDisplayConfigPanel centerDisplayPanel;
    [SerializeField] private CardDesignTabs cardDesignTabs;

    [SerializeField] private Button ShowTableClothPanelButton;
    [SerializeField] private Button ShowTableEdgePanelButton;
    [SerializeField] private Button ShowCharacterPanelButton;
    [SerializeField] private Button ShowCardBackPanelButton;
    [SerializeField] private Button ShowCardEdgePanelButton;
    [SerializeField] private Button ShowCardFacePanelButton;
    [SerializeField] private Button ShowCardFaceBgPanelButton;
    [SerializeField] private Button ShowCenterDisplayPanelButton;
    [SerializeField] private Button HideAllPanelButton;

    private string nowPage = "";
    private bool panelVisible;
    private bool navigationBound;
    private void OnEnable() { Card3DPresetLibrary.Instance?.BeginEditing(); }

    private void Awake() {
        BindNavigation();
        HideContentPanels();
        EnsureResetControls();
        ShowTableClothPanel();
    }

    private void BindNavigation() {
        if (navigationBound) return;
        navigationBound = true;
        ShowTableClothPanelButton.onClick.AddListener(() => TogglePage("TableCloth", ShowTableClothPanel));
        ShowTableEdgePanelButton.onClick.AddListener(() => TogglePage("TableEdge", ShowTableEdgePanel));
        ShowCharacterPanelButton.onClick.AddListener(() => TogglePage("Character", ShowCharacterPanel));
        ShowCardBackPanelButton.onClick.AddListener(() => TogglePage(cardDesignTabs != null ? "CardDesign" : "CardBack", RestoreCardDesign));
        ShowCardEdgePanelButton.onClick.AddListener(() => TogglePage(cardDesignTabs != null ? "CardDesign" : "CardEdge", ShowCardEdgePanel));
        ShowCardFacePanelButton.onClick.AddListener(() => TogglePage(cardDesignTabs != null ? "FaceDesign" : "CardFace", RestoreFaceDesign));
        ShowCardFaceBgPanelButton.onClick.AddListener(() => TogglePage(cardDesignTabs != null ? "FaceDesign" : "CardFaceBg", ShowCardFaceBgPanel));
        if (ShowCenterDisplayPanelButton != null)
            ShowCenterDisplayPanelButton.onClick.AddListener(() => TogglePage("CenterDisplay", ShowCenterDisplayPanel));
        HideAllPanelButton.onClick.AddListener(TogglePanelVisibility);
        if (cardDesignTabs != null) cardDesignTabs.PanelShown += OnCardPanelShown;
        UpdateVisibilityLabel();
    }

    private void OnDestroy() {
        if (cardDesignTabs != null) cardDesignTabs.PanelShown -= OnCardPanelShown;
    }

    private void TogglePage(string page, System.Action show) {
        if (panelVisible && nowPage == page) HideAllPanel();
        else show();
    }

    private void SetCurrentPage(string page) {
        nowPage = page;
        panelVisible = true;
        UpdateVisibilityLabel();
    }

    private void UpdateVisibilityLabel() {
        string label = panelVisible ? "隐藏面板" : "显示面板";
        var text = HideAllPanelButton.GetComponentInChildren<TMP_Text>(true);
        if (text != null) text.text = label;
        else {
            var legacyText = HideAllPanelButton.GetComponentInChildren<Text>(true);
            if (legacyText != null) legacyText.text = label;
        }
    }

    private void OnCardPanelShown(bool isFaceDesign) {
        // Direct links inside the card panels also change the sidebar's restore target.
        SetActive(tableClothPanel, false);
        SetActive(tableEdgePanel, false);
        SetActive(characterPanel, false);
        if (centerDisplayPanel != null) centerDisplayPanel.HidePanel();
        SetCurrentPage(isFaceDesign ? "FaceDesign" : "CardDesign");
    }

    private void RestoreFaceDesign() {
        if (cardDesignTabs == null) { ShowCardFacePanel(); return; }
        HideContentPanels();
        cardDesignTabs.RestoreFace();
    }

    private void RestoreCardDesign() {
        if (cardDesignTabs == null) { ShowCardBackPanel(); return; }
        HideContentPanels();
        cardDesignTabs.ShowCard(cardDesignTabs.SelectedCardTab);
    }

    private void HideContentPanels() {
        if (cardDesignTabs != null) cardDesignTabs.HidePanels();
        SetActive(tableClothPanel, false);
        SetActive(tableEdgePanel, false);
        SetActive(characterPanel, false);
        cardBackPanel.HidePanel();
        SetActive(cardEdgePanel, false);
        cardFacePanel.HidePanel();
        cardFaceBgPanel.HidePanel();
        if (centerDisplayPanel != null) centerDisplayPanel.HidePanel();
    }

    private static void SetActive(Component panel, bool active) {
        panel.gameObject.SetActive(active);
    }

    private void ShowTableClothPanel() {
        HideContentPanels();
        SetActive(tableClothPanel, true);
        tableClothPanel.LoadTablecloths();
        SetCurrentPage("TableCloth");
    }

    private void ShowTableEdgePanel() {
        HideContentPanels();
        SetActive(tableEdgePanel, true);
        tableEdgePanel.LoadTableEdges();
        SetCurrentPage("TableEdge");
    }

    private void ShowCharacterPanel() {
        HideContentPanels();
        SetActive(characterPanel, true);
        SetCurrentPage("Character");
    }

    public void ShowCardBackPanel() {
        HideContentPanels();
        if (cardDesignTabs != null) {
            cardDesignTabs.ShowCard(0);
            SetCurrentPage("CardDesign");
            return;
        }
        cardBackPanel.ShowPanel();
        SetCurrentPage("CardBack");
    }

    public void ShowCardEdgePanel() {
        HideContentPanels();
        if (cardDesignTabs != null) {
            cardDesignTabs.ShowCard(1);
            SetCurrentPage("CardDesign");
            return;
        }
        SetActive(cardEdgePanel, true);
        SetCurrentPage("CardEdge");
    }

    public void ShowCardFacePanel() {
        HideContentPanels();
        if (cardDesignTabs != null) {
            cardDesignTabs.ShowFace(0);
            SetCurrentPage("FaceDesign");
            return;
        }
        cardFacePanel.ShowPanel();
        SetCurrentPage("CardFace");
    }

    public void ShowCardFaceBgPanel() {
        HideContentPanels();
        if (cardDesignTabs != null) {
            cardDesignTabs.ShowFace(1);
            SetCurrentPage("FaceDesign");
            return;
        }
        cardFaceBgPanel.ShowPanel();
        SetCurrentPage("CardFaceBg");
    }

    public void ShowCenterDisplayPanel() {
        if (centerDisplayPanel == null) return;
        HideContentPanels();
        centerDisplayPanel.ShowPanel();
        SetCurrentPage("CenterDisplay");
    }

    private void HideAllPanel() {
        HideContentPanels();
        panelVisible = false;
        UpdateVisibilityLabel();
    }

    private void TogglePanelVisibility() {
        if (panelVisible) { HideAllPanel(); return; }
        switch (nowPage) {
            case "TableEdge": ShowTableEdgePanel(); break;
            case "Character": ShowCharacterPanel(); break;
            case "CenterDisplay": ShowCenterDisplayPanel(); break;
            case "FaceDesign": RestoreFaceDesign(); break;
            case "CardDesign": RestoreCardDesign(); break;
            case "CardBack": ShowCardBackPanel(); break;
            case "CardEdge": ShowCardEdgePanel(); break;
            case "CardFace": ShowCardFacePanel(); break;
            case "CardFaceBg": ShowCardFaceBgPanel(); break;
            default: ShowTableClothPanel(); break;
        }
    }

    public void RefreshPage() {
        // Upload callbacks may complete after the user has hidden the panel.
        if (!panelVisible) return;
        if (cardDesignTabs != null && (nowPage == "FaceDesign" || nowPage == "CardDesign")) {
            cardDesignTabs.RefreshPanel();
            return;
        }
        if (nowPage == "TableCloth") {
            tableClothPanel.LoadTablecloths();
        } else if (nowPage == "TableEdge") {
            tableEdgePanel.LoadTableEdges();
        } else if (nowPage == "CardBack") {
            cardBackPanel.ShowPanel();
        } else if (nowPage == "CardEdge") {
            SetActive(cardEdgePanel, true);
        } else if (nowPage == "CardFace") {
            cardFacePanel.ShowPanel();
        } else if (nowPage == "CardFaceBg") {
            cardFaceBgPanel.ShowPanel();
        } else if (nowPage == "CenterDisplay" && centerDisplayPanel != null) {
            centerDisplayPanel.ShowPanel();
        }
    }
}
