using System;
using TMPro;
using UnityEngine;

/// <summary>固定官方牌组的设置预览；使用明确的资源系列，避免不同规则的牌号碰撞。</summary>
public sealed class CardFaceFamilyPreview : MonoBehaviour
{
    public enum Source { Hongque, Singapore, Malaysia, Vietnamese, Duocai }
    [Serializable] public sealed class Entry {
        public Source source;
        public int id;
        public CardFacePreviewSlot slot;
        public CardFacePreviewSlot tableSlot;
    }
    public GameObject settings, previewRoot;
    public GameObject[] standardPreviewGroups;
    public RectTransform hongqueContent, expandedContent;
    public Entry[] hongqueEntries, expandedEntries;
    public UnityEngine.UI.ScrollRect scroll;
    public UnityEngine.UI.ScrollRect tableScroll;
    public RectTransform hongqueTableContent, expandedTableContent;
    public CardFaceLayoutPresentation.Row[] handRows, tableRows;
    public UnityEngine.UI.Button standardOption, handButton, tableButton;
    public TMP_Text optionLabel, previewTitle;
    public UnityEngine.UI.Image optionBackground, optionArtwork;
    public SingleTileAdjustmentPanel tileEditor;
    public CardFacePreviewReadingArea readingArea;
    int family;
    string editingPack;
    bool handDirty, tableDirty, refreshing;
    float handSaveAt, tableSaveAt;
    public float PreferredContentHeight => 706;

    void Awake() {
        standardOption.onClick.AddListener(Refresh);
        BindRows(handRows, false); BindRows(tableRows, true);
    }
    void OnEnable() {
        TileFaceResolver.OnPackChanged += Refresh;
        HandSurfaceLibrary.Changed += Refresh;
    }
    void OnDisable() {
        TileFaceResolver.OnPackChanged -= Refresh;
        HandSurfaceLibrary.Changed -= Refresh;
        Commit();
    }
    void OnApplicationQuit() => Commit();
    void Update() {
        if (handDirty && Time.unscaledTime >= handSaveAt) SaveGroup(false);
        if (tableDirty && Time.unscaledTime >= tableSaveAt) SaveGroup(true);
    }
    void BindRows(CardFaceLayoutPresentation.Row[] rows, bool isTable) {
        if (rows == null) return;
        FaceScaleSlider.Configure(rows[2].slider);
        for (int i = 0; i < 3; i++) {
            int axis = i;
            rows[i].slider.onValueChanged.AddListener(value => Change(isTable, axis, axis == 2 ? FaceScaleSlider.ToScale(value) * 100f : value));
            rows[i].reset.onClick.AddListener(() => {
                var value = TilePackLayoutConfig.Default(editingPack, isTable);
                Change(isTable, axis, (axis == 0 ? value.x : axis == 1 ? value.y : value.scale) * 100f);
                SaveGroup(isTable);
            });
        }
    }
    HandSurfaceLibrary.FaceLayout Group(bool isTable) => isTable ? TableFaceLayoutSettings.Get(editingPack) : HandFaceAdjustmentSettings.Get(editingPack);
    void Change(bool isTable, int axis, float percentage) {
        if (refreshing || family == 0 || string.IsNullOrEmpty(editingPack)) return;
        var value = Group(isTable);
        if (axis == 0) value.x = percentage / 100f;
        else if (axis == 1) value.y = percentage / 100f;
        else value.scale = percentage / 100f;
        if (isTable) {
            tableDirty = true; tableSaveAt = Time.unscaledTime + .3f;
            TableFaceLayoutSettings.Preview(editingPack, value);
        } else {
            handDirty = true; handSaveAt = Time.unscaledTime + .3f;
            HandFaceAdjustmentSettings.Preview(editingPack, value);
        }
    }
    void SaveGroup(bool isTable) {
        if (string.IsNullOrEmpty(editingPack)) return;
        if (isTable && tableDirty) { tableDirty = false; TableFaceLayoutSettings.Save(editingPack, Group(true)); }
        if (!isTable && handDirty) { handDirty = false; HandFaceAdjustmentSettings.Save(editingPack, Group(false)); }
    }
    void Commit() { SaveGroup(false); SaveGroup(true); }

    public void Show(int selectedFamily) {
        Commit();
        if (tileEditor != null) tileEditor.Close();
        family = selectedFamily;
        editingPack = family == 1 ? FixedFacePack.Hongque : family == 2 ? FixedFacePack.Expanded : null;
        bool special = family != 0;
        settings.SetActive(special); previewRoot.SetActive(special);
        for (int i = 0; i < standardPreviewGroups.Length; i++) standardPreviewGroups[i].SetActive(i == 0 || !special);
        hongqueContent.gameObject.SetActive(family == 1);
        expandedContent.gameObject.SetActive(family == 2);
        scroll.content = family == 1 ? hongqueContent : expandedContent;
        scroll.StopMovement(); scroll.verticalNormalizedPosition = 1f;
        hongqueTableContent.gameObject.SetActive(family == 1);
        expandedTableContent.gameObject.SetActive(family == 2);
        tableScroll.content = family == 1 ? hongqueTableContent : expandedTableContent;
        tableScroll.StopMovement(); tableScroll.verticalNormalizedPosition = 1f;
        if (readingArea != null) readingArea.ShowFamily(family);
        Refresh();
    }

    void Refresh() {
        if (family == 0 || !isActiveAndEnabled || refreshing) return;
        refreshing = true;
        optionLabel.text = family == 1 ? "虹雀麻将牌(标准)" : "拓展麻将牌(标准)";
        standardOption.GetComponent<CardSurfaceOptionView>().SetSelected(true);
        RefreshRows(handRows, Group(false)); RefreshRows(tableRows, Group(true));
        var source = family == 1 ? Source.Hongque : Source.Malaysia;
        int sample = FixedFacePack.Key(source, family == 1 ? 1001 : 61);
        optionArtwork.sprite = FixedFacePack.Load(editingPack, sample, false);
        optionArtwork.color = Color.white; optionArtwork.gameObject.SetActive(true);
        if (family == 1) {
            optionBackground.sprite = null; optionBackground.color = Color.clear;
            TileFaceFit.ApplyStandaloneHand(optionArtwork, optionBackground.rectTransform, sample, editingPack);
        } else {
            optionBackground.color = Color.white;
            optionBackground.sprite = TileFaceResolver.LoadHandBackground();
            TileFaceFit.ApplyHandArtwork(optionArtwork, optionBackground, sample, HandSurfaceLibrary.CurrentFaceLayout, editingPack);
        }
        optionBackground.preserveAspect = true;
        foreach (var entry in family == 1 ? hongqueEntries : expandedEntries) {
            int key = FixedFacePack.Key(entry.source, entry.id);
            FixedFacePack.ApplyPreview(entry.slot, editingPack, key, false);
            FixedFacePack.ApplyPreview(entry.tableSlot, editingPack, key, true);
        }
        refreshing = false;
    }
    static void RefreshRows(CardFaceLayoutPresentation.Row[] rows, HandSurfaceLibrary.FaceLayout value) {
        for (int i = 0; i < 3; i++) {
            float number = i == 0 ? value.x : i == 1 ? value.y : value.scale;
            rows[i].slider.SetValueWithoutNotify(i == 2 ? FaceScaleSlider.ToPosition(number) : number * 100f);
            rows[i].value.text = (number * 100f).ToString("0.#") + "%";
        }
    }
}
