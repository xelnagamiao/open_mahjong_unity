using TMPro;
using UnityEngine;

/// <summary>当前手牌背景的花纹排版草稿。滑条只预览，确认才写入持久化记录。</summary>
public sealed class HandFaceLayoutEditor : MonoBehaviour
{
    public const string PrefabPath = "UI/HandFaceLayoutEditor";
    [SerializeField] TMP_Text title, xValue, yValue, scaleValue, status;
    [SerializeField] UnityEngine.UI.Slider xSlider, ySlider, scaleSlider;
    [SerializeField] UnityEngine.UI.Button cancel, confirm, reset, nextSample;
    [SerializeField] UnityEngine.UI.Image[] backgrounds, artworks;
    static readonly int[] SampleIds = { 11, 21, 31, 19, 29, 39, 41, 45, 47, 46, 2, 105 };
    string backgroundPath;
    HandSurfaceLibrary.FaceLayout draft;
    bool initialized, saving, finished, previewDirty;
    int sampleOffset;

    public static HandFaceLayoutEditor Open(Transform owner, TMP_FontAsset font, string path, string name)
    {
        if (!HandSurfaceLibrary.CanEditFaceLayout(path)) return null;
        var prefab = Resources.Load<HandFaceLayoutEditor>(PrefabPath);
        if (prefab == null) { SceneConfigUi.ShowTip("缺少牌面位置窗口"); return null; }
        var canvas = owner.GetComponentInParent<Canvas>();
        var editor = Instantiate(prefab, canvas != null ? canvas.rootCanvas.transform : owner, false);
        editor.transform.SetAsLastSibling();
        foreach (var text in editor.GetComponentsInChildren<TMP_Text>(true)) { if (font != null) text.font = font; text.richText = false; }
        editor.backgroundPath = path;
        editor.title.text = "牌面位置 · " + name;
        editor.draft = HandSurfaceLibrary.GetFaceLayout(path);
        editor.initialized = true;
        editor.xSlider.onValueChanged.AddListener(value => { editor.draft.x = value / 100f; editor.UpdateDraft(); });
        editor.ySlider.onValueChanged.AddListener(value => { editor.draft.y = value / 100f; editor.UpdateDraft(); });
        editor.scaleSlider.onValueChanged.AddListener(value => { editor.draft.scale = value / 100f; editor.UpdateDraft(); });
        editor.cancel.onClick.AddListener(editor.Cancel);
        editor.confirm.onClick.AddListener(editor.Confirm);
        editor.reset.onClick.AddListener(() => { editor.draft = HandSurfaceLibrary.DefaultFaceLayout(editor.backgroundPath); editor.SyncControls(); editor.UpdateDraft(); });
        editor.nextSample.onClick.AddListener(() => { editor.sampleOffset = (editor.sampleOffset + 3) % SampleIds.Length; editor.RefreshPreview(); });
        editor.SyncControls(); editor.RefreshPreview();
        return editor;
    }
    void SyncControls()
    {
        xSlider.SetValueWithoutNotify(draft.x * 100f); ySlider.SetValueWithoutNotify(draft.y * 100f);
        scaleSlider.SetValueWithoutNotify(draft.scale * 100f); RefreshValues();
    }
    void RefreshValues()
    {
        xValue.text = (draft.x * 100f).ToString("0.#") + "%";
        yValue.text = (draft.y * 100f).ToString("0.#") + "%";
        scaleValue.text = (draft.scale * 100f).ToString("0.#") + "%";
    }
    void UpdateDraft()
    {
        if (saving || finished) return;
        draft = draft.Normalized; previewDirty = true; status.text = "";
        RefreshValues(); RefreshPreview();
    }
    void LateUpdate()
    {
        if (!previewDirty || saving || finished) return;
        previewDirty = false; HandSurfaceLibrary.PreviewFaceLayout(backgroundPath, draft);
    }
    void RefreshPreview()
    {
        if (!initialized) return;
        for (int i = 0; i < backgrounds.Length; i++) {
            int id = SampleIds[(sampleOffset + i) % SampleIds.Length];
            backgrounds[i].sprite = TileFaceResolver.LoadHandBackground();
            backgrounds[i].preserveAspect = true;
            artworks[i].sprite = TileFaceResolver.PreviewHand(id);
            artworks[i].preserveAspect = true;
            TileFaceFit.ApplyHandArtwork(artworks[i], backgrounds[i], id, draft);
        }
    }
    void OnRectTransformDimensionsChange() { if (initialized) RefreshPreview(); }
    void Confirm()
    {
        if (saving || finished) return;
        saving = true; previewDirty = false; SetInteractable(false); status.text = "正在保存…";
        HandSurfaceLibrary.SaveFaceLayout(backgroundPath, draft, () => {
            if (this == null) return;
            finished = true; Destroy(gameObject);
        }, reason => {
            if (this == null) return;
            saving = false; SetInteractable(true); status.text = reason;
        });
    }
    void SetInteractable(bool value)
    {
        cancel.interactable = confirm.interactable = reset.interactable = nextSample.interactable = value;
        xSlider.interactable = ySlider.interactable = scaleSlider.interactable = value;
    }
    public void Cancel()
    {
        if (saving || finished) return;
        finished = true; previewDirty = false;
        HandSurfaceLibrary.CancelFaceLayoutPreview(backgroundPath); Destroy(gameObject);
    }
    public void CloseWithOwner() { if (saving) Destroy(gameObject); else Cancel(); }
    void OnDisable()
    {
        previewDirty = false;
        if (initialized && !finished) HandSurfaceLibrary.CancelFaceLayoutPreview(backgroundPath);
    }
}
