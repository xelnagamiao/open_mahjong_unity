using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Shared create/preview/confirm flow. Draft colors never write the current selection.</summary>
public sealed partial class TableSurfaceColorEditor : MonoBehaviour
{
    [SerializeField] TableSurfacePanel owner;
    [SerializeField] TMP_Dropdown mode;
    [SerializeField] TMP_FontAsset font;
    [SerializeField] RectTransform modal;
    [SerializeField] Slider[] channels = new Slider[4];
    [SerializeField] TMP_Text[] values = new TMP_Text[4];
    [SerializeField] Button back, confirm;
    [SerializeField] Image swatch;
    [SerializeField] TMP_InputField nameInput;
    Color rgb;
    float brightness;
    bool editing, saving, syncing, bound;
    int session;
    static readonly Color Ink = new Color32(40,58,78,255);
    public static TableSurfaceColorEditor Ensure(TableSurfacePanel panel)
    {
        var editor = panel.GetComponent<TableSurfaceColorEditor>();
#if UNITY_EDITOR
        if (editor == null) editor = panel.gameObject.AddComponent<TableSurfaceColorEditor>();
#endif
        if (editor != null) editor.owner = panel;
        return editor;
    }
    public bool HasBakedUi => owner != null && mode != null && modal != null && back != null && confirm != null
        && swatch != null && nameInput != null && channels.Length == 4 && System.Array.TrueForAll(channels, x => x != null)
        && values.Length == 4 && System.Array.TrueForAll(values, x => x != null);
    void OnEnable() { if (Application.isPlaying) Bind(); }
    bool Bind()
    {
        if (bound) return true;
        if (!HasBakedUi) return false;
        bound = true;
        mode.onValueChanged.AddListener(index => { if (index == 1) Begin(); else RefreshSelection(); });
        back.onClick.AddListener(Cancel); confirm.onClick.AddListener(Confirm);
        nameInput.onSubmit.AddListener(_ => Confirm());
        for (int i = 0; i < channels.Length; i++) {
            int channel = i;
            channels[i].onValueChanged.AddListener(value => Edit(channel, value));
        }
        RefreshSelection();
        return true;
    }
    public void RefreshSelection()
    {
        if (mode == null || owner == null) return;
        var selected = owner.CurrentSelection;
        string caption = "图片模式";
        if (TableSurfaceColorLibrary.TryGet(selected.path, owner.IsClothSurface, out var entry)) caption=entry.name;
        else if (!selected.isCustom && owner.IsClothSurface && TableClothStyles.IsSolid(selected.path)) caption=TableClothStyles.DisplayName(selected.path);
        else if (!selected.isCustom && !owner.IsClothSurface && TableFrameStyles.IsSolid(selected.path)) caption=TableFrameStyles.DisplayName(selected.path);
        mode.options[0].text=caption;mode.SetValueWithoutNotify(0);mode.RefreshShownValue();mode.interactable=true;
    }
    public void Begin()
    {
        if (owner == null || ConfigManager.Instance == null || saving) return;
        if (!Bind()) { Debug.LogError("请先在编辑器烘焙场景设置 UI", this); return; }
        if (editing) { modal.SetAsLastSibling(); return; }
        if (!TableSurfaceColorLibrary.IsReady)
        {
            int version = ++session;
            TableSurfaceColorLibrary.EnsureReady(() => {
                if (this != null && isActiveAndEnabled && session == version) Begin();
            });
            return;
        }
        var selected = owner.CurrentSelection;
        brightness=0;
        if (TableSurfaceColorLibrary.TryGet(selected.path,owner.IsClothSurface,out var entry)) {rgb=entry.rgb;brightness=entry.brightness;}
        else rgb=owner.IsClothSurface ? TableClothStyles.DefaultColor(selected.path) : TableFrameStyles.SolidColor(selected.path);
        editing=true;session++;modal.SetAsLastSibling();modal.gameObject.SetActive(true);
        nameInput.SetTextWithoutNotify(TableSurfaceColorLibrary.GetSuggestedName(owner.IsClothSurface));
        Sync();Preview();RefreshSelection();
    }
    void Edit(int channel,float value)
    {
        if(!editing||saving||syncing||SceneConfigColorUi.IsLayoutRefresh)return;
        if(channel==3)brightness=value/100f;else rgb[channel]=value/255f;
        Sync();Preview();
    }
    void Sync()
    {
        syncing=true;
        for(int i=0;i<3;i++)SceneConfigColorUi.SyncChannel(channels[i],values[i],rgb[i]);
        SceneConfigColorUi.SyncBrightness(channels[3],values[3],brightness);
        swatch.color=ConfigManager.ApplyColorBrightness(rgb,brightness);
        syncing=false;
    }
    void Preview()=>Desktop.Instance?.PreviewSurfaceColor(owner.IsClothSurface,ConfigManager.ApplyColorBrightness(rgb,brightness));
    public void Cancel()
    {
        session++;
        if (!editing) return;
        editing=false;
        Desktop.Instance?.PreviewSurfaceColor(owner.IsClothSurface,null);
        if(modal!=null)modal.gameObject.SetActive(false);
        RefreshSelection();
    }
    void Confirm()
    {
        if(!editing||saving)return;
        string name = nameInput.text.Trim();
        if (string.IsNullOrWhiteSpace(name)) { SceneConfigUi.ShowTip("请输入预设名称"); nameInput?.ActivateInputField(); return; }
        saving=true;back.interactable=confirm.interactable=false;
        nameInput.interactable=false;
        foreach(var slider in channels)slider.interactable=false;
        int version=session;
        TableSurfaceColorLibrary.Create(owner.IsClothSurface,rgb,brightness,name,entry=>{
            if(this==null)return;
            SetSavingFinished();
            if(!editing||session!=version)return;
            owner.SelectColor(entry.id);Cancel();owner.ReloadColors(true);
        },error=>{
            if(this==null)return;
            SetSavingFinished();SceneConfigUi.ShowTip("保存颜色失败："+error);
        });
    }
    void SetSavingFinished(){saving=false;back.interactable=confirm.interactable=nameInput.interactable=true;foreach(var slider in channels)slider.interactable=true;}
    void OnDisable(){mode?.Hide();Cancel();}
    void OnDestroy(){Cancel();}
}
