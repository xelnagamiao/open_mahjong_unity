using Mahjong.SceneSettingsUI;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

public partial class SceneConfigPanel
{
    private bool resetControlsReady;
    private MessagePrefab resetConfirmation;
    [SerializeField] private Button clothReset, frameReset, centerReset, cardsReset, allReset;
    [SerializeField] private Button newCloth, newFrame;
    [SerializeField] private Card3DPresetPanel cardPresets;
    [SerializeField] private int fixedUiVersion;
    public bool HasBakedFixedUi => fixedUiVersion == 1 && clothReset && frameReset && centerReset && cardsReset
        && allReset && newCloth && newFrame && cardPresets;

    private void EnsureResetControls()
    {
        if (resetControlsReady) return;
        if (!HasBakedFixedUi) { Debug.LogError("场景设置固定 UI 尚未烘焙", this); return; }
        resetControlsReady = true;
        clothReset.onClick.AddListener(RestoreTablecloth);
        frameReset.onClick.AddListener(RestoreTableFrame);
        centerReset.onClick.AddListener(RestoreCenterDisplay);
        cardsReset.onClick.AddListener(Restore3DCards);
        allReset.onClick.AddListener(ConfirmRestoreAllDefaults);
        newCloth.onClick.AddListener(tableClothPanel.BeginNewColor);
        newFrame.onClick.AddListener(tableEdgePanel.BeginNewColor);
        cardPresets.Initialize(this);
    }


    private void RestoreTablecloth()
    {
        if (ConfigManager.Instance == null) return;
        ConfigManager.Instance.RestoreDefaultTablecloth();
        Desktop.Instance?.RefreshAppearance();
        tableClothPanel.GetComponent<TableSeamSelector>()?.RefreshSelection();
        RefreshPage();
    }

    private void RestoreTableFrame()
    {
        if (ConfigManager.Instance == null) return;
        ConfigManager.Instance.RestoreDefaultTableFrame();
        Desktop.Instance?.RefreshAppearance();
        tableEdgePanel.GetComponent<TableFrameHeader>()?.RefreshSelection();
        tableClothPanel.GetComponent<TableSeamSelector>()?.RefreshSelection();
        RefreshPage();
    }

    private void RestoreCenterDisplay()
    {
        if (ConfigManager.Instance == null) return;
        ConfigManager.Instance.RestoreDefaultCenterDisplay();
        centerDisplayPanel.ShowPanel();
    }

    private void Restore3DCards()
    {
        if (ConfigManager.Instance == null) return;
        ConfigManager.Instance.RestoreDefault3DCards();
        RefreshDefaultCards(false);
        RefreshPage();
    }

    private void RefreshDefaultCards(bool includeHand)
    {
        CardBackManager.RefreshAfterDefaults(includeHand);
        if (cardBackPanel.isActiveAndEnabled) cardBackPanel.ReloadSaved();
        if (cardEdgePanel.isActiveAndEnabled) cardEdgePanel.ReloadSaved();
        if (cardFacePanel.isActiveAndEnabled) cardFacePanel.RefreshHighlights();
        if (cardFaceBgPanel.isActiveAndEnabled) cardFaceBgPanel.RefreshSolidColorUi();
    }

    private void ConfirmRestoreAllDefaults()
    {
        if (resetConfirmation != null || ConfigManager.Instance == null) return;
        if (NotificationManager.Instance == null) return;
        resetConfirmation = NotificationManager.Instance.ShowConfirmation("恢复全部场景设置",
            "确定将所有场景设置恢复为默认吗？\n桌布、边框、中心盘、牌面及3D卡牌外观将被重置。\n已上传的图片和牌面包会保留。",
            () => { if (this != null) RestoreAllDefaults(); }, "确定恢复", "取消");
    }

    private void RestoreAllDefaults()
    {
        if (ConfigManager.Instance == null) return;
        ConfigManager.Instance.RestoreAllSceneDefaults();
        TileFaceResolver.SelectPack(TilePackIds.PackOfficial);
        RefreshDefaultCards(true);
        Desktop.Instance?.RefreshAppearance();
        tableClothPanel.GetComponent<TableSeamSelector>()?.RefreshSelection();
        if (centerDisplayPanel != null) centerDisplayPanel.ReloadSaved();
        tableEdgePanel.GetComponent<TableFrameHeader>()?.RefreshSelection();
        RefreshPage();
        SceneConfigUi.ShowTip("场景设置已恢复默认");
    }
}
