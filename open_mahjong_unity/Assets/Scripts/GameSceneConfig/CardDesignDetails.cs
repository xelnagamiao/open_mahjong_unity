using UnityEngine;
using UnityEngine.UI;

/// <summary>聚合布局中的说明、跳转与只读手牌预览；设置仍由原组件保存。</summary>
public sealed class CardDesignDetails : MonoBehaviour
{
    [SerializeField] CardDesignTabs tabs;
    [SerializeField] GameObject faceHelp, imageHelp;
    [SerializeField] Button openFaceHelp, closeFaceHelp, openImageHelp, closeImageHelp;
    [SerializeField] Button showTableFaces;
    [SerializeField] CardFacePreviewSlot[] handSlots;
    [SerializeField] Button[] editFaceButtons;
    float nextRefresh;

    void Awake()
    {
        openFaceHelp.onClick.AddListener(() => faceHelp.SetActive(true));
        closeFaceHelp.onClick.AddListener(() => faceHelp.SetActive(false));
        openImageHelp.onClick.AddListener(() => imageHelp.SetActive(true));
        closeImageHelp.onClick.AddListener(() => imageHelp.SetActive(false));
        for (int i = 0; i < editFaceButtons.Length; i++) {
            int index = i;
            editFaceButtons[i].onClick.AddListener(() => {
                if (index == 0) { tabs.ShowFace(0); showTableFaces.onClick.Invoke(); }
                else tabs.ShowTableBackground();
            });
        }
        tabs.PageChanged += CloseHelp;
    }

    void OnDestroy() { if (tabs != null) tabs.PageChanged -= CloseHelp; }
    void OnDisable() { CloseHelp(); }
    void CloseHelp() { faceHelp.SetActive(false); imageHelp.SetActive(false); }
    void LateUpdate()
    {
        if (Time.unscaledTime < nextRefresh) return;
        nextRefresh = Time.unscaledTime + .15f;
        if (handSlots.Length > 0 && handSlots[0].gameObject.activeInHierarchy)
            RefreshPreviews();
    }
    public void RefreshPreviews()
    {
        foreach (var slot in handSlots) {
            var bg = TileFaceResolver.ShouldLayerHandFace(slot.tileId) ? TileFaceResolver.LoadHandBackground() : null;
            slot.Apply(TileFaceResolver.PreviewHand(slot.tileId), bg, false);
        }
    }
}
