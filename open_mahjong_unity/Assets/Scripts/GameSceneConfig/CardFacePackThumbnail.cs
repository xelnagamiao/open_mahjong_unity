using System.Linq;
using UnityEngine;

/// <summary>Pack thumbnails use the actual pack layout without changing the global selection.</summary>
public sealed class CardFacePackThumbnail : MonoBehaviour {
    public string pack;
    public UnityEngine.UI.Image background, artwork;
    public int tileId = 41;
    string requestedPack;
    Sprite customSprite;
    Texture2D customTexture;
    int customTileId;

    void OnEnable() {
        TileFaceResolver.OnPackChanged += Refresh;
        HandSurfaceLibrary.Changed += Refresh;
        Refresh();
    }
    void OnDisable() {
        TileFaceResolver.OnPackChanged -= Refresh;
        HandSurfaceLibrary.Changed -= Refresh;
    }
    void OnDestroy() => ReleaseCustomImage();
    void OnRectTransformDimensionsChange() { if (isActiveAndEnabled) Refresh(); }

    public void SetPack(string id) {
        if (pack != id) { ReleaseCustomImage(); requestedPack = null; }
        pack = id;
        Refresh();
    }

    void Refresh() {
        if (background == null || artwork == null || ConfigManager.Instance == null) return;
        bool custom = TilePackIds.IsCustomPack(pack);
        if (custom && requestedPack != pack) {
            string id = requestedPack = pack;
            TilePackLibrary.Load(id, result => {
                if (this == null || requestedPack != id || pack != id || result.HandPngs.Count == 0) return;
                customTileId = result.HandPngs.ContainsKey(tileId) ? tileId
                    : result.HandPngs.ContainsKey(11) ? 11 : result.HandPngs.Keys.OrderBy(key => key).First();
                TilePackLayoutConfig.Register(id, result.LayoutConfig);
                customTexture = UnityAssetIdb.ToTexture(result.HandPngs[customTileId]);
                if (customTexture == null) return;
                customSprite = Sprite.Create(customTexture, new Rect(0, 0, customTexture.width, customTexture.height),
                    new Vector2(.5f, .5f), 100, 0, SpriteMeshType.FullRect);
                Refresh();
            }, _ => { if (this != null && pack == id) Refresh(); });
        }
        background.sprite = TileFaceResolver.LoadHandBackground();
        background.preserveAspect = true;
        artwork.sprite = custom ? customSprite : BuiltinTileSprites.Shared(pack, tileId) ?? BuiltinTileSprites.Hand(pack, tileId);
        artwork.enabled = artwork.sprite != null;
        TileFaceFit.ApplyHandArtwork(artwork, background, custom && customSprite != null ? customTileId : tileId,
            HandSurfaceLibrary.CurrentFaceLayout, pack);
    }

    void ReleaseCustomImage() {
        if (customSprite != null) Destroy(customSprite);
        if (customTexture != null) Destroy(customTexture);
        customSprite = null; customTexture = null;
    }
}
