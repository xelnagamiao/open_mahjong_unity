#if UNITY_EDITOR
using System;
using System.IO;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Serialization;

// Editor-only authoring; runtime controls bind the serialized scene objects.
public partial class CardFaceBackgroundPanel
{
    public void BakePreviewLayout() {
        BakeHandSurfaceControls();
        if (tableBgArtwork != null) return;
        RectTransform frame = tableBgPreview.rectTransform;
        TileTextureLayout.FitRenderedCardPreview(frame, tilePreviewPrefab);
        tableBgPreview.sprite = null;
        tableBgPreview.preserveAspect = false;
        GameObject artwork = new GameObject("TableBackgroundArtwork", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        artwork.layer = tableBgPreview.gameObject.layer;
        artwork.transform.SetParent(frame, false);
        tableBgArtwork = artwork.GetComponent<Image>();
        tableBgArtwork.raycastTarget = false;
        RectTransform content = tableBgArtwork.rectTransform;
        content.anchorMin = Vector2.zero;
        content.anchorMax = Vector2.one;
        content.offsetMin = content.offsetMax = Vector2.zero;
    }

    private static void EnsureHandSurfacePicker(ref HandSurfaceStylePicker picker, Image preview) {
        if (picker != null || preview == null) return;
        // Reuse a baked instance when present; old scenes instantiate the same authored prefab once.
        picker = preview.transform.parent.GetComponentInChildren<HandSurfaceStylePicker>(true);
        if (picker != null) return;
        var prefab = Resources.Load<HandSurfaceStylePicker>(HandSurfaceStylePicker.PrefabPath);
        if (prefab != null) picker = Instantiate(prefab, preview.transform.parent, false);
        else Debug.LogError("缺少手牌底图选择控件：" + HandSurfaceStylePicker.PrefabPath);
    }

    // 可单独补齐手牌入口，不触碰其他页面已经烘焙好的布局。
    public void BakeHandSurfaceControls() {
        EnsureHandSurfacePicker(ref handBgStyles, handBgPreview);
        EnsureHandSurfacePicker(ref handBackStyles, cardBackPreview);
        BakeHandHeaderButton(ref handLayoutButton, handBgPreview, "HandFaceLayoutButton", "牌面位置");
        BakeHandHeaderButton(ref handBackFollowButton, cardBackPreview, "HandBackFollowButton", "自动跟随");
        SceneConfigUi.SetButtonSelected(handBackFollowButton, true);
        var followLabel = handBackFollowButton.GetComponentInChildren<TMP_Text>(true);
        followLabel.color = Color.white;
        UnityEditor.PrefabUtility.RecordPrefabInstancePropertyModifications(handBackFollowButton);
        UnityEditor.PrefabUtility.RecordPrefabInstancePropertyModifications(handBackFollowButton.targetGraphic);
        UnityEditor.PrefabUtility.RecordPrefabInstancePropertyModifications(followLabel);
    }

    private static void BakeHandHeaderButton(ref Button button, Image preview, string objectName, string caption) {
        if (button != null) return;
        if (preview == null) throw new InvalidOperationException("缺少手牌预览，无法烘焙" + caption + "入口。");
        var parent = preview.transform.parent;
        var existing = parent.Find(objectName);
        if (existing != null) button = existing.GetComponent<Button>();
        if (button != null) return;
        var prefab = Resources.Load<Button>("UI/HandFaceLayoutButton");
        if (prefab == null) throw new InvalidOperationException("缺少手牌顶部按钮预制体。");
        var instance = (GameObject)UnityEditor.PrefabUtility.InstantiatePrefab(prefab.gameObject, parent);
        UnityEditor.Undo.RegisterCreatedObjectUndo(instance, "Bake hand header button");
        instance.name = objectName;
        instance.GetComponentInChildren<TMP_Text>(true).text = caption;
        button = instance.GetComponent<Button>();
        UnityEditor.PrefabUtility.RecordPrefabInstancePropertyModifications(instance);
        UnityEditor.PrefabUtility.RecordPrefabInstancePropertyModifications(instance.GetComponentInChildren<TMP_Text>(true));
    }

    /// <summary>编辑器拖拽入口：把拖入的图片应用到 3D 牌面背景。</summary>
    public void ApplyEditorDroppedTableBackground(Texture2D source) {
        if (source == null) return;
        byte[] png = SceneConfigTextureCapture.EncodePng(source);
        CardBackManager.PersistTableBackground(png);
        RefreshPreviews();
        SceneConfigUi.ShowTip("3D 牌面背景已应用");
    }
}
#endif
