using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Image/name choices for one 3D surface, using the face-design option component.</summary>
public sealed class CardDesignImagePicker : MonoBehaviour
{
    public bool back;
    public RectTransform options;
    public Button optionTemplate, solidButton, uploadButton, renameButton, deleteButton;
    public RectTransform actions, colorControl;
    public Button restoreImageButton, restoreColorButton, adjustColorButton;
    public CardDesignSurfaceLayout layout;
    public GameObject edgeControls;
    public const float EdgeControlsHeight = 166;
    public float EdgeExpansion { get; set; }
    public float TotalHeight => PreferredHeight + (edgeControls != null && edgeControls.activeSelf ? (EdgeControlsHeight + 12) * EdgeExpansion : 0);
    public void PlaceEdgeControls() {
        if (edgeControls != null) CardFaceLayoutPresentation.Place((RectTransform)edgeControls.transform, 0, PreferredHeight + 12 * EdgeExpansion, 610, EdgeControlsHeight * EdgeExpansion);
    }
    public CardDesignColorControls colors;
    readonly Dictionary<string, CardDesignImageOption> buttons = new Dictionary<string, CardDesignImageOption>();
    List<CardDesignImageLibrary.Entry> entries = new List<CardDesignImageLibrary.Entry>();
    HandSurfaceNameDialog nameDialog;
    MessagePrefab deleteConfirmation;
    bool refreshing;
    float nextSelection;
    string lastPath;
    bool actionSolid;
    float actionTop = -1;
    public float PreferredHeight { get; private set; } = 254;

