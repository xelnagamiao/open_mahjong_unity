using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

/// <summary>Independent table effects, using the settings UI's existing dropdown template.</summary>
public sealed class TableSeamSelector : MonoBehaviour
{
    private static readonly Color TextColor = new Color32(40, 58, 78, 255);
    private static readonly Color OptionTextColor = new Color32(238, 243, 250, 255);
    private static readonly Color DropdownColor = new Color32(38, 44, 56, 255);
    private static readonly Color AccentColor = new Color32(241, 183, 121, 255);
    private readonly TMP_Dropdown[] dropdowns = new TMP_Dropdown[4];
    private readonly RectTransform[] rows = new RectTransform[4];
    private readonly RectTransform[] labels = new RectTransform[4];
    private RectTransform gallery;
    private RectTransform container;
    private RectTransform surfaceTitle;
    private Vector2 originalMin;
    private Vector2 originalMax;
    private Coroutine waitingForConfig;
    private bool layoutApplied;
    private float lastWidth = -1;
    private TableClothColorControl colorControl;
    private RectTransform colorHost;

    public bool IsLoading => waitingForConfig != null;

    // Called after the surface footer changes its gallery reservation, while the panel is hidden.
    public void RefreshGalleryBounds()
    {
        if (gallery == null || container == null) return;
        originalMin = gallery.offsetMin;
        originalMax = gallery.offsetMax;
        container.anchorMin = new Vector2(gallery.anchorMin.x, gallery.anchorMax.y);
        container.anchorMax = new Vector2(gallery.anchorMax.x, gallery.anchorMax.y);
        ResizeLayout();
    }

    public void Initialize(TableClothPanel panel)
    {
        if (panel == null) return;
        if (container == null)
        {
            var scroll = panel.contentParent != null ? panel.contentParent.GetComponentInParent<ScrollRect>(true) : null;
            var template = FindTemplate(panel.GetComponentInParent<Canvas>());
            if (scroll == null || template == null) return;
            gallery = scroll.GetComponent<RectTransform>();
            originalMin = gallery.offsetMin;
            originalMax = gallery.offsetMax;
            Build(template, panel);
        }
        if (isActiveAndEnabled) Resume();
    }

    internal static TMP_Dropdown FindTemplate(Canvas canvas)
    {
        if (canvas == null) return null;
        TMP_Dropdown fallback = null;
        foreach (var candidate in canvas.GetComponentsInChildren<TMP_Dropdown>(true))
        {
            if (candidate.template == null || candidate.captionText == null || candidate.itemText == null ||
                candidate.template.GetComponentInChildren<ScrollRect>(true) == null ||
                candidate.template.GetComponentInChildren<Toggle>(true) == null) continue;
            if (candidate.name == "CustomPackDropdown") return candidate;
            if (fallback == null) fallback = candidate;
        }
        return fallback;
    }

    private void Build(TMP_Dropdown template, TableClothPanel panel)
    {
        container = CreateRect("TableSeamSelector", gallery.parent);
        container.gameObject.SetActive(false);
        container.anchorMin = new Vector2(gallery.anchorMin.x, gallery.anchorMax.y);
        container.anchorMax = new Vector2(gallery.anchorMax.x, gallery.anchorMax.y);
        container.offsetMin = new Vector2(originalMin.x, originalMax.y - 80);
        container.offsetMax = originalMax;
        // Keep labels legible regardless of the selected cloth or translucent parent panel.
        var panelBackground = container.gameObject.AddComponent<Image>();
        panelBackground.color = SceneConfigUi.SurfaceHeaderBackground;
        panelBackground.raycastTarget = false;
        surfaceTitle = SceneConfigUi.CreateSurfaceHeaderTitle(container, "桌布", template.captionText.font).rectTransform;

        var seamNames = new List<string>(TableSurfaceNames.SeamOptionCount);
        for (int index = 0; index < TableSurfaceNames.SeamOptionCount; index++)
            seamNames.Add(TableSurfaceNames.SeamDisplayName(TableSurfaceNames.SeamStyleAtOption(index)));
        rows[0] = CreateRect("ClothColorRow", container);
        var colorLabel = CreateRect("Label", rows[0]).gameObject.AddComponent<TextMeshProUGUI>();
        colorLabel.font = template.captionText.font; colorLabel.text = "底色";
        colorLabel.color = TextColor; colorLabel.fontSize = 17; colorLabel.raycastTarget = false;
        colorLabel.alignment = TextAlignmentOptions.MidlineLeft;
        labels[0] = colorLabel.rectTransform;
        colorHost = CreateRect("ColorControl", rows[0]);
        colorControl = container.gameObject.AddComponent<TableClothColorControl>();
        colorControl.Initialize(colorHost, container, panel, template.captionText.font);
        CreateDropdown(1, "缝线", "TableSeamDropdown", seamNames, template,
            index => SelectStyle(TableSurfaceNames.SeamStyleAtOption(index)));
        CreateDropdown(2, "边框阴影", "TableShadowDropdown", new List<string>(TableLightingPresets.ShadowNames), template,
            index => SelectShadow(TableLightingPresets.ShadowStyleAtOption(index)));
        CreateDropdown(3, "光照", "TableLightDropdown", new List<string>(TableLightingPresets.LightNames), template,
            index => SelectLight(TableLightingPresets.LightStyleAtOption(index)));
        ResizeLayout();
    }

