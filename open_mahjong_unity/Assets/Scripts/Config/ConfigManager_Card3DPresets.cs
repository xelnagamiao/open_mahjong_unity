using System;
using UnityEngine;

public partial class ConfigManager
{
    public event Action Card3DAppearanceChanged;
    private const string BackLayerFractionKey = "CardBackLayerFraction";
    public float BackLayerFraction { get; private set; }
    private Card3DAppearance runtimeCardAppearance;
    private void NotifyCard3DAppearanceChanged() => Card3DAppearanceChanged?.Invoke();

    public Card3DAppearance CaptureCard3DAppearance()
    {
        var back = GetSelectedCardBackImage(); var bg = GetSelectedTableBackground();
        return new Card3DAppearance {
            back = CardBackColor, side = SideColor, backEdge = BackEdgeColor, frontEdge = FrontEdgeColor,
            face = TableFaceColor, backBrightness = CardBackBrightness, faceBrightness = TableFaceBrightness,
            backLayerFraction = BackLayerFraction,
            frontEdgeBrightness = FrontEdgeBrightness, backEdgeBrightness = BackEdgeBrightness,
            backEdgeMode = (int)BackEdgeMode, frontEdgeMode = (int)FrontEdgeMode,
            useBackground = UseTableFaceBackground, useSolid = TableFaceUseSolidColor,
            backImage = back.path, backImageCustom = back.isCustom,
            backgroundImage = bg.path, backgroundImageCustom = bg.isCustom
        };
    }

    // Commit the complete appearance once, so observers never save half-applied presets.
    public void ApplyCard3DAppearance(Card3DAppearance value, bool persist = true)
    {
        if (value == null) return;
        Card3DAppearance applied = value;
        if (value.faceToneOnly) {
            var current = CaptureCard3DAppearance();
            applied = value.Copy();
            // A face reference is deliberately independent from the 2D hand background
            // and from the live 3D back. Preserve every back-related setting.
            applied.back = current.back; applied.backEdge = current.backEdge;
            applied.backBrightness = current.backBrightness; applied.backEdgeBrightness = current.backEdgeBrightness;
            applied.backLayerFraction = current.backLayerFraction; applied.backEdgeMode = current.backEdgeMode;
            applied.backImage = current.backImage; applied.backImageCustom = current.backImageCustom;
            applied.backgroundImage = current.backgroundImage; applied.backgroundImageCustom = current.backgroundImageCustom;
            applied.faceToneOnly = false; applied.useSolid = true; applied.useBackground = false;
        }
        runtimeCardAppearance = persist ? null : applied.Copy();
        CardBackColor = applied.back; SideColor = applied.side; BackEdgeColor = applied.backEdge;
        FrontEdgeColor = applied.frontEdge; TableFaceColor = applied.face;
        BackLayerFraction = Mathf.Clamp01(applied.backLayerFraction);
        CardBackBrightness = Mathf.Clamp(applied.backBrightness, -1, 1);
        TableFaceBrightness = Mathf.Clamp(applied.faceBrightness, -1, 1);
        FrontEdgeBrightness = Mathf.Clamp(applied.frontEdgeBrightness, -1, 1);
        BackEdgeBrightness = Mathf.Clamp(applied.backEdgeBrightness, -1, 1);
        BackEdgeMode = (CardEdgePanel.BackEdgeMode)Mathf.Clamp(applied.backEdgeMode, 0, 2);
        FrontEdgeMode = (CardEdgePanel.FrontEdgeMode)Mathf.Clamp(applied.frontEdgeMode, 0, 2);
        BackEdgeSyncEnabled = BackEdgeMode == CardEdgePanel.BackEdgeMode.FollowBack;
        FrontEdgeSyncEnabled = FrontEdgeMode == CardEdgePanel.FrontEdgeMode.FollowTableBg;
        TableFaceUseSolidColor = applied.useSolid; UseTableFaceBackground = applied.useBackground && !applied.useSolid;
        if (!persist) return; // Runtime visuals must never overwrite the editor's saved appearance.
        PlayerPrefs.SetString(KEY_CARD_BACK_COLOR, ColorUtility.ToHtmlStringRGBA(CardBackColor));
        PlayerPrefs.SetString(KEY_SIDE_COLOR, ColorUtility.ToHtmlStringRGBA(SideColor));
        PlayerPrefs.SetInt(KEY_SIDE_LIGHTING_VERSION, 1);
        PlayerPrefs.SetString(KEY_BACK_EDGE_COLOR, ColorUtility.ToHtmlStringRGBA(BackEdgeColor));
        PlayerPrefs.SetString(KEY_FRONT_EDGE_COLOR, ColorUtility.ToHtmlStringRGBA(FrontEdgeColor));
        PlayerPrefs.SetString(KEY_TABLE_FACE_COLOR, ColorUtility.ToHtmlStringRGBA(TableFaceColor));
        PlayerPrefs.SetFloat(BackLayerFractionKey, BackLayerFraction);
        PlayerPrefs.SetFloat("CardBackBrightness", CardBackBrightness); PlayerPrefs.SetFloat("TableFaceBrightness", TableFaceBrightness);
        PlayerPrefs.SetFloat("FrontEdgeBrightness", FrontEdgeBrightness); PlayerPrefs.SetFloat("BackEdgeBrightness", BackEdgeBrightness);
        PlayerPrefs.SetInt(KEY_BACK_EDGE_MODE, (int)BackEdgeMode); PlayerPrefs.SetInt(KEY_FRONT_EDGE_MODE, (int)FrontEdgeMode);
        PlayerPrefs.SetInt(KEY_BACK_EDGE_SYNC, BackEdgeSyncEnabled ? 1 : 0); PlayerPrefs.SetInt(KEY_FRONT_EDGE_SYNC, FrontEdgeSyncEnabled ? 1 : 0);
        PlayerPrefs.SetInt(KEY_TABLE_FACE_USE_SOLID, TableFaceUseSolidColor ? 1 : 0);
        PlayerPrefs.SetInt(KEY_USE_TABLE_FACE_BACKGROUND, UseTableFaceBackground ? 1 : 0);
        PlayerPrefs.SetString(KEY_CARD_BACK_IMAGE_PATH, applied.backImage ?? ""); PlayerPrefs.SetInt(KEY_CARD_BACK_IMAGE_IS_CUSTOM, applied.backImageCustom ? 1 : 0);
        PlayerPrefs.SetString(KEY_TABLE_BG_PATH, applied.backgroundImage ?? ""); PlayerPrefs.SetInt(KEY_TABLE_BG_IS_CUSTOM, applied.backgroundImageCustom ? 1 : 0);
        PlayerPrefs.DeleteKey(KEY_FRONT_TEX_EXTEND_EDGE); PlayerPrefs.DeleteKey(KEY_FRONT_TEX_FOLLOW_TABLE_BG); PlayerPrefs.DeleteKey(KEY_FRONT_TEX_FOLLOW_TABLE_BG_TO_EDGE);
        PlayerPrefs.Save();
        NotifyCard3DAppearanceChanged();
    }
}