    void Awake()
    {
        solidButton.onClick.AddListener(() => { CardDesignImageLibrary.SelectSolid(back); RefreshSavedUi(); });
        renameButton.onClick.AddListener(Rename);
        deleteButton.onClick.AddListener(ConfirmDelete);
        if (restoreImageButton != null) restoreImageButton.onClick.AddListener(ResetImage);
        if (restoreColorButton != null) restoreColorButton.onClick.AddListener(ResetColor);
    }
    void OnEnable()
    {
        CardDesignImageLibrary.Changed += Refresh;
        TileFaceResolver.OnPackChanged += Refresh;
        UnityAssetIdb.EnsureReady(() => { if (this != null && isActiveAndEnabled) Refresh(); });
    }
    void OnDisable()
    {
        CardDesignImageLibrary.Changed -= Refresh;
        TileFaceResolver.OnPackChanged -= Refresh;
        if (nameDialog != null) nameDialog.CloseWithOwner();
        if (deleteConfirmation != null) deleteConfirmation.CloseMessage();
    }
    void LateUpdate()
    {
        if (Time.unscaledTime < nextSelection) return;
        nextSelection = Time.unscaledTime + .15f;
        string path = CardDesignImageLibrary.CurrentPath(back);
        if (path != lastPath) Refresh(); else RefreshSelection();
    }
    public void Refresh()
    {
        if (refreshing || !Application.isPlaying) return;
        refreshing = true;
        try
        {
            entries = CardDesignImageLibrary.GetEntries(back);
            var retained = new HashSet<string>();
            for (int i = 0; i < entries.Count; i++)
            {
                var entry = entries[i]; retained.Add(entry.id);
                if (!buttons.TryGetValue(entry.id, out var option))
                {
                    var button = Instantiate(optionTemplate, options, false);
                    button.name = "Image_" + entry.id; button.onClick = new Button.ButtonClickedEvent();
                    string id = entry.id;
                    button.onClick.AddListener(() => { CardDesignImageLibrary.Select(entries.Find(e => e.id == id)); RefreshSavedUi(); });
                    option = button.GetComponent<CardDesignImageOption>(); buttons.Add(entry.id, option);
                }
                option.SetEntry(entry);
                PlaceChoice((RectTransform)option.transform, i + 1, false);
                option.gameObject.SetActive(true);
            }
            var removed = new List<string>();
            foreach (var pair in buttons) if (!retained.Contains(pair.Key)) { pair.Value.gameObject.SetActive(false); Destroy(pair.Value.gameObject); removed.Add(pair.Key); }
            foreach (string id in removed) buttons.Remove(id);
            PlaceChoice((RectTransform)solidButton.transform, 0, false);
            PlaceChoice((RectTransform)uploadButton.transform, entries.Count + 1, true);
            float height = CardFaceLayoutPresentation.ChoiceGridHeight(entries.Count + 1, true);
            options.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, height);
            lastPath = CardDesignImageLibrary.CurrentPath(back);
            RefreshSelection();
            layout.RefreshLayout();
        }
        finally { refreshing = false; }
    }
    static void PlaceChoice(RectTransform rect, int index, bool add)
        => CardFaceLayoutPresentation.PlaceChoice(rect, index, add);
    void RefreshSelection()
    {
        bool solid = CardDesignImageLibrary.IsSolid(back);
        solidButton.GetComponent<CardDesignImageOption>().SetSolid(back ? colors.back.PickerColor : colors.background.PickerColor);
        solidButton.GetComponent<CardDesignImageOption>().SetSelected(solid);
        string path = CardDesignImageLibrary.CurrentPath(back);
        foreach (var entry in entries) if (buttons.TryGetValue(entry.id, out var option)) option.SetSelected(!solid && entry.path == path);
        bool selected = !solid && entries.Exists(e => e.path == path);
        renameButton.interactable = deleteButton.interactable = selected;
        LayoutActions(solid);
    }
    public void LayoutActions(bool solid, bool force = false)
    {
        if (restoreColorButton == null || restoreImageButton == null || adjustColorButton == null) return;
        float top = 54 + options.rect.height + 12;
        if (!force && actionSolid == solid && Mathf.Approximately(actionTop, top)) return;
        actionSolid = solid; actionTop = top;
        renameButton.gameObject.SetActive(!solid); deleteButton.gameObject.SetActive(!solid);
        var controls = solid ? new[] { restoreColorButton, restoreImageButton, adjustColorButton }
            : new[] { renameButton, deleteButton, restoreImageButton, restoreColorButton, adjustColorButton };
        for (int i = 0; i < controls.Length; i++)
        {
            controls[i].gameObject.SetActive(true);
            CardFaceLayoutPresentation.Place((RectTransform)controls[i].transform, i % 3 * 206, i / 3 * 64, 198, 56);
        }
        float height = (controls.Length - 1) / 3 * 64 + 56;
        CardFaceLayoutPresentation.Place(actions, 0, top, 610, height);
        PreferredHeight = top + height + 12;
        if (layout != null) layout.RefreshLayout();
    }
    void ResetColor()
    {
        if (back) colors.back.ApplyPickerColor(ConfigManager.DefaultCardBackColor);
        else colors.background.ApplyPickerColor(ConfigManager.DefaultTableFaceColor);
        RefreshSavedUi();
    }
    void RefreshSavedUi()
    {
        colors.back.ReloadSaved();
        colors.background.RefreshSolidColorUi();
        Refresh();
    }
    public void Import(byte[] bytes, string filename)
    {
        if (nameDialog != null) return;
        if (bytes == null || bytes.Length == 0 || bytes.Length > UnityAssetIdb.MaxImageBytes) { SceneConfigUi.ShowTip("图片大小需在 8MB 以内"); return; }
        var image = UnityAssetIdb.ToTexture(bytes);
        if (image == null) { SceneConfigUi.ShowTip("图片无法读取"); return; }
        if (image.width > 4096 || image.height > 4096) { Destroy(image); SceneConfigUi.ShowTip("图片单边不能超过 4096 像素"); return; }
        nameDialog = HandSurfaceNameDialog.Open(transform, uploadButton.GetComponentInChildren<TMP_Text>(true).font,
            back ? "保存3D牌背" : "保存3D牌面背景", CardDesignImageLibrary.SuggestedName(filename, back), image,
            (name, close, error) => CardDesignImageLibrary.SaveNew(bytes, name, back,
                entry => { CardDesignImageLibrary.Select(entry); close(); if (this != null) RefreshSavedUi(); }, error));
    }
    void Rename()
    {
        var entry = entries.Find(e => e.path == CardDesignImageLibrary.CurrentPath(back));
        if (entry == null || nameDialog != null) return;
        nameDialog = HandSurfaceNameDialog.Open(transform, uploadButton.GetComponentInChildren<TMP_Text>(true).font, "重命名图片", entry.name, null,
            (name, close, error) => CardDesignImageLibrary.Rename(entry.id, name, close, error));
    }
    public void ConfirmDelete()
    {
        var entry = entries.Find(e => e.path == CardDesignImageLibrary.CurrentPath(back));
        if (entry == null || deleteConfirmation != null || NotificationManager.Instance == null) return;
        deleteConfirmation = NotificationManager.Instance.ShowConfirmation("移除图片选项", "确定移除“" + entry.name + "”吗？",
            () => { CardDesignImageLibrary.SelectSolid(back, true); CardDesignImageLibrary.Remove(entry.id); if (this != null) RefreshSavedUi(); }, "移除", "返回");
    }
    public void ResetImage() { CardDesignImageLibrary.SelectSolid(back, true); RefreshSavedUi(); }
}
