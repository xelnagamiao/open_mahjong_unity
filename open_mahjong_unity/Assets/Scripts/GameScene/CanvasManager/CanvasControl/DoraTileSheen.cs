using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;
using System.Collections.Generic;

/// <summary>GPU animation shared by the image layers and their physical face frame.</summary>
[DisallowMultipleComponent]
public sealed class DoraTileSheen : MonoBehaviour {
    private static Material worldMaterial;
    private Material uiMaterial;
    private readonly Dictionary<Image, Material> originalMaterials = new Dictionary<Image, Material>();
    private static Mesh quad;
    private GameObject surface;
    private Image image;
    private Image body;

    public bool IsVisible => surface && surface.activeInHierarchy;

    private static Material SurfaceMaterial() {
        if (!worldMaterial) {
            worldMaterial = new Material(Resources.Load<Shader>("Materials/Tiles/DoraSheen")) {
                name = "Dora face sheen", hideFlags = HideFlags.HideAndDontSave
            };
            worldMaterial.SetInt("_ZTest", (int)CompareFunction.LessEqual);
            worldMaterial.SetFloat("_Strength", 1.635f);
        }
        return worldMaterial;
    }

    public static void Apply(Transform card, int tile) {
        var sheen = card.GetComponent<DoraTileSheen>();
        bool visible = RiichiDoraHints.IsDora(tile);
        if (!visible) {
            if (sheen) { sheen.RestoreImages(); if (sheen.surface) sheen.surface.SetActive(false); }
            return;
        }
        if (!sheen) sheen = card.gameObject.AddComponent<DoraTileSheen>();
        if (!sheen.surface) {
            sheen.surface = new GameObject("DoraSheen", typeof(RectTransform), typeof(Image));
            sheen.surface.layer = card.gameObject.layer;
            var rect = (RectTransform)sheen.surface.transform;
            rect.SetParent(TileFaceOverlay.Get(card), false);
            // White face inside the hand sprite: omit the top rim and bottom body.
            rect.anchorMin = new Vector2(.065f, .055f);
            rect.anchorMax = new Vector2(.935f, .895f);
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            sheen.image = sheen.surface.GetComponent<Image>();
            // The rectangle defines one common face frame. Brightening happens
            // in the real image layers, where their original colors are known.
            sheen.image.enabled = false;
            sheen.image.raycastTarget = false;
        }
        // Effects cover artwork, while corner indices stay crisp above them.
        sheen.surface.transform.SetAsFirstSibling();
        sheen.body = card.GetComponent<TileCard>()?.BadgeBody ?? card.GetComponent<StaticCard>()?.BadgeBody;
        sheen.BindImage(sheen.body);
        sheen.BindImage(card.GetComponent<TileCard>()?.FaceArtwork ?? card.GetComponent<StaticCard>()?.FaceArtwork);
        sheen.BindImage(card.GetComponent<StaticCard>()?.FaceTexture);
        sheen.surface.SetActive(true);
        sheen.SyncTint();
    }

    public static void Apply3D(Renderer face, int tile, bool faceUp, bool halfTurn) {
        if (!face) return;
        var sheen = face.GetComponent<DoraTileSheen>();
        bool visible = faceUp && RiichiDoraHints.IsDora(tile);
        if (!visible) { if (sheen && sheen.surface) sheen.surface.SetActive(false); return; }
        if (!sheen) sheen = face.gameObject.AddComponent<DoraTileSheen>();
        if (!sheen.surface) {
            if (!quad) {
                quad = new Mesh { name = "Tile face overlay", hideFlags = HideFlags.HideAndDontSave };
                quad.vertices = new[] { new Vector3(-.5f,-.5f), new Vector3(.5f,-.5f), new Vector3(.5f,.5f), new Vector3(-.5f,.5f) };
                quad.uv = new[] { Vector2.zero, Vector2.right, Vector2.one, Vector2.up };
                quad.colors = new[] { Color.white, Color.white, Color.white, Color.white };
                quad.triangles = new[] { 0,2,1,0,3,2 };
                quad.RecalculateBounds();
            }
            sheen.surface = new GameObject("DoraSheen3D", typeof(MeshFilter), typeof(MeshRenderer));
            sheen.surface.layer = face.gameObject.layer;
            sheen.surface.transform.SetParent(face.transform, false);
            sheen.surface.GetComponent<MeshFilter>().sharedMesh = quad;
            var renderer = sheen.surface.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = SurfaceMaterial();
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
        }
        Bounds bounds = face.localBounds;
        var target = sheen.surface.transform;
        target.localPosition = TileFaceFrame.Center(bounds);
        target.localRotation = TileFaceFrame.Rotation(halfTurn);
        target.localScale = new Vector3(bounds.size.x * .88f, bounds.size.y * .88f, 1);
        sheen.surface.SetActive(true);
    }

    private void OnEnable() => Canvas.willRenderCanvases += SyncTint;
    private void OnDisable() => Canvas.willRenderCanvases -= SyncTint;
    private void BindImage(Image target) {
        if (!target) return;
        if (!uiMaterial) uiMaterial = new Material(Resources.Load<Shader>("Materials/Tiles/DoraSheenUI")) {
            name = "Dora UI face", hideFlags = HideFlags.HideAndDontSave
        };
        if (!originalMaterials.ContainsKey(target)) originalMaterials.Add(target,target.material);
        target.material = uiMaterial;
    }
    private void RestoreImages() {
        foreach (var pair in originalMaterials) if (pair.Key && pair.Key.material == uiMaterial) pair.Key.material = pair.Value;
        originalMaterials.Clear();
    }
    private void OnDestroy() {
        RestoreImages();
        if (uiMaterial) Destroy(uiMaterial);
    }
    private void SyncTint() {
        if (!uiMaterial || !surface || !surface.activeSelf) return;
        // Do not depend on Canvas callback subscription order after pooling.
        GetComponent<TileFaceOverlay>()?.Sync();
        var rect = (RectTransform)surface.transform;
        if (rect.rect.width <= 0 || rect.rect.height <= 0) return;
        var toFace = Matrix4x4.TRS(new Vector3(.5f,.5f,0),Quaternion.identity,
            new Vector3(1/rect.rect.width,1/rect.rect.height,1)) * rect.worldToLocalMatrix;
        uiMaterial.SetMatrix("_WorldToFace",toFace);
        foreach (var pair in originalMaterials) if (pair.Key) {
            var rendered = pair.Key.materialForRendering;
            if (rendered && rendered != uiMaterial) rendered.SetMatrix("_WorldToFace",toFace);
        }
    }

    public static void RefreshAll() {
        foreach (var card in Object.FindObjectsByType<TileCard>(UnityEngine.FindObjectsSortMode.None)) Apply(card.transform, card.tileId);
        foreach (var card in Object.FindObjectsByType<StaticCard>(UnityEngine.FindObjectsSortMode.None)) Apply(card.transform, card.TileId);
        foreach (var tile in Object.FindObjectsByType<Tile3D>(UnityEngine.FindObjectsSortMode.None)) tile.RefreshRuleBadge();
    }
}