    private void CreateDropdown(int index, string title, string objectName, List<string> options,
        TMP_Dropdown template, UnityAction<int> onSelected)
    {
        rows[index] = CreateRect(objectName + "Row", container);
        var label = CreateRect("Label", rows[index]).gameObject.AddComponent<TextMeshProUGUI>();
        labels[index] = label.rectTransform;
        label.font = template.captionText.font;
        label.color = TextColor;
        label.fontSize = 17;
        label.text = title;
        label.alignment = TextAlignmentOptions.MidlineLeft;
        label.textWrappingMode = TextWrappingModes.NoWrap;
        label.raycastTarget = false;

        var dropdown = Instantiate(template, rows[index], false);
        dropdown.name = objectName;
        dropdown.transform.localScale = Vector3.one;
        dropdowns[index] = dropdown;
        // Clone visuals and scrolling, not the original setting's listeners or selection.
        dropdown.onValueChanged = new TMP_Dropdown.DropdownEvent();
        dropdown.MultiSelect = false;
        dropdown.ClearOptions();
        dropdown.AddOptions(options);
        dropdown.SetValueWithoutNotify(0);
        dropdown.onValueChanged.AddListener(onSelected);
        dropdown.navigation = new Navigation { mode = Navigation.Mode.Automatic };
        dropdown.template.gameObject.SetActive(false);
        dropdown.template.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, 200);
        // The source can be expanded when this panel is opened. Never copy its live popup.
        foreach (Transform child in dropdown.transform)
            if (child.name == "Dropdown List")
            {
                child.gameObject.SetActive(false);
                Release(child.gameObject);
            }
        ConfigureText(dropdown.captionText);
        ConfigureText(dropdown.itemText);
        ConfigureDropdownAppearance(dropdown);
        var scroll = dropdown.template.GetComponentInChildren<ScrollRect>(true);
        scroll.horizontal = false;
        scroll.vertical = true;
        if (scroll.viewport != null)
        {
            var viewport = scroll.viewport;
            viewport.offsetMin = new Vector2(viewport.offsetMin.x, 0);
            viewport.offsetMax = new Vector2(viewport.offsetMax.x, 0);
        }
        dropdown.gameObject.SetActive(true);
    }

    private static void ConfigureText(TMP_Text text)
    {
        text.color = OptionTextColor;
        text.enableVertexGradient = false;
        text.enableAutoSizing = true;
        text.fontSizeMin = 13;
        text.fontSizeMax = 18;
        text.fontSize = 18;
        text.textWrappingMode = TextWrappingModes.NoWrap;
    }

    internal static void ConfigureDropdownAppearance(TMP_Dropdown dropdown)
    {
        // Apply only to this cloned selector, never to the shared card-settings template.
        var colors = ColorBlock.defaultColorBlock;
        colors.normalColor = DropdownColor;
        colors.highlightedColor = new Color32(57, 70, 89, 255);
        colors.selectedColor = colors.highlightedColor;
        colors.pressedColor = new Color32(24, 31, 43, 255);
        colors.disabledColor = new Color32(65, 71, 83, 255);
        colors.colorMultiplier = 1f;
        colors.fadeDuration = .1f;
        ApplyColors(dropdown, colors);

        var popupBackground = dropdown.template.GetComponent<Image>();
        if (popupBackground != null)
        {
            popupBackground.sprite = null;
            popupBackground.type = Image.Type.Simple;
            popupBackground.color = DropdownColor;
        }
        var arrow = dropdown.transform.Find("Arrow")?.GetComponent<Graphic>();
        if (arrow != null) arrow.color = AccentColor;

        foreach (var toggle in dropdown.template.GetComponentsInChildren<Toggle>(true))
        {
            ApplyColors(toggle, colors);
            if (toggle.graphic != null) toggle.graphic.color = AccentColor;
        }
        foreach (var scrollbar in dropdown.template.GetComponentsInChildren<Scrollbar>(true))
        {
            var track = scrollbar.GetComponent<Image>();
            if (track != null) track.color = new Color32(29, 35, 46, 255);
            var scrollColors = colors;
            scrollColors.normalColor = new Color32(117, 148, 173, 255);
            scrollColors.highlightedColor = new Color32(86, 124, 155, 255);
            scrollColors.selectedColor = scrollColors.highlightedColor;
            scrollColors.pressedColor = AccentColor;
            ApplyColors(scrollbar, scrollColors);
        }
    }

    private static void ApplyColors(Selectable selectable, ColorBlock colors)
    {
        if (selectable.targetGraphic != null) selectable.targetGraphic.color = Color.white;
        if (selectable.targetGraphic is Image background)
        {
            background.sprite = null;
            background.type = Image.Type.Simple;
        }
        selectable.transition = Selectable.Transition.ColorTint;
        selectable.colors = colors;
    }

    private RectTransform CreateRect(string objectName, Transform parent)
    {
        var child = new GameObject(objectName, typeof(RectTransform));
        child.layer = gameObject.layer;
        child.transform.SetParent(parent, false);
        return child.GetComponent<RectTransform>();
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
        layoutApplied = true;
        ResizeLayout();
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
        if (container != null) Resume();
    }

    private void OnRectTransformDimensionsChange() => ResizeLayout();

    private void ResizeLayout()
    {
        if (container == null || gallery == null || dropdowns[dropdowns.Length - 1] == null) return;
        float width = container.rect.width;
        if (lastWidth >= 0 && !Mathf.Approximately(lastWidth, width)) CloseDropdowns();
        lastWidth = width;
        bool titleAbove = width < 600;
        float controlsLeft = titleAbove ? 0 : 120;
        float controlsTop = titleAbove ? 44 : 0;
        float controlsWidth = width - controlsLeft;
        int columns = controlsWidth >= 1050 ? 4 : controlsWidth >= 600 ? 2 : 1;
        bool stacked = columns > 1 || controlsWidth < 320;
        float rowHeight = stacked ? 64 : 40;
        int rowCount = Mathf.CeilToInt((float)rows.Length / columns);
        float height = controlsTop + 16 + rowCount * rowHeight + (rowCount - 1) * 8;
        container.offsetMin = new Vector2(originalMin.x, originalMax.y - height);
        if (layoutApplied) gallery.offsetMax = originalMax - new Vector2(0, height);
        Place(surfaceTitle, 20, 8, titleAbove ? Mathf.Max(1, width - 40) : 88, titleAbove ? 32 : height - 16);
        for (int i = 0; i < rows.Length; i++)
        {
            float cellWidth = Mathf.Max(1, (controlsWidth - 16 - (columns - 1) * 12) / columns);
            Place(rows[i], controlsLeft + 8 + i % columns * (cellWidth + 12), controlsTop + 8 + i / columns * (rowHeight + 8),
                cellWidth, rowHeight);
            Place(labels[i], 0, 0, stacked ? cellWidth : 96, stacked ? 22 : 40);
            Place(i == 0 ? colorHost : (RectTransform)dropdowns[i].transform, stacked ? 0 : 104, stacked ? 24 : 0,
                Mathf.Max(1, cellWidth - (stacked ? 0 : 104)), 40);
        }
        colorControl?.Layout();
    }

    private static void Place(RectTransform rect, float x, float y, float width, float height)
    {
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0, 1);
        rect.anchoredPosition = new Vector2(x, -y);
        rect.sizeDelta = new Vector2(width, height);
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
        if (layoutApplied && gallery != null)
        {
            gallery.offsetMin = originalMin;
            gallery.offsetMax = originalMax;
        }
        layoutApplied = false;
        if (container != null) container.gameObject.SetActive(false);
    }

    private void OnDestroy()
    {
        OnDisable();
        if (container != null) Release(container.gameObject);
    }

    private static void Release(Object ownedObject)
    {
        if (Application.isPlaying) Destroy(ownedObject);
        else DestroyImmediate(ownedObject);
    }
}
