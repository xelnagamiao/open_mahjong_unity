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

    [SerializeField] private Button ShowTableClothPanelButton;
    [SerializeField] private Button ShowTableEdgePanelButton;
    [SerializeField] private Button ShowCharacterPanelButton;
    [SerializeField] private Button ShowCardBackPanelButton;
    [SerializeField] private Button ShowCardEdgePanelButton;
    [SerializeField] private Button ShowCardFacePanelButton;
    [SerializeField] private Button ShowCardFaceBgPanelButton;
    [SerializeField] private Button HideAllPanelButton;

    private string nowPage = "";

    private void Awake() {
        ShowTableClothPanelButton.onClick.AddListener(ShowTableClothPanel);
        ShowTableEdgePanelButton.onClick.AddListener(ShowTableEdgePanel);
        ShowCharacterPanelButton.onClick.AddListener(ShowCharacterPanel);
        ShowCardBackPanelButton.onClick.AddListener(ShowCardBackPanel);
        ShowCardEdgePanelButton.onClick.AddListener(ShowCardEdgePanel);
        ShowCardFacePanelButton.onClick.AddListener(ShowCardFacePanel);
        ShowCardFaceBgPanelButton.onClick.AddListener(ShowCardFaceBgPanel);
        HideAllPanelButton.onClick.AddListener(HideAllPanel);
        HideContentPanels();
        ShowTableClothPanel();
    }

    private void HideContentPanels() {
        SetActive(tableClothPanel, false);
        SetActive(tableEdgePanel, false);
        SetActive(characterPanel, false);
        cardBackPanel.HidePanel();
        SetActive(cardEdgePanel, false);
        cardFacePanel.HidePanel();
        cardFaceBgPanel.HidePanel();
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
        cardBackPanel.ShowPanel();
        nowPage = "CardBack";
    }

    public void ShowCardEdgePanel() {
        HideContentPanels();
        SetActive(cardEdgePanel, true);
        nowPage = "CardEdge";
    }

    public void ShowCardFacePanel() {
        HideContentPanels();
        cardFacePanel.ShowPanel();
        nowPage = "CardFace";
    }

    public void ShowCardFaceBgPanel() {
        HideContentPanels();
        cardFaceBgPanel.ShowPanel();
        nowPage = "CardFaceBg";
    }

    private void HideAllPanel() {
        HideContentPanels();
        nowPage = "Clear";
    }

    public void RefreshPage() {
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
        }
    }
}
