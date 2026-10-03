using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

public partial class CardFaceBackgroundPanel
{
    sealed class PendingHandImage { public byte[] background, back; public string name; public bool zip; }
    readonly Queue<PendingHandImage> pendingHandImages = new Queue<PendingHandImage>();
    HandSurfaceNameDialog handNameDialog;
    MessagePrefab handDeleteConfirmation;
    TMP_FontAsset HandFont => uploadHandBgButton.GetComponentInChildren<TMP_Text>(true)?.font;

    void QueueHandImage(byte[] bytes, string fileName, bool back)
    {
        if (bytes == null || bytes.Length == 0 || bytes.Length > UnityAssetIdb.MaxImageBytes - 4096)
        { SceneConfigUi.ShowTip("图片为空或超过 8MB，请压缩后上传"); return; }
        pendingHandImages.Enqueue(new PendingHandImage { background = back ? null : bytes, back = back ? bytes : null, name = fileName });
        HandSurfaceLibrary.EnsureReady(() => { if (this != null && isActiveAndEnabled) ShowNextHandImage(); });
    }
    void QueueHandImport(byte[] background, byte[] back, string fileName)
    {
        pendingHandImages.Enqueue(new PendingHandImage { background = background, back = back, name = fileName, zip = true });
        HandSurfaceLibrary.EnsureReady(() => { if (this != null && isActiveAndEnabled) ShowNextHandImage(); });
    }
    void ShowNextHandImage()
    {
        if (handNameDialog != null || pendingHandImages.Count == 0) return;
        var pending = pendingHandImages.Dequeue();
        Texture2D texture = UnityAssetIdb.ToTexture(pending.background ?? pending.back);
        if (texture == null) { SceneConfigUi.ShowTip("无法读取图片，请使用 PNG 或 JPG"); ShowNextHandImage(); return; }
        bool paired = pending.background != null && pending.back != null;
        string title = paired ? "导入手牌背景与牌背" : pending.back != null ? "上传手牌牌背" : "上传手牌背景";
        string suggested = pending.zip ? HandSurfaceLibrary.ImportName(pending.name)
            : HandSurfaceLibrary.SuggestedName(pending.name, pending.back != null);
        handNameDialog = HandSurfaceNameDialog.Open(transform, HandFont, title, suggested, texture, (name, close, error) =>
                HandSurfaceLibrary.SaveImport(pending.background, pending.back, name, (background, back) =>
                {
                    if (this != null && isActiveAndEnabled)
                    {
                        bool selected = true;
                        if (background != null) selected &= CardBackManager.SelectUploadedHandSurface(background.id, false);
                        if (back != null) selected &= CardBackManager.SelectUploadedHandSurface(back.id, true, paired);
                        RefreshHandPreviews(); SceneConfigUi.ShowTip(selected ? "已保存并选中“" + name + "”" : "图片已保存，请从列表中选择");
                        close(); StartCoroutine(NextHandImageFrame());
                    }
                    else close();
                }, error), () => pendingHandImages.Clear());
    }
    IEnumerator NextHandImageFrame() { yield return null; if (isActiveAndEnabled) ShowNextHandImage(); }
    void RenameHandSurface(bool back)
    {
        if (handNameDialog != null || ConfigManager.Instance == null) return;
        var selection = back ? ConfigManager.Instance.GetSelectedHandBack() : ConfigManager.Instance.GetSelectedHandBackground();
        var entry = selection.isCustom ? HandSurfaceLibrary.Find(selection.path) : null;
        if (entry == null) return;
        handNameDialog = HandSurfaceNameDialog.Open(transform, HandFont, back ? "重命名手牌牌背" : "重命名手牌背景", entry.name,
            HandSurfaceLibrary.LoadTexture(entry.id, back), (name, close, error) => HandSurfaceLibrary.Rename(entry.id, name, () =>
            { close(); if (this != null && isActiveAndEnabled) RefreshHandPreviews(); }, error));
    }
    void DeleteHandSurface(bool back)
    {
        if (handDeleteConfirmation != null || ConfigManager.Instance == null || NotificationManager.Instance == null) return;
        var selection = back ? ConfigManager.Instance.GetSelectedHandBack() : ConfigManager.Instance.GetSelectedHandBackground();
        var entry = selection.isCustom ? HandSurfaceLibrary.Find(selection.path) : null;
        if (entry == null) return;
        handDeleteConfirmation = NotificationManager.Instance.ShowConfirmation(back ? "删除手牌牌背" : "删除手牌背景",
            "确定删除“" + entry.name + "”吗？\n正在使用时将恢复默认靛蓝，其他上传图片不受影响。", () =>
            {
                if (this == null || !isActiveAndEnabled) return;
                HandSurfaceLibrary.Delete(entry.id, () =>
                { if (this != null && isActiveAndEnabled) { RefreshHandPreviews(); SceneConfigUi.ShowTip("已删除“" + entry.name + "”"); } }, SceneConfigUi.ShowTip);
            }, "删除", "返回");
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
        if (handBgSprite != null) Destroy(handBgSprite);
        if (cardBackSprite != null) Destroy(cardBackSprite);
        if (tableBgSprite != null) Destroy(tableBgSprite);
    }
}
