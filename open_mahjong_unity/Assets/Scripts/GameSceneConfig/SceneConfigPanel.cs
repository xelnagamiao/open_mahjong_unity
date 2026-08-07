using UnityEngine;
using UnityEngine.UI;

public class SceneConfigPanel : MonoBehaviour
{
    [SerializeField] private TableClothPanel tableClothPanel;
    [SerializeField] private TableEdgePanel tableEdgePanel;
    [SerializeField] private CharacterPanel characterPanel;
    private CardBackConfigPanel cardBackPanel;
    private CardEdgePanel cardEdgePanel;

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
        CardBackConfigPanel.AttachToScenePanel(transform);
        cardBackPanel = GetComponentInChildren<CardBackConfigPanel>(true);
        if (cardBackPanel != null) cardBackPanel.HidePanel();
        cardEdgePanel = GetComponentInChildren<CardEdgePanel>(true);
        if (cardEdgePanel != null) cardEdgePanel.gameObject.SetActive(false);
        HookCardBackButton();
        HookCardEdgeButton();
        RandomTableButton.HookExistingButton();
#if UNITY_EDITOR
        CardBackEditorDragReceiver.EnsureOnRoot(gameObject);
#endif
        ShowTableClothPanel();
    }

    /// <summary>把场景里手工画好的“牌边”导航按钮挂到面板切换逻辑上（切到边缘模式）。</summary>
    private void HookCardEdgeButton()
    {
        Button[] buttons = GetComponentsInChildren<Button>(true);
        foreach (Button button in buttons)
        {
            if (button == null || button.gameObject == null) continue;
            string name = button.gameObject.name;
            if (string.IsNullOrEmpty(name)) continue;
            if (!name.Contains("CardEdge") && !name.Contains("牌边")) continue;
            if (cardBackPanel != null && button.transform.IsChildOf(cardBackPanel.transform)) continue;
            button.onClick.RemoveListener(ShowCardEdgePanel);
            button.onClick.AddListener(ShowCardEdgePanel);
            return;
        }
    }

    /// <summary>把场景里手工画好的“牌背”导航按钮挂到面板切换逻辑上。</summary>
    private void HookCardBackButton()
    {
        Button[] buttons = GetComponentsInChildren<Button>(true);
        foreach (Button button in buttons)
        {
            if (button == null || button.gameObject == null) continue;
            string name = button.gameObject.name;
            if (string.IsNullOrEmpty(name)) continue;
            if (!name.Contains("CardBack") && !name.Contains("牌背")) continue;
            if (cardBackPanel != null && button.transform.IsChildOf(cardBackPanel.transform)) continue;
            button.onClick.RemoveListener(ShowCardBackPanel);
            button.onClick.AddListener(ShowCardBackPanel);
            return;
        }
    }

    private void ShowTableClothPanel() {

        tableClothPanel.gameObject.SetActive(true);
        tableEdgePanel.gameObject.SetActive(false);
        characterPanel.gameObject.SetActive(false);
        if (cardBackPanel != null) cardBackPanel.HidePanel();
        if (cardEdgePanel != null) cardEdgePanel.gameObject.SetActive(false);

        tableClothPanel.LoadTablecloths();
        nowPage = "TableCloth";
    }
    private void ShowTableEdgePanel() {
        tableEdgePanel.gameObject.SetActive(true);
        tableClothPanel.gameObject.SetActive(false);
        characterPanel.gameObject.SetActive(false);
        if (cardBackPanel != null) cardBackPanel.HidePanel();
        if (cardEdgePanel != null) cardEdgePanel.gameObject.SetActive(false);

        tableEdgePanel.LoadTableEdges();
        nowPage = "TableEdge";
    }
    private void ShowCharacterPanel() {
        characterPanel.gameObject.SetActive(true);
        tableClothPanel.gameObject.SetActive(false);
        tableEdgePanel.gameObject.SetActive(false);
        if (cardBackPanel != null) cardBackPanel.HidePanel();
        if (cardEdgePanel != null) cardEdgePanel.gameObject.SetActive(false);
        nowPage = "Character";
    }

    public void ShowCardBackPanel() {
        if (cardBackPanel == null) return;
        cardBackPanel.ShowPanel();
        if (cardEdgePanel != null) cardEdgePanel.gameObject.SetActive(false);
        tableClothPanel.gameObject.SetActive(false);
        tableEdgePanel.gameObject.SetActive(false);
        characterPanel.gameObject.SetActive(false);
        nowPage = "CardBack";
    }

    /// <summary>显示独立的牌边设置面板。</summary>
    public void ShowCardEdgePanel() {
        if (cardBackPanel != null) cardBackPanel.HidePanel();
        if (cardEdgePanel == null) return;
        cardEdgePanel.gameObject.SetActive(true);
        tableClothPanel.gameObject.SetActive(false);
        tableEdgePanel.gameObject.SetActive(false);
        characterPanel.gameObject.SetActive(false);
        nowPage = "CardEdge";
    }

    private void HideAllPanel() {
        tableClothPanel.gameObject.SetActive(false);
        tableEdgePanel.gameObject.SetActive(false);
        characterPanel.gameObject.SetActive(false);
        if (cardBackPanel != null) cardBackPanel.HidePanel();
        if (cardEdgePanel != null) cardEdgePanel.gameObject.SetActive(false);
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
        }else if (nowPage == "CardEdge"){
            if (cardEdgePanel != null) cardEdgePanel.gameObject.SetActive(true);
        }else if (nowPage == "Clear"){
            //
        }
    }
}
