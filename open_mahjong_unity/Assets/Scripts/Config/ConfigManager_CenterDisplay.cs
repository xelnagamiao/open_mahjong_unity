using System;
using UnityEngine;

public partial class ConfigManager
{
    public const string CenterDisplayPreferenceKey = "SelectedCenterDisplayId";

    // Stable IDs keep saved choices independent of card ordering or translated names.
    public string SelectedCenterDisplayId => CenterDisplayStyles.Normalize(
        PlayerPrefs.GetString(CenterDisplayPreferenceKey, CenterDisplayStyles.Classic));

    public event Action<string> CenterDisplayChanged;

    public void SetSelectedCenterDisplay(string id)
    {
        string selected = CenterDisplayStyles.Normalize(id);
        PlayerPrefs.SetString(CenterDisplayPreferenceKey, selected);
        PlayerPrefs.Save();
        CenterDisplayChanged?.Invoke(selected);
    }
}
