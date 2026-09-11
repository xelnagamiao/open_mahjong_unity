using UnityEngine;
using UnityEngine.UI;

public class SceneConfigPanel : MonoBehaviour
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

    private void Awake() {
        BindNavigation();
        HideContentPanels();
        ShowTableClothPanel();
    }

    private void BindNavigation() {
        ShowTableClothPanelButton.onClick.AddListener(ShowTableClothPanel);
        ShowTableEdgePanelButton.onClick.AddListener(ShowTableEdgePanel);
        ShowCharacterPanelButton.onClick.AddListener(ShowCharacterPanel);
        ShowCardBackPanelButton.onClick.AddListener(ShowCardBackPanel);
        ShowCardEdgePanelButton.onClick.AddListener(ShowCardEdgePanel);
        ShowCardFacePanelButton.onClick.AddListener(ShowCardFacePanel);
        ShowCardFaceBgPanelButton.onClick.AddListener(ShowCardFaceBgPanel);
        if (ShowCenterDisplayPanelButton != null)
            ShowCenterDisplayPanelButton.onClick.AddListener(ShowCenterDisplayPanel);
        HideAllPanelButton.onClick.AddListener(HideAllPanel);
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
        nowPage = "TableCloth";
    }

    private void ShowTableEdgePanel() {
        HideContentPanels();
        SetActive(tableEdgePanel, true);
        tableEdgePanel.LoadTableEdges();
        nowPage = "TableEdge";
    }

    private void ShowCharacterPanel() {
        HideContentPanels();
        SetActive(characterPanel, true);
        nowPage = "Character";
    }

    public void ShowCardBackPanel() {
        HideContentPanels();
        if (cardDesignTabs != null) {
            cardDesignTabs.ShowCard(0);
            nowPage = "CardDesign";
            return;
        }
        cardBackPanel.ShowPanel();
        nowPage = "CardBack";
    }

    public void ShowCardEdgePanel() {
        HideContentPanels();
        if (cardDesignTabs != null) {
            cardDesignTabs.ShowCard(1);
            nowPage = "CardDesign";
            return;
        }
        SetActive(cardEdgePanel, true);
        nowPage = "CardEdge";
    }

    public void ShowCardFacePanel() {
        HideContentPanels();
        if (cardDesignTabs != null) {
            cardDesignTabs.ShowFace(0);
            nowPage = "FaceDesign";
            return;
        }
        cardFacePanel.ShowPanel();
        nowPage = "CardFace";
    }

    public void ShowCardFaceBgPanel() {
        HideContentPanels();
        if (cardDesignTabs != null) {
            cardDesignTabs.ShowFace(1);
            nowPage = "FaceDesign";
            return;
        }
        cardFaceBgPanel.ShowPanel();
        nowPage = "CardFaceBg";
    }

    public void ShowCenterDisplayPanel() {
        if (centerDisplayPanel == null) return;
        HideContentPanels();
        centerDisplayPanel.ShowPanel();
        nowPage = "CenterDisplay";
    }

    private void HideAllPanel() {
        HideContentPanels();
        nowPage = "Clear";
    }

    public void RefreshPage() {
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
