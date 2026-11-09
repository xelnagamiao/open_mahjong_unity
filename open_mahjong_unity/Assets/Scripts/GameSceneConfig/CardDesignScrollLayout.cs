using UnityEngine;
using UnityEngine.UI;

/// <summary>卡牌设计共用一条设置滚动区，平面与立体预览同步使用现有配置。</summary>
public sealed class CardDesignScrollLayout : MonoBehaviour
{
    [SerializeField] ScrollRect settingsScroll;
    [SerializeField] CardFacePreviewSlot[] tablePreviews;
    [SerializeField] Button[] previewButtons;
    [SerializeField] Outline[] selectionOutlines;
    [SerializeField] CardDesignModelPreview model;
    float nextRefresh;
    int selectedTile = 11;

    void Awake()
    {
        for (int i = 0; i < previewButtons.Length; i++) {
            int tile = tablePreviews[i].tileId;
            previewButtons[i].onClick.AddListener(() => SelectTile(tile));
        }
    }

    public void Show()
    {
        RefreshPreviews();
        model.SelectTile(selectedTile);
    }

    void LateUpdate()
    {
        if (Time.unscaledTime < nextRefresh) return;
        nextRefresh = Time.unscaledTime + .15f;
        RefreshPreviews();
    }

    void SelectTile(int tile)
    {
        selectedTile = tile;
        model.SelectTile(tile);
        RefreshSelection();
    }

    void RefreshSelection()
    {
        for (int i = 0; i < selectionOutlines.Length; i++)
            selectionOutlines[i].enabled = tablePreviews[i].tileId == selectedTile;
    }

    void RefreshPreviews()
    {
        var config = ConfigManager.Instance;
        if (config == null) return;
        var background = config.UseTableFaceBackground && !config.TableFaceUseSolidColor
            ? TileFaceResolver.LoadTableBackground() : null;
        foreach (var slot in tablePreviews)
            slot.ApplyTable(TileFaceResolver.PreviewTable(slot.tileId), background,
                TileFaceResolver.TablePreviewBaseColor, false,
                TileFaceResolver.TableImageScaleFor(slot.tileId, applyWhiteDragonFaceSetting: false));
        RefreshSelection();
    }
}
