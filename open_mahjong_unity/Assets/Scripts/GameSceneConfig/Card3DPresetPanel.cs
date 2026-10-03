using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>Header presets + an ordered, multi-select rotation page over the existing design tabs.</summary>
public sealed partial class Card3DPresetPanel : MonoBehaviour
{
    public const string AlternatingHint = "点击确定保存轮转快照，第一局使用第一副，第二局起交替轮转；编辑其他预设不影响已确认配置";
    public const string RandomHint = "点击确定保存轮转快照，第一局使用第一副，随后随机轮转；选择的卡牌套数越多内存开销则会越大";
    [SerializeField] private SceneConfigPanel owner;
    private Card3DPresetLibrary library;
    [SerializeField] private TMP_FontAsset font;
    [SerializeField] private TMP_Dropdown presets, rotation;
    [SerializeField] private Button add, remove, confirmRotation, backRotation, createCancel, createSave;
    private MessagePrefab deleteConfirmation;
    [SerializeField] private RectTransform rotationPage, listContent, createModal;
    [SerializeField] private TMP_Text hint;
    [SerializeField] private TMP_InputField presetName;
    [SerializeField] private Toggle rowTemplate;
    [SerializeField] private EventTrigger rotationTrigger;
    private bool bound;
    private string catalogSignature;
    private readonly List<string> ids = new List<string>();
    private readonly Dictionary<string, Toggle> toggles = new Dictionary<string, Toggle>();
    private readonly Dictionary<string, TMP_Text> badges = new Dictionary<string, TMP_Text>();
    private static readonly Color TextColor = new Color32(238,243,250,255);

