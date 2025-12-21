using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

// 设置项类型枚举
public enum SettingType
{
    Toggle,     // 开关（布尔）
    Slider,     // 滑动条（数值）
    Dropdown    // 下拉框
}

// 设置项数据类
[System.Serializable]
public class SettingItem
{
    public string settingName;          // 设置名称
    public SettingType settingType;     // 设置类型
    public float defaultValue;          // 默认值（用于滑动条）
    public float minValue;              // 最小值（用于滑动条）
    public float maxValue;              // 最大值（用于滑动条）
    public bool defaultToggleValue;     // 默认布尔值（用于开关）
    public List<string> dropdownOptions; // 下拉框选项
    public int defaultDropdownIndex;    // 默认下拉框索引
}

public class AppConfigPanel : MonoBehaviour
{
    public static AppConfigPanel Instance;

    [Header("预制体引用")]
    [SerializeField] private GameObject togglePrefab;    // 开关预制体
    [SerializeField] private GameObject sliderPrefab;    // 滑动条预制体
    [SerializeField] private GameObject dropdownPrefab;  // 下拉框预制体

    [Header("设置容器")]
    [SerializeField] private Transform settingsContainer; // 设置项容器

    [Header("设置项配置")]
    [SerializeField] private List<SettingItem> settingItems = new List<SettingItem>(); // 设置项列表

    private Dictionary<string, object> settingValues = new Dictionary<string, object>(); // 存储设置值

    private void Awake()
    {
        Instance = this;
    }

    private void Start()
    {
        // 初始化所有设置项
        InitializeSettings();
    }

    // 初始化设置项
    private void InitializeSettings()
    {
        foreach (var setting in settingItems)
        {
            CreateSettingItem(setting);
        }
    }

    // 创建设置项
    private void CreateSettingItem(SettingItem setting)
    {
        GameObject prefab = null;

        switch (setting.settingType)
        {
            case SettingType.Toggle:
                prefab = togglePrefab;
                break;
            case SettingType.Slider:
                prefab = sliderPrefab;
                break;
            case SettingType.Dropdown:
                prefab = dropdownPrefab;
                break;
        }

        if (prefab == null || settingsContainer == null)
        {
            Debug.LogError($"预制体或容器未设置: {setting.settingName}");
            return;
        }

        GameObject settingObject = Instantiate(prefab, settingsContainer);
        ConfigureSettingItem(settingObject, setting);
    }

    // 配置设置项
    private void ConfigureSettingItem(GameObject settingObject, SettingItem setting)
    {
        // 设置标签文本
        TextMeshProUGUI label = settingObject.GetComponentInChildren<TextMeshProUGUI>();
        if (label != null)
        {
            label.text = setting.settingName;
        }

        switch (setting.settingType)
        {
            case SettingType.Toggle:
                ConfigureToggle(settingObject, setting);
                break;
            case SettingType.Slider:
                ConfigureSlider(settingObject, setting);
                break;
            case SettingType.Dropdown:
                ConfigureDropdown(settingObject, setting);
                break;
        }
    }

    // 配置开关
    private void ConfigureToggle(GameObject settingObject, SettingItem setting)
    {
        Toggle toggle = settingObject.GetComponentInChildren<Toggle>();
        if (toggle != null)
        {
            toggle.isOn = setting.defaultToggleValue;
            settingValues[setting.settingName] = setting.defaultToggleValue;

            toggle.onValueChanged.AddListener((value) => {
                settingValues[setting.settingName] = value;
                OnSettingChanged(setting.settingName, value);
            });
        }
    }

    // 配置滑动条
    private void ConfigureSlider(GameObject settingObject, SettingItem setting)
    {
        Slider slider = settingObject.GetComponentInChildren<Slider>();
        if (slider != null)
        {
            slider.minValue = setting.minValue;
            slider.maxValue = setting.maxValue;
            slider.value = setting.defaultValue;
            settingValues[setting.settingName] = setting.defaultValue;

            slider.onValueChanged.AddListener((value) => {
                settingValues[setting.settingName] = value;
                OnSettingChanged(setting.settingName, value);
            });

            // 如果有数值显示文本，更新它
            TextMeshProUGUI valueText = settingObject.GetComponentsInChildren<TextMeshProUGUI>()[1];
            if (valueText != null)
            {
                valueText.text = setting.defaultValue.ToString("F1");
                slider.onValueChanged.AddListener((value) => {
                    valueText.text = value.ToString("F1");
                });
            }
        }
    }

    // 配置下拉框
    private void ConfigureDropdown(GameObject settingObject, SettingItem setting)
    {
        TMP_Dropdown dropdown = settingObject.GetComponentInChildren<TMP_Dropdown>();
        if (dropdown != null && setting.dropdownOptions != null)
        {
            dropdown.ClearOptions();
            dropdown.AddOptions(setting.dropdownOptions);
            dropdown.value = setting.defaultDropdownIndex;
            settingValues[setting.settingName] = setting.defaultDropdownIndex;

            dropdown.onValueChanged.AddListener((value) => {
                settingValues[setting.settingName] = value;
                OnSettingChanged(setting.settingName, value);
            });
        }
    }

    // 设置改变时的回调（可以在这里保存设置或触发其他逻辑）
    private void OnSettingChanged(string settingName, object value)
    {
        Debug.Log($"设置改变: {settingName} = {value}");
        // 可以在这里添加保存设置到 PlayerPrefs 的逻辑
        SaveSetting(settingName, value);
    }

    // 保存设置
    private void SaveSetting(string settingName, object value)
    {
        if (value is bool boolValue)
        {
            PlayerPrefs.SetInt(settingName, boolValue ? 1 : 0);
        }
        else if (value is float floatValue)
        {
            PlayerPrefs.SetFloat(settingName, floatValue);
        }
        else if (value is int intValue)
        {
            PlayerPrefs.SetInt(settingName, intValue);
        }
        PlayerPrefs.Save();
    }

    // 获取设置值（公共接口）
    public T GetSetting<T>(string settingName)
    {
        if (settingValues.ContainsKey(settingName))
        {
            return (T)settingValues[settingName];
        }
        return default(T);
    }

    // 动态添加设置项（公共接口）
    public void AddSettingItem(SettingItem setting)
    {
        settingItems.Add(setting);
        CreateSettingItem(setting);
    }

}
