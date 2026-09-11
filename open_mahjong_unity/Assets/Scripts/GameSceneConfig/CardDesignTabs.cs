using UnityEngine;
using UnityEngine.UI;

/// <summary>只聚合原面板；样式、设置值和上传事件仍由原组件管理。</summary>
public sealed class CardDesignTabs : MonoBehaviour
{
    [SerializeField] private GameObject faceDesignRoot;
    [SerializeField] private GameObject cardDesignRoot;
    [SerializeField] private Button[] faceTabs;
    [SerializeField] private Button[] cardTabs;
    [SerializeField] private CardFaceConfigPanel facePanel;
    [SerializeField] private CardFaceBackgroundPanel backgroundPanel;
    [SerializeField] private CardBackConfigPanel backPanel;
    [SerializeField] private CardEdgePanel edgePanel;
    [SerializeField] private GameObject handImagesPage;
    [SerializeField] private GameObject tableFacePage;
    [SerializeField] private GameObject cardModelPreview;

    private int faceTab;
    private int cardTab;
    public event System.Action PageChanged;

    private void Awake()
    {
        for (int i = 0; i < faceTabs.Length; i++) {
            int tab = i;
            faceTabs[i].onClick.AddListener(() => ShowFace(tab));
        }
        for (int i = 0; i < cardTabs.Length; i++) {
            int tab = i;
            cardTabs[i].onClick.AddListener(() => ShowCard(tab));
        }
    }

    public void ShowFace(int tab)
    {
        // Keep existing direct callers of the former third face tab working.
        if (tab == 2) { ShowTableBackground(); return; }
        PageChanged?.Invoke();
        faceTab = Mathf.Clamp(tab, 0, faceTabs.Length - 1);
        cardDesignRoot.SetActive(false);
        facePanel.gameObject.SetActive(false);
        backgroundPanel.gameObject.SetActive(false);
        handImagesPage.SetActive(faceTab == 1);
        tableFacePage.SetActive(false);
        faceDesignRoot.SetActive(true);
        if (faceTab == 0) facePanel.ShowPanel();
        else { backgroundPanel.ShowPanel(); backgroundPanel.RefreshSolidColorUi(); }
        Select(faceTabs, faceTab);
    }

    public void ShowCard(int tab)
    {
        PageChanged?.Invoke();
        cardTab = Mathf.Clamp(tab, 0, cardTabs.Length - 1);
        faceDesignRoot.SetActive(false);
        backgroundPanel.HidePanel();
        handImagesPage.SetActive(false);
        backPanel.gameObject.SetActive(false);
        edgePanel.gameObject.SetActive(false);
        cardDesignRoot.SetActive(true);
        tableFacePage.SetActive(cardTab == 2);
        cardModelPreview.SetActive(cardTab != 2);
        if (cardTab == 0) backPanel.ShowPanel();
        else if (cardTab == 1) edgePanel.gameObject.SetActive(true);
        else { backgroundPanel.ShowPanel(); backgroundPanel.RefreshSolidColorUi(); }
        Select(cardTabs, cardTab);
    }

    public void ShowTableBackground() => ShowCard(2);

    public void HidePanels()
    {
        PageChanged?.Invoke();
        faceDesignRoot.SetActive(false);
        cardDesignRoot.SetActive(false);
        backgroundPanel.HidePanel();
        handImagesPage.SetActive(false);
        tableFacePage.SetActive(false);
    }

    public void RefreshPanel()
    {
        if (faceDesignRoot.activeSelf) ShowFace(faceTab);
        else if (cardDesignRoot.activeSelf) ShowCard(cardTab);
    }

    private static void Select(Button[] buttons, int index)
    {
        // 与原「标准麻将牌 / 虹雀麻将牌」标签完全相同的颜色和点击方式。
        for (int i = 0; i < buttons.Length; i++)
            SceneConfigUi.SetButtonSelected(buttons[i], i == index);
    }
}
