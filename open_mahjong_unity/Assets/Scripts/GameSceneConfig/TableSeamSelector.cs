using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

/// <summary>Independent table effects, using the settings UI's existing dropdown template.</summary>
public sealed partial class TableSeamSelector : MonoBehaviour
{
    private static readonly Color TextColor = new Color32(40, 58, 78, 255);
    private static readonly Color OptionTextColor = new Color32(238, 243, 250, 255);
    private static readonly Color DropdownColor = new Color32(38, 44, 56, 255);
    private static readonly Color AccentColor = new Color32(241, 183, 121, 255);
    [SerializeField] private TMP_Dropdown[] dropdowns = new TMP_Dropdown[4];
    [SerializeField] private RectTransform[] rows = new RectTransform[4];
    [SerializeField] private RectTransform[] labels = new RectTransform[4];
    [SerializeField] private RectTransform gallery;
    [SerializeField] private RectTransform container;
    [SerializeField] private RectTransform surfaceTitle;
    [SerializeField] private Vector2 originalMin;
    [SerializeField] private Vector2 originalMax;
    private Coroutine waitingForConfig;
    [SerializeField] private TableClothColorControl colorControl;
    [SerializeField] private RectTransform colorHost;
    private bool bound;

    public bool IsLoading => waitingForConfig != null;
    public bool HasBakedUi => container != null && colorControl != null && dropdowns.Length == 4
        && dropdowns[1] != null && dropdowns[2] != null && dropdowns[3] != null;

    public void Initialize(TableClothPanel panel)
    {
        if (!Application.isPlaying || !HasBakedUi) return;
        if (!bound) {
            bound = true;
            dropdowns[1].onValueChanged.AddListener(index => SelectStyle(TableSurfaceNames.SeamStyleAtOption(index)));
            dropdowns[2].onValueChanged.AddListener(index => SelectShadow(TableLightingPresets.ShadowStyleAtOption(index)));
            dropdowns[3].onValueChanged.AddListener(index => SelectLight(TableLightingPresets.LightStyleAtOption(index)));
        }
        if (isActiveAndEnabled) Resume();
    }


    public void SelectStyle(int style)
    {
        var config = ConfigManager.Instance;
        if (style < -1 || style >= TableSurfaceNames.SeamStyleCount || config == null) return;
        if (config.GetSelectedTableSeam() == style) { RefreshSelection(); return; }
        config.SetSelectedTableSeam(style);
        ApplySelection();
    }

    public void SelectShadow(int preset)
    {
        var config = ConfigManager.Instance;
        if (config == null) return;
        preset = TableLightingPresets.ClampShadow(preset);
        if (config.GetSelectedTableShadow() == preset) { RefreshSelection(); return; }
        config.SetSelectedTableShadow(preset);
        ApplySelection();
    }

    public void SelectLight(int preset)
    {
        var config = ConfigManager.Instance;
        if (config == null) return;
        preset = TableLightingPresets.ClampLight(preset);
        if (config.GetSelectedTableLight() == preset) { RefreshSelection(); return; }
        config.SetSelectedTableLight(preset);
        ApplySelection();
    }

    private void ApplySelection()
    {
        RefreshSelection();
        Desktop.Instance?.RefreshAppearance();
    }

    public void RefreshSelection()
    {
        var config = ConfigManager.Instance;
        foreach (var dropdown in dropdowns) if (dropdown != null) dropdown.interactable = config != null;
        colorControl?.RefreshSelection();
        if (config == null) return;
        if (dropdowns[1] != null) dropdowns[1].SetValueWithoutNotify(TableSurfaceNames.SeamOptionForStyle(config.GetSelectedTableSeam()));
        if (dropdowns[2] != null) dropdowns[2].SetValueWithoutNotify(TableLightingPresets.ShadowOptionForStyle(config.GetSelectedTableShadow()));
        if (dropdowns[3] != null) dropdowns[3].SetValueWithoutNotify(TableLightingPresets.LightOptionForStyle(config.GetSelectedTableLight()));
    }

    private void Resume()
    {
        container.gameObject.SetActive(true);
        RefreshSelection();
        if (Application.isPlaying && ConfigManager.Instance == null && waitingForConfig == null)
            waitingForConfig = StartCoroutine(WaitForConfig());
    }

    private IEnumerator WaitForConfig()
    {
        yield return null;
        while (ConfigManager.Instance == null) yield return null;
        RefreshSelection();
        waitingForConfig = null;
    }

    private void OnEnable()
    {
        Initialize(GetComponent<TableClothPanel>());
    }


    private void CloseDropdowns()
    {
        if (!Application.isPlaying) return;
        foreach (var dropdown in dropdowns)
            if (dropdown != null && dropdown.IsExpanded)
            {
                dropdown.Hide();
                // TMP_Dropdown.OnDisable also removes its blocker and pending popup.
                dropdown.gameObject.SetActive(false);
                dropdown.gameObject.SetActive(true);
            }
    }

    private void OnDisable()
    {
        if (waitingForConfig != null) StopCoroutine(waitingForConfig);
        waitingForConfig = null;
        CloseDropdowns();
    }

    private void OnDestroy()
    {
        OnDisable();
    }

}
