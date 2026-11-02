using System.Collections.Generic;
using TMPro;
using UnityEngine;

[DisallowMultipleComponent, RequireComponent(typeof(TMP_Dropdown))]
public sealed class LanguageSelector : MonoBehaviour
{
    TMP_Dropdown dropdown;
    void Awake()
    {
        dropdown = GetComponent<TMP_Dropdown>();
        dropdown.ClearOptions();
        dropdown.AddOptions(new List<string>(ConfigManager.LanguageOptionLabels));
        dropdown.onValueChanged.AddListener(Select);
    }
    void OnEnable() { ConfigManager.OnLanguageChanged += Refresh; Refresh(); }
    void Start() => Refresh();
    void OnDisable() { ConfigManager.OnLanguageChanged -= Refresh; }
    void OnDestroy() { if (dropdown) dropdown.onValueChanged.RemoveListener(Select); }
    void Select(int value) { if (ConfigManager.Instance) ConfigManager.Instance.SetLanguageMode(value); }
    public void Refresh()
    {
        if (!dropdown) dropdown = GetComponent<TMP_Dropdown>();
        dropdown.SetValueWithoutNotify((int)ConfigManager.CurrentLanguage);
        dropdown.RefreshShownValue();
    }
}
