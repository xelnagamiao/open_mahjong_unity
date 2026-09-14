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
    private readonly Dictionary<int, Texture2D> textures = new Dictionary<int, Texture2D>();
    private readonly HashSet<int> groupStarts = new HashSet<int>();
    private int tileCount;

    public void SetHand(int[] concealed, params int[][] melds) {
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
            var face = faces[tileCount++];
            face.transform.parent.gameObject.SetActive(true);
            if (!textures.TryGetValue(id, out var texture)) {
                texture = Resources.Load<Texture2D>($"image/Cards/Faces/official/table/{id}");
                textures[id] = texture;
            }
            face.texture = texture;
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
        face.rectTransform.anchorMin = new Vector2(5f / 48, 7f / 72);
        face.rectTransform.anchorMax = new Vector2(43f / 48, 62f / 72);
        face.rectTransform.offsetMin = face.rectTransform.offsetMax = Vector2.zero;
        faces.Add(face);
    }

    private void OnRectTransformDimensionsChange() => LayoutTiles();

    private void LayoutTiles() {
        if (tileCount == 0) return;
        var area = ((RectTransform)transform).rect;
        float naturalWidth = tileCount * tileWidth + groupStarts.Count * meldGap;
        float scale = Mathf.Clamp01(Mathf.Min(area.width / naturalWidth, area.height / (tileWidth * 1.5f)));
        float width = tileWidth * scale, x = 0;
        for (int i = 0; i < tileCount; i++) {
            if (groupStarts.Contains(i)) x += meldGap * scale;
            var tile = (RectTransform)faces[i].transform.parent;
            tile.anchorMin = tile.anchorMax = tile.pivot = new Vector2(0, 1);
            tile.sizeDelta = new Vector2(width, width * 1.5f);
            tile.anchoredPosition = new Vector2(x, 0);
            x += width;
        }
    }
}