    public void Initialize(SceneConfigPanel sceneOwner)
    {
        if (bound || !HasBakedUi) return;
        bound = true; owner = sceneOwner;
        presets.onValueChanged.AddListener(index => { if (library != null && index >= 0 && index < ids.Count) { string id = ids[index]; CancelRotation(); library.Select(id); } });
        rotation.onValueChanged.AddListener(index => {
            if (library == null) return;
            library.SetRotation((Card3DRotationMode)index); ShowRotation(index != 0);
        });
        add.onClick.AddListener(ShowCreate);
        remove.onClick.AddListener(ConfirmDelete);
        confirmRotation.onClick.AddListener(ConfirmRotation);
        backRotation.onClick.AddListener(CancelRotation);
        createCancel.onClick.AddListener(() => createModal.gameObject.SetActive(false));
        createSave.onClick.AddListener(SaveCreated);
        presetName.onSubmit.AddListener(_ => SaveCreated());
        rotationTrigger.triggers.Clear();
        foreach (var type in new[] { EventTriggerType.PointerClick, EventTriggerType.Submit }) {
            var entry = new EventTrigger.Entry { eventID = type };
            entry.callback.AddListener(_ => { if (library != null && library.Data.rotation != Card3DRotationMode.None) ShowRotation(true); });
            rotationTrigger.triggers.Add(entry);
        }
        presets.interactable = rotation.interactable = add.interactable = remove.interactable = false;
        BindLibrary();
    }
    public bool HasBakedUi => presets && rotation && add && remove && confirmRotation && backRotation && createCancel && createSave
        && rotationPage && listContent && createModal && hint && presetName && rowTemplate && rotationTrigger;
    private void Update()
    {
        if (presets == null) return;
        BindLibrary();
    }
    private void OnEnable() { if (Application.isPlaying) Initialize(owner); BindLibrary(); library?.BeginEditing(); }
    private void OnDisable()
    {
        CancelRotation();
        library?.Flush(); presets?.Hide(); rotation?.Hide();
        if (createModal != null) createModal.gameObject.SetActive(false);
    }
    private void OnDestroy()
    {
        if (library == null) return;
        library.Changed -= Refresh; library.AppearanceApplied -= RefreshAppearance;
    }
    private void BindLibrary()
    {
        if (presets == null || library != null || !Application.isPlaying) return;
        library = Card3DPresetLibrary.Ensure(ConfigManager.Instance);
        if (library == null) return;
        library.BeginEditing();
        library.Changed += Refresh; library.AppearanceApplied += RefreshAppearance;
        Refresh(); ShowRotation(library.Ready && library.Data.rotation != Card3DRotationMode.None);
    }
    private void Refresh()
    {
        bool ready = library != null && library.Ready;
        presets.interactable = rotation.interactable = add.interactable = ready;
        remove.interactable = ready && !Card3DPresetData.IsBuiltin(library.Data.selectedId);
        if (!ready) return;
        bool initialLoad = catalogSignature == null;
        RenderData(library.Data);
        if (initialLoad) ShowRotation(library.Data.rotation != Card3DRotationMode.None);
        if (library.Data.rotation == Card3DRotationMode.None) ShowRotation(false);
    }
    private void RefreshAppearance()
    {
        if (owner == null) return;
        foreach (var panel in owner.GetComponentsInChildren<CardBackConfigPanel>(true)) if (panel.isActiveAndEnabled) panel.ReloadSaved();
        foreach (var panel in owner.GetComponentsInChildren<CardEdgePanel>(true)) if (panel.isActiveAndEnabled) panel.ReloadSaved();
        foreach (var panel in owner.GetComponentsInChildren<CardFaceBackgroundPanel>(true)) if (panel.isActiveAndEnabled) panel.RefreshSolidColorUi();
    }
    // Also used by isolated layout checks: rendering data never writes settings.
    public void RenderData(Card3DPresetData data)
    {
        var all = data.All(); string signature = string.Join("|", all.Select(p => p.id + ":" + p.name));
        if (signature != catalogSignature) {
            presets.Hide(); ids.Clear(); presets.ClearOptions();
            ids.AddRange(all.Select(p => p.id)); presets.AddOptions(all.Select(p => p.name).ToList());
            BuildRows(all); catalogSignature = signature;
        }
        presets.SetValueWithoutNotify(Mathf.Max(0, ids.IndexOf(data.selectedId)));
        remove.interactable = !Card3DPresetData.IsBuiltin(data.selectedId);
        rotation.SetValueWithoutNotify((int)data.rotation);
        string activeMode = data.confirmedRotation == Card3DRotationMode.None ? "无轮转" : data.confirmedRotation == Card3DRotationMode.Alternating ? "两副轮转" : "随机轮转";
        hint.text = (data.rotation == Card3DRotationMode.Random ? RandomHint : AlternatingHint)
            + "\n" + (data.DraftMatchesConfirmed() ? "已确认：" : "有未确认修改，当前生效：") + activeMode;
        bool full = data.rotation == Card3DRotationMode.Alternating && data.rotationIds.Count >= 2;
        foreach (string id in ids) {
            int order = data.rotationIds.IndexOf(id);
            toggles[id].SetIsOnWithoutNotify(order >= 0); toggles[id].interactable = !full || order >= 0;
            badges[id].text = order < 0 ? "" : order == 0 ? "第一副" : order == 1 ? "第二副" : "第" + (order + 1) + "副";
        }
        bool valid = data.rotation == Card3DRotationMode.Alternating ? data.rotationIds.Count == 2
            : data.rotation == Card3DRotationMode.Random && data.rotationIds.Count > 0;
        confirmRotation.interactable = valid;
    }
    private void BuildRows(List<Card3DPreset> all)
    {
        for (int i = listContent.childCount-1; i >= 0; i--) { var child = listContent.GetChild(i); child.gameObject.SetActive(false); if (Application.isPlaying) Destroy(child.gameObject); else DestroyImmediate(child.gameObject); }
        toggles.Clear(); badges.Clear();
        int index = 0;
        foreach (var preset in all) {
            string id = preset.id;
            var toggle = Instantiate(rowTemplate, listContent, false);
            var row = (RectTransform)toggle.transform;
            row.name = "Preset_" + id;
            row.anchoredPosition = new Vector2(0,-index++*70); row.sizeDelta = new Vector2(0,60);
            row.Find("Name").GetComponent<TMP_Text>().text = preset.name;
            var badge = row.Find("Order").GetComponent<TMP_Text>();
            toggle.onValueChanged.AddListener(on => {
                if (library == null || !library.SetIncluded(id,on)) Refresh();
            });
            toggles[id] = toggle; badges[id] = badge;
            row.gameObject.SetActive(true);
        }
        listContent.sizeDelta = new Vector2(0,Mathf.Max(0,all.Count*70-10));
    }
    private void ShowRotation(bool show)
    {
        if (show) library?.BeginRotationEdit();
        if (rotationPage != null) rotationPage.gameObject.SetActive(show);
    }
    private void CancelRotation() { library?.CancelRotationEdit(); ShowRotation(false); }
    private void ConfirmDelete()
    {
        if (library == null || !library.Ready || deleteConfirmation != null || NotificationManager.Instance == null) return;
        var preset = library.Data.Find(library.Data.selectedId);
        if (preset == null || Card3DPresetData.IsBuiltin(preset.id)) return;
        string id = preset.id;
        deleteConfirmation = NotificationManager.Instance.ShowConfirmation("删除卡牌预设",
            "确定删除“" + preset.name + "”吗？\n该预设将退出轮转；两副轮转不足两副时会关闭轮转。\n已上传的图片会保留。",
            () => { if (this != null && isActiveAndEnabled) { CancelRotation(); library.Remove(id); } }, "删除", "返回");
    }
    private void ConfirmRotation()
    {
        if (library == null || !library.Ready || !confirmRotation.interactable) return;
        if (!library.ConfirmRotation()) return;
        ShowRotation(false);
        SceneConfigUi.ShowTip("卡牌轮转设置已确认");
    }
    private void ShowCreate()
    {
        if (library == null || !library.Ready) return;
        CancelRotation();
        int number = 1; var names = new HashSet<string>(library.Data.All().Select(p=>p.name));
        while (names.Contains("卡牌预设 " + number)) number++;
        presetName.SetTextWithoutNotify("卡牌预设 " + number);
        createModal.SetAsLastSibling(); createModal.gameObject.SetActive(true); presetName.ActivateInputField();
    }
    private void SaveCreated()
    {
        if (library != null && library.Add(presetName.text)) createModal.gameObject.SetActive(false);
    }
}
