using UnityEngine;

public partial class ConfigManager
{
    // Reset only scene appearance preferences. Never clear the asset library or app/account settings.
    public void RestoreDefaultTablecloth()
    {
        ResetTableClothColors();
        DeleteScenePreferences("SelectedTableClothPath", "SelectedTableClothIsCustom",
            "SelectedTableSeam", "SelectedTableShadow", "SelectedTableLight");
        // Match Desktop's original blue cloth and make the gallery selection explicit.
        PlayerPrefs.SetString("SelectedTableClothPath", "Tablecloth_blue");
        PlayerPrefs.Save();
    }

    public void RestoreDefaultTableFrame()
    {
        ResetTableFrameParameters();
        DeleteScenePreferences("SelectedTableEdgePath", "SelectedTableEdgeIsCustom", TableContactOutlineKey);
        PlayerPrefs.SetString("SelectedTableEdgePath", TableFrameStyles.Default);
        PlayerPrefs.Save();
    }

    public void RestoreDefaultCenterDisplay()
    {
        PlayerPrefs.DeleteKey(CenterDisplayPreferenceKey);
        PlayerPrefs.Save();
        CenterDisplayChanged?.Invoke(SelectedCenterDisplayId);
    }

    public void RestoreDefault3DCards()
    {
        Card3DPresetLibrary.Instance?.BeginEditing();
        Card3DPresetLibrary.Instance?.Flush();
        DeleteScenePreferences(KEY_CARD_BACK_COLOR, KEY_CARD_BACK_IMAGE_PATH, KEY_CARD_BACK_IMAGE_IS_CUSTOM,
            KEY_SIDE_COLOR, KEY_SIDE_LIGHTING_VERSION, KEY_BACK_EDGE_COLOR, KEY_BACK_EDGE_SYNC, KEY_BACK_EDGE_MODE,
            KEY_TABLE_BG_PATH, KEY_TABLE_BG_IS_CUSTOM, KEY_USE_TABLE_FACE_BACKGROUND,
            KEY_FRONT_EDGE_COLOR, KEY_FRONT_EDGE_SYNC, KEY_FRONT_EDGE_MODE,
            KEY_FRONT_TEX_EXTEND_EDGE, KEY_FRONT_TEX_FOLLOW_TABLE_BG, KEY_FRONT_TEX_FOLLOW_TABLE_BG_TO_EDGE,
            KEY_TABLE_FACE_COLOR, KEY_TABLE_FACE_USE_SOLID,
            "CardBackBrightness", "TableFaceBrightness", "FrontEdgeBrightness", "BackEdgeBrightness");
        CardBackColor = DefaultCardBackColor;
        SideColor = DefaultSideColor;
        BackEdgeColor = DefaultBackEdgeColor;
        BackEdgeSyncEnabled = true;
        BackEdgeMode = CardEdgePanel.BackEdgeMode.FollowBack;
        FrontEdgeColor = Color.white;
        FrontEdgeSyncEnabled = false;
        FrontEdgeMode = CardEdgePanel.FrontEdgeMode.Independent;
        TableFaceColor = DefaultTableFaceColor;
        TableFaceUseSolidColor = false;
        UseTableFaceBackground = false;
        CardBackBrightness = TableFaceBrightness = FrontEdgeBrightness = BackEdgeBrightness = 0f;
        PlayerPrefs.Save();
        Card3DPresetLibrary.Instance?.RestoreSelectionDefaults();
    }

    public void RestoreAllSceneDefaults()
    {
        RestoreDefaultTablecloth();
        RestoreDefaultTableFrame();
        RestoreDefault3DCards();
        DeleteScenePreferences(KEY_STANDARD_TILE_PACK_ID, KEY_CUSTOM_STANDARD_TILE_PACK,
            KEY_HAND_BG_PATH, KEY_HAND_BG_IS_CUSTOM, KEY_HAND_BACK_PATH, KEY_HAND_BACK_IS_CUSTOM,
            KEY_USE_HAND_FACE_BACKGROUND);
        StandardTilePackId = TilePackIds.PackOfficial;
        UseHandFaceBackground = true;
        PlayerPrefs.Save();
        RestoreDefaultCenterDisplay();
    }

    private static void DeleteScenePreferences(params string[] keys)
    {
        foreach (string key in keys) PlayerPrefs.DeleteKey(key);
    }
}
