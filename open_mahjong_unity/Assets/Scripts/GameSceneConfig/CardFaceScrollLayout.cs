using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>牌面设计的单列设置与同屏预览；素材和保存仍使用原有组件。</summary>
public sealed class CardFaceScrollLayout : MonoBehaviour
{
    [SerializeField] Button[] familyTabs;
    [SerializeField] GameObject standardContent;
    [SerializeField] ScrollRect settingsScroll;
    [SerializeField] Button[] backgroundOptions, backOptions;
    [SerializeField] CardFaceBackgroundPanel backgroundPanel;
    [SerializeField] HandSurfaceStylePicker backgroundLibrary, backLibrary;
    [SerializeField] TMP_Dropdown faceLibrary;
    [SerializeField] Slider horizontal, vertical, scale;
    [SerializeField] TMP_Text horizontalValue, verticalValue, scaleValue;
    [SerializeField] Button resetHorizontal, resetVertical, resetScale;
    [SerializeField] Slider[] backgroundAnchorSliders;
    [SerializeField] TMP_Text[] backgroundAnchorValues;
    [SerializeField] Button[] backgroundAnchorResets;
    [SerializeField] Slider[] tableSliders;
    [SerializeField] TMP_Text[] tableValues;
    [SerializeField] Button[] tableResets;
    [SerializeField] CardFacePreviewSlot[] handPreviews, tablePreviews;
    [SerializeField] RectTransform content;
    [SerializeField] CardFaceLayoutPresentation presentation;
    [SerializeField] CardFaceFamilyPreview familyPreview;
    static readonly int[] BuiltinOrder = { 1, 2, 3, 0 };
    string editingBackground;
    HandSurfaceLibrary.FaceLayout layout;
    HandSurfaceLibrary.FaceLayout handLayout;
    HandSurfaceLibrary.FaceLayout tableLayout;
    string editingPack;
    int selectedFamily;
    bool dirty, saving, handDirty, tableDirty, refreshing;
    float saveAt, handSaveAt, tableSaveAt;

    void Awake()
    {
        FaceScaleSlider.Configure(scale);
        if (backgroundAnchorSliders != null && backgroundAnchorSliders.Length == 3) FaceScaleSlider.Configure(backgroundAnchorSliders[2]);
        if (tableSliders != null && tableSliders.Length == 3) FaceScaleSlider.Configure(tableSliders[2]);
        for (int i = 0; i < familyTabs.Length; i++) {
            int index = i;
            familyTabs[i].onClick.AddListener(() => SelectFamily(index));
        }
        for (int i = 0; i < BuiltinOrder.Length; i++) {
            int index = BuiltinOrder[i];
            backgroundOptions[i].onClick.AddListener(() => SelectSurface(index, false));
            backOptions[i].onClick.AddListener(() => SelectSurface(index, true));
        }
        horizontal.onValueChanged.AddListener(value => ChangeLayout(0, value));
        vertical.onValueChanged.AddListener(value => ChangeLayout(1, value));
        scale.onValueChanged.AddListener(value => ChangeLayout(2, FaceScaleSlider.ToScale(value) * 100f));
        resetHorizontal.onClick.AddListener(() => ResetAxis(0));
        resetVertical.onClick.AddListener(() => ResetAxis(1));
        resetScale.onClick.AddListener(() => ResetAxis(2));
        if (backgroundAnchorSliders != null) for (int i = 0; i < backgroundAnchorSliders.Length; i++) {
            int axis = i;
            backgroundAnchorSliders[i].onValueChanged.AddListener(value => ChangeBackgroundAnchor(axis, axis == 2 ? FaceScaleSlider.ToScale(value) * 100f : value));
            backgroundAnchorResets[i].onClick.AddListener(() => ResetBackgroundAxis(axis));
        }
        if (tableSliders != null) for (int i = 0; i < tableSliders.Length; i++) {
            int axis = i;
            tableSliders[i].onValueChanged.AddListener(value => ChangeTableLayout(axis, axis == 2 ? FaceScaleSlider.ToScale(value) * 100f : value));
            tableResets[i].onClick.AddListener(() => ResetTableAxis(axis));
        }
    }

    void OnEnable()
    {
        TileFaceResolver.OnPackChanged += Refresh;
        HandSurfaceLibrary.Changed += Refresh;
        HandSurfaceLibrary.EnsureReady(() => { if (this != null && isActiveAndEnabled) Refresh(); });
        SelectFamily(selectedFamily);
        Refresh();
    }

    void OnDisable()
    {
        TileFaceResolver.OnPackChanged -= Refresh;
        HandSurfaceLibrary.Changed -= Refresh;
        CommitLayout();
        CommitHandLayout();
        CommitTableLayout();
    }

