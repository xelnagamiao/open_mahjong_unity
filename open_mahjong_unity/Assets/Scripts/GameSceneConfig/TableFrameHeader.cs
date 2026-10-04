using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Events;

/// <summary>Frame controls share the cloth header layout and never create full-size color textures.</summary>
public sealed partial class TableFrameHeader : MonoBehaviour
{
    static readonly Color Ink = new Color32(40,58,78,255);
    static readonly Color Dark = new Color32(38,44,56,255);
    static readonly Color Accent = new Color32(241,183,121,255);
    [SerializeField] RectTransform header, gallery, title;
    [SerializeField] RectTransform[] rows = new RectTransform[4];
    [SerializeField] RectTransform[] captions = new RectTransform[4];
    [SerializeField] RectTransform[] controls = new RectTransform[4];
    [SerializeField] TMP_FontAsset font;
    [SerializeField] TableSurfaceColorEditor colorEditor;
    [SerializeField] Slider shadow, highlight;
    [SerializeField] TMP_Text shadowValue, highlightValue;
    [SerializeField] TMP_Dropdown outline;
    bool syncing, changed, ready;
    public bool HasBakedUi => header != null && colorEditor != null && shadow != null && highlight != null
        && shadowValue != null && highlightValue != null && outline != null;

    void Bind()
    {
        if (ready || !HasBakedUi || !Application.isPlaying) return;
        ready = true;
        shadow.onValueChanged.AddListener(v => Edit(() => ConfigManager.Instance.SetTableFrameShadowIntensity(v / 100)));
        highlight.onValueChanged.AddListener(v => Edit(() => ConfigManager.Instance.SetTableFrameHighlightIntensity(v / 100)));
        outline.onValueChanged.AddListener(v => Edit(() => ConfigManager.Instance.SetTableContactOutlineEnabled(v == 1)));
    }

    void Edit(UnityAction action)
    {
        if(syncing || SceneConfigColorUi.IsLayoutRefresh || ConfigManager.Instance==null) return;
        action(); changed=true;
        Desktop.Instance?.RefreshAppearance();
        RefreshSelection();
    }

    public void RefreshSelection()
    {
        Bind();
        if(!ready) return;
        var config=ConfigManager.Instance;
        if(config==null) return;
        var selected=config.GetSelectedTableEdge();
        syncing=true;
        colorEditor.RefreshSelection();
        bool layered=!selected.isCustom || TableSurfaceColorLibrary.IsId(selected.path);
        shadow.interactable=highlight.interactable=layered;
        shadow.SetValueWithoutNotify(config.GetTableFrameShadowIntensity()*100);
        highlight.SetValueWithoutNotify(config.GetTableFrameHighlightIntensity()*100);
        shadowValue.text=Mathf.RoundToInt(shadow.value)+"%";
        highlightValue.text=Mathf.RoundToInt(highlight.value)+"%";
        if(outline!=null) {outline.interactable=layered;outline.SetValueWithoutNotify(config.GetTableContactOutlineEnabled()?1:0);}
        syncing=false;
    }

    void OnEnable() { RefreshSelection(); }
    void OnDisable() { if(outline!=null)outline.Hide(); Flush(); }
    void OnApplicationPause(bool paused) {if(paused)Flush();}
    void OnApplicationQuit()=>Flush();
    void Flush() {if(changed) {PlayerPrefs.Save();changed=false;}}
}
