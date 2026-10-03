using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>内置和上传底图共用选择器；官方选项不可改名或删除。</summary>
public sealed class HandSurfaceStylePicker : MonoBehaviour
{
    public const string PrefabPath = "UI/HandSurfaceStylePicker";
    [SerializeField] private TMP_Dropdown dropdown;
    [SerializeField] private Button renameButton, deleteButton;
    private readonly List<string> ids = new List<string>();
    private readonly List<int> builtinIndices = new List<int>();
    private Action<int, string> selected;
    private Action rename, delete;
    private bool bound;

    public void Bind(TMP_FontAsset font, Action<int, string> onSelected, Action onRename, Action onDelete)
    {
        selected = onSelected; rename = onRename; delete = onDelete;
        foreach (var label in GetComponentsInChildren<TMP_Text>(true))
        {
            if (font != null) label.font = font;
            label.richText = false;
        }
        if (bound) return;
        bound = true;
        dropdown.onValueChanged.AddListener(index => {
            if (index >= 0 && index < ids.Count) selected?.Invoke(builtinIndices[index], ids[index]);
        });
        renameButton.onClick.AddListener(() => rename?.Invoke());
        deleteButton.onClick.AddListener(() => delete?.Invoke());
    }

    public void RefreshSelection(string path, bool custom, bool back)
    {
        dropdown.Hide(); ids.Clear(); builtinIndices.Clear();
        var names = new List<string>();
        void AddBuiltin(int builtin) {
            ids.Add(HandSurfaceStyles.ResourcePath(builtin, back));
            names.Add(HandSurfaceStyles.DisplayName(builtin));
            builtinIndices.Add(builtin);
        }
        for (int i = 0; i < HandSurfaceStyles.Count; i++)
            if (i != HandSurfaceStyles.ClassicIndex) AddBuiltin(i);
        foreach (var entry in HandSurfaceLibrary.GetEntries(back)) { ids.Add(entry.id); names.Add(entry.name); builtinIndices.Add(-1); }
        // 经典始终放在列表末尾；旧上传图片的临时选项也排在经典之前。
        if (custom && !ids.Contains(path)) { ids.Add(path); names.Add("原有上传（待迁移）"); builtinIndices.Add(-1); }
        AddBuiltin(HandSurfaceStyles.ClassicIndex);
        int index = ids.IndexOf(path ?? "");
        dropdown.ClearOptions(); dropdown.AddOptions(names);
        dropdown.SetValueWithoutNotify(Mathf.Max(0, index)); dropdown.RefreshShownValue();
        bool editable = custom && HandSurfaceLibrary.Find(path) != null;
        renameButton.interactable = deleteButton.interactable = editable;
        dropdown.interactable = HandSurfaceLibrary.Ready;
    }

    private void OnDisable() => dropdown?.Hide();
    private void OnDestroy() { selected = null; rename = delete = null; }
}
