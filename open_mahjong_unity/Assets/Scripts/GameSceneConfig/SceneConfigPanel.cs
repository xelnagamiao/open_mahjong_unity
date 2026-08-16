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
        if (ShowCardFaceBgPanelButton != null) {
            ShowCardFaceBgPanelButton.onClick.AddListener(ShowCardFaceBgPanel);
        }
        HideAllPanelButton.onClick.AddListener(HideAllPanel);
        tableClothPanel.gameObject.SetActive(false);
        tableEdgePanel.gameObject.SetActive(false);
        characterPanel.gameObject.SetActive(false);
        if (cardBackPanel != null) cardBackPanel.HidePanel();
        if (cardEdgePanel != null) cardEdgePanel.gameObject.SetActive(false);
        if (cardFacePanel != null) cardFacePanel.HidePanel();
        if (cardFaceBgPanel != null) cardFaceBgPanel.HidePanel();
        RandomTableButton.HookExistingButton();
#if UNITY_EDITOR
        CardBackEditorDragReceiver.EnsureOnRoot(gameObject);
#endif
        ShowTableClothPanel();
    }

    private void HideContentPanels() {
        tableClothPanel.gameObject.SetActive(false);
        tableEdgePanel.gameObject.SetActive(false);
        characterPanel.gameObject.SetActive(false);
        if (cardBackPanel != null) cardBackPanel.HidePanel();
        if (cardEdgePanel != null) cardEdgePanel.gameObject.SetActive(false);
        if (cardFacePanel != null) cardFacePanel.HidePanel();
        if (cardFaceBgPanel != null) cardFaceBgPanel.HidePanel();
    }

    private void ShowTableClothPanel() {
        HideContentPanels();
        tableClothPanel.gameObject.SetActive(true);
        tableClothPanel.LoadTablecloths();
        nowPage = "TableCloth";
    }

    private void ShowTableEdgePanel() {
        HideContentPanels();
        tableEdgePanel.gameObject.SetActive(true);
        tableEdgePanel.LoadTableEdges();
        nowPage = "TableEdge";
    }

    private void ShowCharacterPanel() {
        HideContentPanels();
        characterPanel.gameObject.SetActive(true);
        nowPage = "Character";
    }

    public void ShowCardBackPanel() {
        if (cardBackPanel == null) return;
        HideContentPanels();
        cardBackPanel.ShowPanel();
        nowPage = "CardBack";
    }

    public void ShowCardEdgePanel() {
        if (cardEdgePanel == null) return;
        HideContentPanels();
        cardEdgePanel.gameObject.SetActive(true);
        nowPage = "CardEdge";
    }

    public void ShowCardFacePanel() {
        if (cardFacePanel == null) return;
        HideContentPanels();
        cardFacePanel.ShowPanel();
        nowPage = "CardFace";
    }

    public void ShowCardFaceBgPanel() {
        if (cardFaceBgPanel == null) return;
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
            if (cardBackPanel != null) cardBackPanel.ShowPanel();
        } else if (nowPage == "CardEdge") {
            if (cardEdgePanel != null) cardEdgePanel.gameObject.SetActive(true);
        } else if (nowPage == "CardFace") {
            if (cardFacePanel != null) cardFacePanel.ShowPanel();
        } else if (nowPage == "CardFaceBg") {
            if (cardFaceBgPanel != null) cardFaceBgPanel.ShowPanel();
        }
    }
}
