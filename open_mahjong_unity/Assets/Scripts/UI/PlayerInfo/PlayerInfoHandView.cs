using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>同组牌无间隔；手牌与副露、副露与副露之间留缝。空间不足时整体等比缩小。</summary>
[RequireComponent(typeof(RectTransform))]
public sealed class PlayerInfoHandView : MonoBehaviour {
    [SerializeField] private Texture2D tileBackground;
    [SerializeField, Min(1)] private float tileWidth = 40;
    [SerializeField, Min(0)] private float meldGap = 9;
    [SerializeField] private List<RawImage> faces = new List<RawImage>();
    private readonly Dictionary<int, Sprite> sprites = new Dictionary<int, Sprite>();
    private readonly Dictionary<RawImage, float> faceAspectRatios = new Dictionary<RawImage, float>();
    private readonly HashSet<int> groupStarts = new HashSet<int>();
    private readonly HashSet<int> faceDownTiles = new HashSet<int>();
    private Texture2D tileBack;
    private int tileCount;

    public void SetHand(int[] concealed, params int[][] melds) {
        faceDownTiles.Clear();
        SetContent(concealed, melds);
    }

    public void SetExample(int[][] groups, int[] faceDownIndices) {
        faceDownTiles.Clear();
        if (faceDownIndices != null) foreach (int index in faceDownIndices) faceDownTiles.Add(index);
        SetContent(null, groups);
    }

    private void SetContent(int[] concealed, int[][] melds) {
        tileCount = 0;
        groupStarts.Clear();
        Append(concealed, false);
        if (melds != null) foreach (var meld in melds) Append(meld, true);
        for (int i = tileCount; i < faces.Count; i++) faces[i].transform.parent.gameObject.SetActive(false);
        LayoutTiles();
    }

    private void Append(int[] tiles, bool separate) {
        if (tiles == null || tiles.Length == 0) return;
        if (separate && tileCount > 0) groupStarts.Add(tileCount);
        foreach (int id in tiles) {
            if (tileCount == faces.Count) CreateTile();
            bool faceDown = faceDownTiles.Contains(tileCount);
            var face = faces[tileCount++];
            face.transform.parent.gameObject.SetActive(true);
            if (!sprites.TryGetValue(id, out var sprite) || sprite == null) {
                sprite = HongqueTileVisual.IsHongqueId(id)
                    ? HongqueTileVisual.LoadSprite(id) : BuiltinTileSprites.Hand(TilePackIds.PackOfficial, id);
                sprites[id] = sprite;
            }
            face.texture = sprite != null ? sprite.texture : null;
            Vector4 uv = sprite != null ? UnityEngine.Sprites.DataUtility.GetOuterUV(sprite) : new Vector4(0, 0, 1, 1);
            face.uvRect = Rect.MinMaxRect(uv.x, uv.y, uv.z, uv.w);
            Vector4 padding = sprite != null ? UnityEngine.Sprites.DataUtility.GetPadding(sprite) : Vector4.zero;
            Vector2 canvas = sprite != null ? sprite.rect.size : Vector2.one;
            faceAspectRatios[face] = canvas.x / canvas.y;
            face.enabled = !faceDown;
            // Hand artwork and its backing share the same full-canvas registration.
            // Atlas packing trims transparent borders; restore that space around the sampled region.
            face.rectTransform.anchorMin = new Vector2(padding.x / canvas.x, padding.y / canvas.y);
            face.rectTransform.anchorMax = new Vector2(1 - padding.z / canvas.x, 1 - padding.w / canvas.y);
            face.rectTransform.offsetMin = face.rectTransform.offsetMax = Vector2.zero;
            var background = face.transform.parent.GetComponent<RawImage>();
            if (faceDown && !tileBack) tileBack = Resources.Load<Texture2D>("image/Cards/Surfaces/backs/hand-default");
            background.texture = faceDown ? tileBack : tileBackground;
            background.enabled = faceDown || !HongqueTileVisual.IsHongqueId(id);
        }
    }

    private void CreateTile() {
        var tile = new GameObject("Tile_" + faces.Count, typeof(RectTransform), typeof(RawImage));
        tile.layer = gameObject.layer;
        tile.transform.SetParent(transform, false);
        var background = tile.GetComponent<RawImage>();
        background.texture = tileBackground;
        background.raycastTarget = false;
        var faceObject = new GameObject("Face", typeof(RectTransform), typeof(RawImage));
        faceObject.layer = gameObject.layer;
        faceObject.transform.SetParent(tile.transform, false);
        var face = faceObject.GetComponent<RawImage>();
        face.raycastTarget = false;
        face.rectTransform.anchorMin = Vector2.zero;
        face.rectTransform.anchorMax = Vector2.one;
        face.rectTransform.offsetMin = face.rectTransform.offsetMax = Vector2.zero;
        faces.Add(face);
    }

    private void OnRectTransformDimensionsChange() => LayoutTiles();

    private void LayoutTiles() {
        if (tileCount == 0) return;
        var area = ((RectTransform)transform).rect;
        float naturalWidth = tileCount * tileWidth + groupStarts.Count * meldGap;
        float heightRatio = tileBackground ? (float)tileBackground.height / tileBackground.width : 389f / 272;
        float scale = Mathf.Clamp01(Mathf.Min(area.width / naturalWidth, area.height / (tileWidth * heightRatio)));
        float width = tileWidth * scale, x = 0;
        for (int i = 0; i < tileCount; i++) {
            if (groupStarts.Contains(i)) x += meldGap * scale;
            var tile = (RectTransform)faces[i].transform.parent;
            tile.anchorMin = tile.anchorMax = tile.pivot = new Vector2(0, 1);
            var background = tile.GetComponent<RawImage>();
            var texture = background.enabled ? background.texture : faces[i].texture;
            float aspect = texture ? (float)texture.width / texture.height : 1 / heightRatio;
            if (!background.enabled && faceAspectRatios.TryGetValue(faces[i], out float faceAspect)) aspect = faceAspect;
            float height = Mathf.Min(width * heightRatio, width / aspect);
            tile.sizeDelta = new Vector2(height * aspect, height);
            tile.anchoredPosition = new Vector2(x + (width - tile.rect.width) * .5f, 0);
            x += width;
        }
    }
}
