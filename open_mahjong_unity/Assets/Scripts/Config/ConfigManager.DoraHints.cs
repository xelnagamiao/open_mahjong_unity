using UnityEngine;

public partial class ConfigManager {
    internal const string DoraHintsKey = "RiichiDoraHintsEnabled";
    public bool DoraHintsEnabled { get; private set; }

    public void SetDoraHintsEnabled(bool enabled) {
        DoraHintsEnabled = enabled;
        PlayerPrefs.SetInt(DoraHintsKey, enabled ? 1 : 0);
        PlayerPrefs.Save();
        DoraTileSheen.RefreshAll();
    }
}
