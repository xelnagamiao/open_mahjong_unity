using TMPro;
using UnityEngine;

/// <summary>当前设置页内的单牌编辑器。切换牌包、牌号或显示类型前提交上一份草稿。</summary>
public sealed class SingleTileAdjustmentPanel : MonoBehaviour
{
    public RectTransform panel;
    public CardFacePreviewSlot preview;
    public TMP_Text title, packLabel;
    public UnityEngine.UI.Button close, handButton, tableButton;
    public UnityEngine.UI.Slider[] sliders;
    public TMP_Text[] values;
    public UnityEngine.UI.Button[] reset, minus, plus;
    string pack;
    int tileId = -1;
    bool table, dirty, refreshing;
    float saveAt;
    HandSurfaceLibrary.FaceLayout draft;

    void Awake() {
        FaceScaleSlider.Configure(sliders[2]);
        close.onClick.AddListener(Close);
        handButton.onClick.AddListener(() => SwitchScope(false));
        tableButton.onClick.AddListener(() => SwitchScope(true));
        for (int i = 0; i < sliders.Length; i++) {
            int axis = i;
            sliders[i].onValueChanged.AddListener(value => Change(axis, axis == 2 ? FaceScaleSlider.ToScale(value) * 100f : value));
            reset[i].onClick.AddListener(() => {
                var value = TilePackLayoutConfig.Default(pack, table, tileId);
                Change(axis, (axis == 0 ? value.x : axis == 1 ? value.y : value.scale) * 100f, false); Commit();
            });
            minus[i].onClick.AddListener(() => Change(axis, Axis(axis) * 100f - .1f));
            plus[i].onClick.AddListener(() => Change(axis, Axis(axis) * 100f + .1f));
        }
        panel.gameObject.SetActive(false);
    }
    void OnEnable() => TileFaceResolver.OnPackChanged += Refresh;
    void OnDisable() { TileFaceResolver.OnPackChanged -= Refresh; Close(); }
    void OnApplicationQuit() => Commit();
    void Update() { if (dirty && Time.unscaledTime >= saveAt) Commit(); }
    float Axis(int axis) => axis == 0 ? draft.x : axis == 1 ? draft.y : draft.scale;
    public void Select(int id, bool isTable) {
        if (!TilePackIds.IsStandardFaceId(id)) return;
        Commit(); tileId = id; table = isTable; pack = TableFaceLayoutSettings.SelectedPack;
        draft = TileFaceOffsetSettings.Get(pack, id, table);
        panel.gameObject.SetActive(true); panel.SetAsLastSibling();
        Refresh();
    }
    public void SelectFixed(string fixedPack, int key, bool isTable) {
        if (!FixedFacePack.IsEditable(fixedPack, key)) return;
        Commit(); pack = fixedPack; tileId = key; table = isTable;
        draft = TileFaceOffsetSettings.Get(pack, tileId, table);
        panel.gameObject.SetActive(true); panel.SetAsLastSibling(); Refresh();
    }
    void SwitchScope(bool isTable) {
        if (FixedFacePack.IsFixed(pack)) SelectFixed(pack, tileId, isTable);
        else Select(tileId, isTable);
    }
    void Change(int axis, float value, bool snapToStep = true) {
        if (refreshing || tileId < 0) return;
        // Slider/step-button input snaps to 0.1%; restoring preserves the exact .cfg default.
        value = snapToStep ? Mathf.Round(value * 10f) / 1000f : value / 100f;
        if (axis == 0) draft.x = value; else if (axis == 1) draft.y = value; else draft.scale = value;
        draft = TileFaceOffsetSettings.Normalize(draft); dirty = true; saveAt = Time.unscaledTime + .3f;
        TileFaceOffsetSettings.Preview(pack, tileId, table, draft);
        Refresh();
    }
    void Commit() {
        if (!dirty || tileId < 0 || string.IsNullOrEmpty(pack)) return;
        dirty = false; TileFaceOffsetSettings.Save(pack, tileId, table, draft);
    }
    public void Close() { Commit(); tileId = -1; if (panel != null) panel.gameObject.SetActive(false); }
    void Refresh() {
        if (refreshing || tileId < 0 || !panel.gameObject.activeSelf) return;
        refreshing = true;
        string selected = TableFaceLayoutSettings.SelectedPack;
        if (!FixedFacePack.IsFixed(pack) && pack != selected) { Commit(); pack = selected; }
        // Read group migration first, before the per-tile values it may create.
        if (table) TableFaceLayoutSettings.Get(pack);
        draft = TileFaceOffsetSettings.Get(pack, tileId, table);
        title.text = "单牌微调 · " + (FixedFacePack.IsFixed(pack) ? FixedFacePack.DisplayName(pack, tileId) : TileName(tileId));
        packLabel.text = PackName(pack) + " · " + (table ? "3D 牌面" : "2D 手牌牌面");
        SceneConfigUi.SetButtonSelected(handButton, !table); SceneConfigUi.SetButtonSelected(tableButton, table);
        for (int i = 0; i < sliders.Length; i++) {
            sliders[i].SetValueWithoutNotify(i == 2 ? FaceScaleSlider.ToPosition(draft.scale) : Axis(i) * 100f);
            values[i].text = (Axis(i) * 100f).ToString("0.0") + "%";
        }
        if (FixedFacePack.IsFixed(pack)) {
            FixedFacePack.ApplyPreview(preview, pack, tileId, table);
            refreshing = false; return;
        }
        preview.tileId = tileId; preview.packId = null;
        if (table) {
            var config = ConfigManager.Instance;
            var background = config != null && config.UseTableFaceBackground && !config.TableFaceUseSolidColor
                ? TileFaceResolver.LoadTableBackground() : null;
            preview.ApplyTable(TileFaceResolver.PreviewTable(tileId), background, TileFaceResolver.TablePreviewBaseColor, false,
                TileFaceResolver.TableImageScaleFor(tileId, applyWhiteDragonFaceSetting: false));
        } else preview.Apply(TileFaceResolver.PreviewHand(tileId), TileFaceResolver.LoadHandBackground(), false);
        refreshing = false;
    }
    public static string PackName(string id) => id == FixedFacePack.Hongque ? "虹雀麻将牌(标准)"
        : id == FixedFacePack.Expanded ? "拓展麻将牌(标准)" : id == TilePackIds.PackOfficial ? "标准（雪枫）"
        : id == TilePackIds.PackFluffy ? "Fluffy" : id == TilePackIds.PackHkMahjong ? "香港" : TilePackLibrary.DisplayName(id);
    static string TileName(int id) {
        if (id == 105 || id == 205 || id == 305) return "赤五" + (id == 105 ? "万" : id == 205 ? "饼" : "索");
        if (id >= 11 && id <= 39 && id % 10 >= 1 && id % 10 <= 9)
            return "一二三四五六七八九"[id % 10 - 1] + (id / 10 == 1 ? "万" : id / 10 == 2 ? "饼" : "索");
        if (id >= 41 && id <= 47) return new[] { "东", "南", "西", "北", "中", "白", "发" }[id - 41];
        if (id >= 51 && id <= 58) return new[] { "梅", "兰", "竹", "菊", "春", "夏", "秋", "冬" }[id - 51];
        return id == 2 ? "纯白白板" : "牌 " + id;
    }
}