    void Update()
    {
        // Save after a short pause, keeping dragging responsive and avoiding disk writes per frame.
        if (dirty && !saving && Time.unscaledTime >= saveAt) CommitLayout();
        if (handDirty && Time.unscaledTime >= handSaveAt) CommitHandLayout();
        if (tableDirty && Time.unscaledTime >= tableSaveAt) CommitTableLayout();
    }

    public void Show(bool preserveSelection)
    {
        gameObject.SetActive(true);
        SelectFamily(preserveSelection ? selectedFamily : 0);
        Refresh();
    }

    public void SelectFamily(int family)
    {
        CommitLayout();
        CommitHandLayout();
        CommitTableLayout();
        selectedFamily = Mathf.Clamp(family, 0, familyTabs.Length - 1);
        standardContent.SetActive(selectedFamily == 0);
        if (familyPreview != null) familyPreview.Show(selectedFamily);
        for (int i = 0; i < familyTabs.Length; i++) SceneConfigUi.SetButtonSelected(familyTabs[i], i == selectedFamily);
        content.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, selectedFamily == 0 ? (presentation != null ? presentation.PreferredContentHeight : 920)
            : familyPreview != null ? familyPreview.PreferredContentHeight : 230);
        settingsScroll.StopMovement();
        settingsScroll.verticalNormalizedPosition = 1;
    }

    public void RefreshSettingsHeight(float height)
    {
        if (selectedFamily == 0) content.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, height);
    }

    static void SelectOption(Button button, bool selected)
    {
        var card = button.GetComponent<CardSurfaceOptionView>();
        if (card != null) card.SetSelected(selected);
        else SceneConfigUi.SetButtonSelected(button, selected);
    }

    void SelectSurface(int index, bool back)
    {
        CommitLayout();
        backgroundPanel.SelectInlineSurface(index, back);
        Refresh();
    }

    void ChangeLayout(int axis, float percentage)
    {
        if (refreshing || string.IsNullOrEmpty(editingPack)) return;
        if (axis == 0) handLayout.x = percentage / 100f;
        else if (axis == 1) handLayout.y = percentage / 100f;
        else handLayout.scale = percentage / 100f;
        handLayout = handLayout.Normalized;
        handDirty = true; handSaveAt = Time.unscaledTime + .3f;
        HandFaceAdjustmentSettings.Preview(editingPack, handLayout);
        RefreshValues();
    }

    void ResetAxis(int axis)
    {
        var value = TilePackLayoutConfig.Default(HandFaceAdjustmentSettings.SelectedPack, false);
        ChangeLayout(axis, (axis == 0 ? value.x : axis == 1 ? value.y : value.scale) * 100f);
        CommitHandLayout();
    }

    void ChangeBackgroundAnchor(int axis, float percentage)
    {
        if (refreshing || !HandSurfaceLibrary.CanEditFaceLayout(editingBackground)) return;
        if (axis == 0) layout.x = percentage / 100f;
        else if (axis == 1) layout.y = percentage / 100f;
        else layout.scale = percentage / 100f;
        layout = layout.Normalized;
        dirty = true; saveAt = Time.unscaledTime + .3f;
        HandSurfaceLibrary.PreviewFaceLayout(editingBackground, layout);
        RefreshValues();
    }

    void ResetBackgroundAxis(int axis)
    {
        var baseline = HandSurfaceLibrary.DefaultFaceLayout(editingBackground);
        ChangeBackgroundAnchor(axis, (axis == 0 ? baseline.x : axis == 1 ? baseline.y : baseline.scale) * 100f);
        CommitLayout();
    }

    void CommitHandLayout()
    {
        if (!handDirty || string.IsNullOrEmpty(editingPack)) return;
        handDirty = false;
        HandFaceAdjustmentSettings.Save(editingPack, handLayout);
    }

    void ChangeTableLayout(int axis, float percentage)
    {
        if (refreshing || string.IsNullOrEmpty(editingPack)) return;
        if (axis == 0) tableLayout.x = percentage / 100f;
        else if (axis == 1) tableLayout.y = percentage / 100f;
        else tableLayout.scale = percentage / 100f;
        tableLayout = tableLayout.Normalized;
        tableDirty = true; tableSaveAt = Time.unscaledTime + .3f;
        TableFaceLayoutSettings.Preview(editingPack, tableLayout);
        RefreshValues();
    }

    void ResetTableAxis(int axis)
    {
        var value = TilePackLayoutConfig.Default(TableFaceLayoutSettings.SelectedPack, true);
        ChangeTableLayout(axis, (axis == 0 ? value.x : axis == 1 ? value.y : value.scale) * 100f);
        CommitTableLayout();
    }

    void CommitTableLayout()
    {
        if (!tableDirty || string.IsNullOrEmpty(editingPack)) return;
        tableDirty = false;
        TableFaceLayoutSettings.Save(editingPack, tableLayout);
    }

    void CommitLayout()
    {
        if (!dirty || saving) return;
        dirty = false; saving = true;
        HandSurfaceLibrary.SaveFaceLayout(editingBackground, layout, () => { saving = false; }, error => {
            saving = false;
            SceneConfigUi.ShowTip(error);
            HandSurfaceLibrary.CancelFaceLayoutPreview(editingBackground);
        });
    }

    void RefreshValues()
    {
        horizontalValue.text = (handLayout.x * 100f).ToString("0.#") + "%";
        verticalValue.text = (handLayout.y * 100f).ToString("0.#") + "%";
        scaleValue.text = (handLayout.scale * 100f).ToString("0.#") + "%";
        if (backgroundAnchorValues != null && backgroundAnchorValues.Length == 3) {
            backgroundAnchorValues[0].text = (layout.x * 100f).ToString("0.#") + "%";
            backgroundAnchorValues[1].text = (layout.y * 100f).ToString("0.#") + "%";
            backgroundAnchorValues[2].text = (layout.scale * 100f).ToString("0.#") + "%";
        }
        if (tableValues != null && tableValues.Length == 3) {
            tableValues[0].text = (tableLayout.x * 100f).ToString("0.#") + "%";
            tableValues[1].text = (tableLayout.y * 100f).ToString("0.#") + "%";
            tableValues[2].text = (tableLayout.scale * 100f).ToString("0.#") + "%";
        }
    }

    void Refresh()
    {
        if (refreshing || ConfigManager.Instance == null) return;
        refreshing = true;
        var config = ConfigManager.Instance;
        var background = config.GetSelectedHandBackground();
        var back = config.GetSelectedHandBack();
        if (editingBackground != background.path) CommitLayout();
        if (editingPack != config.StandardTilePackId) { CommitHandLayout(); CommitTableLayout(); }
        editingPack = config.StandardTilePackId;
        handLayout = HandFaceAdjustmentSettings.Get(editingPack);
        tableLayout = TableFaceLayoutSettings.Get(editingPack);
        editingBackground = background.path;
        layout = HandSurfaceLibrary.GetFaceLayout(editingBackground);
        horizontal.SetValueWithoutNotify(handLayout.x * 100f);
        vertical.SetValueWithoutNotify(handLayout.y * 100f);
        scale.SetValueWithoutNotify(FaceScaleSlider.ToPosition(handLayout.scale));
        if (backgroundAnchorSliders != null && backgroundAnchorSliders.Length == 3) {
            backgroundAnchorSliders[0].SetValueWithoutNotify(layout.x * 100f);
            backgroundAnchorSliders[1].SetValueWithoutNotify(layout.y * 100f);
            backgroundAnchorSliders[2].SetValueWithoutNotify(FaceScaleSlider.ToPosition(layout.scale));
        }
        if (tableSliders != null && tableSliders.Length == 3) {
            tableSliders[0].SetValueWithoutNotify(tableLayout.x * 100f);
            tableSliders[1].SetValueWithoutNotify(tableLayout.y * 100f);
            tableSliders[2].SetValueWithoutNotify(FaceScaleSlider.ToPosition(tableLayout.scale));
        }
        bool editable = HandSurfaceLibrary.CanEditFaceLayout(editingBackground);
        if (backgroundAnchorSliders != null) for (int i = 0; i < backgroundAnchorSliders.Length; i++) {
            backgroundAnchorSliders[i].interactable = editable;
            backgroundAnchorResets[i].interactable = editable;
        }
        RefreshValues();
        for (int i = 0; i < BuiltinOrder.Length; i++) {
            SelectOption(backgroundOptions[i], !background.isCustom && HandSurfaceStyles.FindIndex(background.path, false) == BuiltinOrder[i]);
            SelectOption(backOptions[i], !back.isCustom && HandSurfaceStyles.FindIndex(back.path, true) == BuiltinOrder[i]);
        }
        backgroundLibrary.gameObject.SetActive(HandSurfaceLibrary.GetEntries(false).Count > 0);
        backLibrary.gameObject.SetActive(HandSurfaceLibrary.GetEntries(true).Count > 0);
        faceLibrary.gameObject.SetActive(presentation.uploadedPacks == null && TilePackLibrary.GetEntries().Count > 0);
        if (presentation != null) presentation.RefreshLayout();
        foreach (var slot in handPreviews) {
            var bg = TileFaceResolver.ShouldLayerHandFace(slot.tileId) ? TileFaceResolver.LoadHandBackground() : null;
            slot.Apply(TileFaceResolver.PreviewHand(slot.tileId), bg, false);
        }
        var tableBackground = config.UseTableFaceBackground && !config.TableFaceUseSolidColor ? TileFaceResolver.LoadTableBackground() : null;
        foreach (var slot in tablePreviews)
            slot.ApplyTable(TileFaceResolver.PreviewTable(slot.tileId), tableBackground, TileFaceResolver.TablePreviewBaseColor, false,
                TileFaceResolver.TableImageScaleFor(slot.tileId, applyWhiteDragonFaceSetting: false));
        refreshing = false;
    }
}
