using System;
using UnityEngine;

public partial class ConfigManager
{
    public event Action Card3DAppearanceChanged;
    private Card3DAppearance runtimeCardAppearance;
    private void NotifyCard3DAppearanceChanged() => Card3DAppearanceChanged?.Invoke();

    public Card3DAppearance CaptureCard3DAppearance()
    {
        var back = GetSelectedCardBackImage(); var bg = GetSelectedTableBackground();
        return new Card3DAppearance {
            back = CardBackColor, side = SideColor, backEdge = BackEdgeColor, frontEdge = FrontEdgeColor,
            face = TableFaceColor, backBrightness = CardBackBrightness, faceBrightness = TableFaceBrightness,
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
        runtimeCardAppearance = persist ? null : value.Copy();
        CardBackColor = value.back; SideColor = value.side; BackEdgeColor = value.backEdge;
        FrontEdgeColor = value.frontEdge; TableFaceColor = value.face;
        CardBackBrightness = Mathf.Clamp(value.backBrightness, -1, 1);
        TableFaceBrightness = Mathf.Clamp(value.faceBrightness, -1, 1);
        FrontEdgeBrightness = Mathf.Clamp(value.frontEdgeBrightness, -1, 1);
        BackEdgeBrightness = Mathf.Clamp(value.backEdgeBrightness, -1, 1);
        BackEdgeMode = (CardEdgePanel.BackEdgeMode)Mathf.Clamp(value.backEdgeMode, 0, 2);
        FrontEdgeMode = (CardEdgePanel.FrontEdgeMode)Mathf.Clamp(value.frontEdgeMode, 0, 2);
        BackEdgeSyncEnabled = BackEdgeMode == CardEdgePanel.BackEdgeMode.FollowBack;
        FrontEdgeSyncEnabled = FrontEdgeMode == CardEdgePanel.FrontEdgeMode.FollowTableBg;
        TableFaceUseSolidColor = value.useSolid; UseTableFaceBackground = value.useBackground && !value.useSolid;
        if (!persist) return; // Runtime visuals must never overwrite the editor's saved appearance.
        PlayerPrefs.SetString(KEY_CARD_BACK_COLOR, ColorUtility.ToHtmlStringRGBA(CardBackColor));
        PlayerPrefs.SetString(KEY_SIDE_COLOR, ColorUtility.ToHtmlStringRGBA(SideColor));
        PlayerPrefs.SetInt(KEY_SIDE_LIGHTING_VERSION, 1);
        PlayerPrefs.SetString(KEY_BACK_EDGE_COLOR, ColorUtility.ToHtmlStringRGBA(BackEdgeColor));
        PlayerPrefs.SetString(KEY_FRONT_EDGE_COLOR, ColorUtility.ToHtmlStringRGBA(FrontEdgeColor));
        PlayerPrefs.SetString(KEY_TABLE_FACE_COLOR, ColorUtility.ToHtmlStringRGBA(TableFaceColor));
        PlayerPrefs.SetFloat("CardBackBrightness", CardBackBrightness); PlayerPrefs.SetFloat("TableFaceBrightness", TableFaceBrightness);
        PlayerPrefs.SetFloat("FrontEdgeBrightness", FrontEdgeBrightness); PlayerPrefs.SetFloat("BackEdgeBrightness", BackEdgeBrightness);
        PlayerPrefs.SetInt(KEY_BACK_EDGE_MODE, (int)BackEdgeMode); PlayerPrefs.SetInt(KEY_FRONT_EDGE_MODE, (int)FrontEdgeMode);
        PlayerPrefs.SetInt(KEY_BACK_EDGE_SYNC, BackEdgeSyncEnabled ? 1 : 0); PlayerPrefs.SetInt(KEY_FRONT_EDGE_SYNC, FrontEdgeSyncEnabled ? 1 : 0);
        PlayerPrefs.SetInt(KEY_TABLE_FACE_USE_SOLID, TableFaceUseSolidColor ? 1 : 0);
        PlayerPrefs.SetInt(KEY_USE_TABLE_FACE_BACKGROUND, UseTableFaceBackground ? 1 : 0);
        PlayerPrefs.SetString(KEY_CARD_BACK_IMAGE_PATH, value.backImage ?? ""); PlayerPrefs.SetInt(KEY_CARD_BACK_IMAGE_IS_CUSTOM, value.backImageCustom ? 1 : 0);
        PlayerPrefs.SetString(KEY_TABLE_BG_PATH, value.backgroundImage ?? ""); PlayerPrefs.SetInt(KEY_TABLE_BG_IS_CUSTOM, value.backgroundImageCustom ? 1 : 0);
        PlayerPrefs.DeleteKey(KEY_FRONT_TEX_EXTEND_EDGE); PlayerPrefs.DeleteKey(KEY_FRONT_TEX_FOLLOW_TABLE_BG); PlayerPrefs.DeleteKey(KEY_FRONT_TEX_FOLLOW_TABLE_BG_TO_EDGE);
        PlayerPrefs.Save();
        NotifyCard3DAppearanceChanged();
    }
}
