#if UNITY_EDITOR
using System;
using System.IO;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Serialization;

// Editor-only authoring; runtime controls bind the serialized scene objects.
public partial class CardBackConfigPanel
{
    public void BakePreviewLayout()
    {
        if (previewArtwork == null)
        {
            TileTextureLayout.FitRenderedCardPreview(previewImage.rectTransform, tilePreviewPrefab);
            var artwork = new GameObject("CardBackArtwork", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            artwork.layer = previewImage.gameObject.layer;
            artwork.transform.SetParent(previewImage.transform, false);
            previewArtwork = artwork.GetComponent<Image>();
            previewArtwork.raycastTarget = false;
            // Match the back shader: stretch the artwork, composite alpha over the base color.
            previewArtwork.preserveAspect = false;
            previewArtwork.rectTransform.anchorMin = Vector2.zero;
            previewArtwork.rectTransform.anchorMax = Vector2.one;
            previewArtwork.rectTransform.offsetMin = previewArtwork.rectTransform.offsetMax = Vector2.zero;
        }
    }

    public void ApplyEditorDroppedTexture(Texture2D source)
    {
        if (source == null) return;

        byte[] bytes = SceneConfigTextureCapture.EncodePng(source);
        ApplyCardBackPng(bytes);
        SceneConfigUi.ShowTip("牌背图片已应用");
    }
}
#endif
