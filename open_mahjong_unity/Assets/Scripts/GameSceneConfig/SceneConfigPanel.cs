using UnityEngine;
using UnityEngine.UI;

public class SceneConfigPanel : MonoBehaviour
{
    [SerializeField] private TableClothPanel tableClothPanel;
    [SerializeField] private TableEdgePanel tableEdgePanel;
    [SerializeField] private CharacterPanel characterPanel;
    private CardBackConfigPanel cardBackPanel;

    [SerializeField] private Button ShowTableClothPanelButton;
    [SerializeField] private Button ShowTableEdgePanelButton;
    [SerializeField] private Button ShowCharacterPanelButton;
    [SerializeField] private Button HideAllPanelButton;

    private string nowPage = "";

    private void Awake() {
        ShowTableClothPanelButton.onClick.AddListener(ShowTableClothPanel);
        ShowTableEdgePanelButton.onClick.AddListener(ShowTableEdgePanel);
        ShowCharacterPanelButton.onClick.AddListener(ShowCharacterPanel);
        HideAllPanelButton.onClick.AddListener(HideAllPanel);
        tableClothPanel.gameObject.SetActive(false);
        tableEdgePanel.gameObject.SetActive(false);
        characterPanel.gameObject.SetActive(false);
        CardBackConfigPanel.EnsureCreated(transform);
        cardBackPanel = GetComponentInChildren<CardBackConfigPanel>(true);
        if (cardBackPanel != null) cardBackPanel.HidePanel();
        RandomTableButton.HookExistingButton();
        ShowTableClothPanel();
    }

    private void ShowTableClothPanel() {

        tableClothPanel.gameObject.SetActive(true);
        tableEdgePanel.gameObject.SetActive(false);
        characterPanel.gameObject.SetActive(false);
        if (cardBackPanel != null) cardBackPanel.HidePanel();

        tableClothPanel.LoadTablecloths();
        nowPage = "TableCloth";
    }
    private void ShowTableEdgePanel() {
        tableEdgePanel.gameObject.SetActive(true);
        tableClothPanel.gameObject.SetActive(false);
        characterPanel.gameObject.SetActive(false);
        if (cardBackPanel != null) cardBackPanel.HidePanel();

        tableEdgePanel.LoadTableEdges();
        nowPage = "TableEdge";
    }
    private void ShowCharacterPanel() {
        characterPanel.gameObject.SetActive(true);
        tableClothPanel.gameObject.SetActive(false);
        tableEdgePanel.gameObject.SetActive(false);
        if (cardBackPanel != null) cardBackPanel.HidePanel();
        nowPage = "Character";
    }

    public void ShowCardBackPanel() {
        if (cardBackPanel == null) return;
        cardBackPanel.ShowPanel();
        tableClothPanel.gameObject.SetActive(false);
        tableEdgePanel.gameObject.SetActive(false);
        characterPanel.gameObject.SetActive(false);
        nowPage = "CardBack";
    }

    private void HideAllPanel() {
        tableClothPanel.gameObject.SetActive(false);
        tableEdgePanel.gameObject.SetActive(false);
        characterPanel.gameObject.SetActive(false);
        if (cardBackPanel != null) cardBackPanel.HidePanel();
        nowPage = "Clear";
    }

    public void RefreshPage(){
        if (nowPage == "TableCloth"){
            tableClothPanel.LoadTablecloths();
        }else if (nowPage == "TableEdge"){
            tableEdgePanel.LoadTableEdges();
        }else if (nowPage == "Character"){
            //
        }else if (nowPage == "CardBack"){
            if (cardBackPanel != null) cardBackPanel.ShowPanel();
        }else if (nowPage == "Clear"){
            //
        }
    }
}
