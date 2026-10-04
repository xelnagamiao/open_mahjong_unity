using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>图片确认与重命名共用弹窗。只有存储成功后才修改选中项。</summary>
public sealed class HandSurfaceNameDialog : MonoBehaviour
{
    public const string PrefabPath = "UI/HandSurfaceNameDialog";
    [SerializeField] TMP_Text title, errorText;
    [SerializeField] TMP_InputField nameInput;
    [SerializeField] RawImage preview;
    [SerializeField] AspectRatioFitter previewAspect;
    [SerializeField] Button confirm, cancel;
    Action<string, Action, Action<string>> save;
    Action cancelled;
    Texture2D ownedTexture;
    bool saving, finished;

    public static HandSurfaceNameDialog Open(Transform owner, TMP_FontAsset font, string title, string initialName,
        Texture2D texture, Action<string, Action, Action<string>> save, Action cancelled = null)
    {
        var prefab = Resources.Load<HandSurfaceNameDialog>(PrefabPath);
        if (prefab == null) { if (texture != null) Destroy(texture); SceneConfigUi.ShowTip("缺少图片命名窗口"); return null; }
        var canvas = owner.GetComponentInParent<Canvas>();
        var dialog = Instantiate(prefab, canvas != null ? canvas.rootCanvas.transform : owner, false);
        dialog.transform.SetAsLastSibling(); dialog.save = save; dialog.cancelled = cancelled; dialog.ownedTexture = texture;
        foreach (var label in dialog.GetComponentsInChildren<TMP_Text>(true)) { if (font != null) label.font = font; label.richText = false; }
        dialog.title.text = title; dialog.nameInput.SetTextWithoutNotify(initialName); dialog.errorText.text = "";
        dialog.preview.texture = texture;
        dialog.preview.gameObject.SetActive(texture != null);
        if (texture != null) dialog.previewAspect.aspectRatio = texture.width / (float)texture.height;
        dialog.confirm.onClick.AddListener(dialog.Confirm);
        dialog.cancel.onClick.AddListener(dialog.Cancel);
        dialog.nameInput.onSubmit.AddListener(_ => dialog.Confirm());
        dialog.nameInput.onValueChanged.AddListener(_ => {
            dialog.RefreshValid();
            dialog.errorText.text = dialog.confirm.interactable ? "" : "请输入 1–24 个字的名称，不可换行";
        });
        dialog.RefreshValid(); dialog.nameInput.ActivateInputField();
        return dialog;
    }
    void RefreshValid() => confirm.interactable = !saving && HandSurfaceLibrary.TryName(nameInput.text, out _);
    void Confirm()
    {
        if (saving || finished || !HandSurfaceLibrary.TryName(nameInput.text, out var name)) return;
        saving = true; confirm.interactable = cancel.interactable = nameInput.interactable = false; errorText.text = "正在保存…";
        save?.Invoke(name, () => { if (this == null) return; finished = true; Destroy(gameObject); }, reason =>
        {
            if (this == null) return;
            saving = false; cancel.interactable = nameInput.interactable = true; errorText.text = reason; RefreshValid();
        });
    }
    public void Cancel()
    {
        if (saving || finished) return;
        finished = true; cancelled?.Invoke(); Destroy(gameObject);
    }
    public void CloseWithOwner()
    {
        if (!saving) Cancel(); else Destroy(gameObject);
    }
    void OnDestroy() { if (ownedTexture != null) Destroy(ownedTexture); save = null; cancelled = null; }
}
