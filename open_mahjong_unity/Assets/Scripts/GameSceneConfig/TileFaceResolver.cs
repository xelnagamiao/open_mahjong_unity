using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 解析 2D/3D 牌面：虹雀始终官方图；标准麻将可选官方 / 预装 Resources / 自定义 zip。
/// 手牌花纹原样叠在牌面背景上，不做裁切或逐像素合成。
/// </summary>
public static class TileFaceResolver {
    public static event Action OnPackChanged;

    private static readonly Dictionary<int, Sprite> CustomHandSprites = new Dictionary<int, Sprite>();
    private static readonly Dictionary<int, Texture2D> CustomHandTextures = new Dictionary<int, Texture2D>();
    private static readonly Dictionary<int, Texture2D> CustomTableTextures = new Dictionary<int, Texture2D>();
    private static readonly Dictionary<int, Sprite> CustomTableSprites = new Dictionary<int, Sprite>();
    private static readonly Dictionary<int, Sprite> OfficialSpriteCache = new Dictionary<int, Sprite>();
    private static Sprite handBackgroundSprite;
    private static Texture2D handBackgroundTexture;
    private static Texture2D defaultHandBackgroundTexture;
    private static Sprite customHandBackSprite;
    private static Texture2D customHandBackTexture;
    private static bool diskLoaded;
    private static bool webGlLoadStarted;
    private static bool ownsRuntimeTextures;

    public static bool HasCustomStandardPack =>
        ConfigManager.Instance != null
        && TilePackIds.IsLayeredPack(ConfigManager.Instance.StandardTilePackId)
        && (CustomHandSprites.Count + CustomTableTextures.Count > 0
            || TilePackIds.IsBuiltinLayeredPack(ConfigManager.Instance.StandardTilePackId));

    public static IReadOnlyDictionary<int, Sprite> CustomHandPreview => CustomHandSprites;

    public static bool UsesLayeredHandFaces =>
        ConfigManager.Instance != null && ConfigManager.Instance.UseHandFaceBackground;

    public static bool UsesCustomStandardFaces =>
        ConfigManager.Instance != null && TilePackIds.IsLayeredPack(ConfigManager.Instance.StandardTilePackId);

    public static void EnsureLoaded() {
        if (ConfigManager.Instance == null) {
            return;
        }
        string packId = ConfigManager.Instance.StandardTilePackId;
        if (packId == TilePackIds.PackOfficial || TilePackIds.IsBuiltinLayeredPack(packId)) {
            return;
        }
        if (packId == TilePackIds.PackCustom) {
            LoadCustomPack();
        }
    }

    public static void SelectPack(string packId) {
        packId = TilePackIds.NormalizePackId(packId);
        if (ConfigManager.Instance != null) {
            ConfigManager.Instance.SetStandardTilePackId(packId);
        }
        diskLoaded = false;
        webGlLoadStarted = false;
        DestroyCustomTextures();
        if (packId == TilePackIds.PackCustom) {
            EnsureLoaded();
        }
        NotifyChanged();
    }

    public static void ApplyImported(TilePackImporter.Result imported, bool persist, bool enableFlag) {
        if (imported == null || !imported.Success) {
            return;
        }
        if (persist) {
            TilePackStorage.SaveImported(imported);
        }
        ReplaceCustomTextures(imported.HandPngs, imported.TablePngs);
        if (enableFlag && ConfigManager.Instance != null) {
            ConfigManager.Instance.SetStandardTilePackId(TilePackIds.PackCustom);
        }
        NotifyChanged();
    }

    public static void ClearCustomPack() {
        SelectPack(TilePackIds.PackOfficial);
    }

    public static void SetUseHandFaceBackground(bool enabled) {
        if (ConfigManager.Instance != null) {
            ConfigManager.Instance.SetUseHandFaceBackground(enabled);
        }
        NotifyChanged();
    }

    public static Sprite LoadSprite(int tileId) {
        EnsureLoaded();
        if (HongqueTileVisual.IsHongqueId(tileId)) {
            return HongqueTileVisual.LoadSprite(tileId);
        }
        if (tileId == ConfigManager.HandBackImageId) {
            Sprite customBack = LoadCustomHandBackSprite();
            if (customBack != null) {
                return customBack;
            }
        }

        int faceId = ResolveFaceId(tileId);
        string packId = CurrentPackId();
        if (TilePackIds.IsBuiltinLayeredPack(packId)) {
            Sprite builtin = Resources.Load<Sprite>(TilePackIds.BuiltinHandResource(packId, faceId));
            if (builtin != null) {
                return builtin;
            }
        }
        else if (packId == TilePackIds.PackCustom
            && CustomHandSprites.TryGetValue(faceId, out Sprite custom) && custom != null) {
            return custom;
        }

        if (OfficialSpriteCache.TryGetValue(faceId, out Sprite cached) && cached != null) {
            return cached;
        }
        Sprite official = Resources.Load<Sprite>($"image/CardFaceImage_xuefun/{faceId}");
        if (official != null) {
            OfficialSpriteCache[faceId] = official;
        }
        return official;
    }

    public static Texture2D LoadTableTexture(int tileId) {
        EnsureLoaded();
        if (HongqueTileVisual.IsHongqueId(tileId)) {
            return HongqueTileVisual.LoadTableTexture(tileId);
        }
        if (!UsesCustomStandardFaces) {
            return null;
        }
        int faceId = ResolveFaceId(tileId);
        string packId = CurrentPackId();
        if (TilePackIds.IsBuiltinLayeredPack(packId)) {
            Texture2D builtin = Resources.Load<Texture2D>(TilePackIds.BuiltinTableResource(packId, faceId));
            if (builtin != null) {
                return builtin;
            }
        }
        if (CustomTableTextures.TryGetValue(faceId, out Texture2D table) && table != null) {
            return table;
        }
        return null;
    }

    public static Sprite LoadTableSprite(int tileId) {
        EnsureLoaded();
        if (HongqueTileVisual.IsHongqueId(tileId)) {
            return HongqueTileVisual.LoadSprite(tileId);
        }
        int faceId = ResolveFaceId(tileId);
        string packId = CurrentPackId();
        if (packId == TilePackIds.PackOfficial) {
            Sprite official = Resources.Load<Sprite>(TilePackIds.BuiltinTableResource(TilePackIds.PackOfficial, faceId));
            if (official != null) {
                return official;
            }
            return Resources.Load<Sprite>($"image/CardFaceMaterial_xuefun/{faceId}");
        }
        if (TilePackIds.IsBuiltinLayeredPack(packId)) {
            Sprite builtin = Resources.Load<Sprite>(TilePackIds.BuiltinTableResource(packId, faceId));
            if (builtin != null) {
                return builtin;
            }
        }
        if (CustomTableSprites.TryGetValue(faceId, out Sprite cached) && cached != null) {
            return cached;
        }
        if (CustomTableTextures.TryGetValue(faceId, out Texture2D texture) && texture != null) {
            Sprite created = Sprite.Create(
                texture,
                new Rect(0f, 0f, texture.width, texture.height),
                new Vector2(0.5f, 0.5f),
                100f);
            CustomTableSprites[faceId] = created;
            return created;
        }
        return LoadSprite(tileId);
    }

    public static bool ShouldLayerHandFace(int tileId) {
        if (tileId == ConfigManager.HandBackImageId || !UsesLayeredHandFaces || HongqueTileVisual.IsHongqueId(tileId)) {
            return false;
        }
        if (CurrentPackId() == TilePackIds.PackOfficial) {
            return false;
        }
        return HasPackHandFace(ResolveFaceId(tileId));
    }

    public static Sprite LoadHandBackground() {
        if (!UsesLayeredHandFaces) {
            return null;
        }
        Texture2D texture = PeekHandBackgroundTexture();
        if (texture == null) {
            return null;
        }
        if (handBackgroundTexture != texture) {
            ReplaceHandBackgroundSprite(texture);
        }
        return handBackgroundSprite;
    }

    public static Texture2D PeekHandBackgroundTexture() {
        Texture2D custom = CardBackManager.LoadSavedHandBackground();
        if (custom != null) {
            return custom;
        }
        EnsureDefaultHandBackground();
        return defaultHandBackgroundTexture;
    }

    public static Sprite PreviewHand(int tileId) {
        return LoadSprite(tileId);
    }

    public static bool HasCustomFace(int tileId) {
        return HasPackHandFace(ResolveFaceId(tileId)) || HasPackTableFace(ResolveFaceId(tileId));
    }

    public static int CountPackFaces() {
        string packId = CurrentPackId();
        if (packId == TilePackIds.PackCustom) {
            return CustomHandSprites.Count;
        }
        if (!TilePackIds.IsBuiltinLayeredPack(packId) && packId != TilePackIds.PackOfficial) {
            return 0;
        }
        int count = 0;
        for (int i = 0; i < TilePackIds.StandardFaceIds.Length; i++) {
            if (HasPackHandFace(TilePackIds.StandardFaceIds[i])) {
                count++;
            }
        }
        return count;
    }

    public static void RefreshVisibleCards() {
        TileCard[] cards = UnityEngine.Object.FindObjectsByType<TileCard>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        for (int i = 0; i < cards.Length; i++) {
            if (cards[i] != null && cards[i].tileId >= 0) {
                cards[i].SetTile(cards[i].tileId, cards[i].currentGetTile);
            }
        }
        StaticCard[] staticCards = UnityEngine.Object.FindObjectsByType<StaticCard>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        for (int i = 0; i < staticCards.Length; i++) {
            if (staticCards[i] != null && staticCards[i].TileId >= 0) {
                staticCards[i].SetTileOnlyImage(staticCards[i].TileId);
            }
        }
        if (MahjongObjectPool.Instance != null) {
            MahjongObjectPool.Instance.RefreshCustomStandardFaces();
        }
    }

    public static void NotifyHandBackgroundChanged() {
        if (handBackgroundTexture != null && handBackgroundTexture != defaultHandBackgroundTexture) {
            ReplaceHandBackgroundSprite(null);
        }
        NotifyChanged();
    }

    public static void NotifyHandBackChanged() {
        if (customHandBackSprite != null) {
            UnityEngine.Object.Destroy(customHandBackSprite);
            customHandBackSprite = null;
        }
        customHandBackTexture = null;
        NotifyChanged();
    }

    public static Texture2D PeekDefaultHandBackTexture() {
        Sprite sprite = Resources.Load<Sprite>($"image/CardFaceImage_xuefun/{ConfigManager.HandBackImageId}");
        if (sprite != null && sprite.texture != null) {
            return sprite.texture;
        }
        return Resources.Load<Texture2D>($"image/CardFaceImage_xuefun/{ConfigManager.HandBackImageId}");
    }

    private static void NotifyChanged() {
        RefreshVisibleCards();
        OnPackChanged?.Invoke();
    }

    private static string CurrentPackId() {
        return ConfigManager.Instance != null
            ? ConfigManager.Instance.StandardTilePackId
            : TilePackIds.PackOfficial;
    }

    private static bool HasPackHandFace(int faceId) {
        string packId = CurrentPackId();
        if (packId == TilePackIds.PackOfficial) {
            return Resources.Load<Sprite>(TilePackIds.BuiltinHandResource(packId, faceId)) != null
                || Resources.Load<Sprite>($"image/CardFaceImage_xuefun/{faceId}") != null;
        }
        if (TilePackIds.IsBuiltinLayeredPack(packId)) {
            return Resources.Load<Sprite>(TilePackIds.BuiltinHandResource(packId, faceId)) != null;
        }
        return CustomHandSprites.ContainsKey(faceId);
    }

    private static bool HasPackTableFace(int faceId) {
        string packId = CurrentPackId();
        if (packId == TilePackIds.PackOfficial || TilePackIds.IsBuiltinLayeredPack(packId)) {
            return Resources.Load<Texture2D>(TilePackIds.BuiltinTableResource(packId, faceId)) != null;
        }
        return CustomTableTextures.ContainsKey(faceId);
    }

    private static void LoadCustomPack() {
#if UNITY_WEBGL && !UNITY_EDITOR
        if (webGlLoadStarted) {
            return;
        }
        webGlLoadStarted = true;
        TilePackStorage.LoadZipFromIndexedDb(zip => ApplyImported(TilePackImporter.Import(zip), persist: false, enableFlag: false), err => {
            if (err != "empty") {
                Debug.LogWarning("加载 IndexedDB 牌面包失败: " + err);
            }
        });
#else
        if (diskLoaded) {
            return;
        }
        diskLoaded = true;
        var hand = new Dictionary<int, byte[]>();
        var table = new Dictionary<int, byte[]>();
        TilePackStorage.LoadPngsFromDisk(hand, table);
        ReplaceCustomTextures(hand, table);
#endif
    }

    private static int ResolveFaceId(int tileId) {
        if (ConfigManager.Instance != null && ConfigManager.Instance.UseBlankWhiteDragonFace(tileId)) {
            return ConfigManager.BlankFaceImageId;
        }
        return tileId;
    }

    private static void ReplaceCustomTextures(Dictionary<int, byte[]> handPngs, Dictionary<int, byte[]> tablePngs) {
        DestroyCustomTextures();
        ownsRuntimeTextures = true;
        if (handPngs != null) {
            foreach (var pair in handPngs) {
                Texture2D texture = BytesToTexture(pair.Value, "CustomHand_" + pair.Key, false);
                if (texture == null) {
                    continue;
                }
                CustomHandTextures[pair.Key] = texture;
                CustomHandSprites[pair.Key] = Sprite.Create(
                    texture,
                    new Rect(0f, 0f, texture.width, texture.height),
                    new Vector2(0.5f, 0.5f),
                    100f);
            }
        }
        if (tablePngs != null) {
            foreach (var pair in tablePngs) {
                Texture2D texture = BytesToTexture(pair.Value, "CustomTable_" + pair.Key, true);
                if (texture != null) {
                    CustomTableTextures[pair.Key] = texture;
                }
            }
        }
    }

    private static void EnsureDefaultHandBackground() {
        if (defaultHandBackgroundTexture != null) {
            return;
        }
        defaultHandBackgroundTexture = Resources.Load<Texture2D>(TilePackIds.DefaultHandBgResource);
        if (defaultHandBackgroundTexture == null) {
            defaultHandBackgroundTexture = Resources.Load<Texture2D>("image/CardFaceImage_xuefun/2");
        }
    }

    private static void ReplaceHandBackgroundSprite(Texture2D texture) {
        if (handBackgroundSprite != null) {
            UnityEngine.Object.Destroy(handBackgroundSprite);
            handBackgroundSprite = null;
        }
        handBackgroundTexture = texture;
        if (texture == null) {
            return;
        }
        handBackgroundSprite = Sprite.Create(
            texture,
            new Rect(0f, 0f, texture.width, texture.height),
            new Vector2(0.5f, 0.5f),
            100f);
    }

    private static Sprite LoadCustomHandBackSprite() {
        Texture2D texture = CardBackManager.LoadSavedHandBack();
        if (texture == null) {
            return null;
        }
        if (customHandBackTexture != texture) {
            if (customHandBackSprite != null) {
                UnityEngine.Object.Destroy(customHandBackSprite);
                customHandBackSprite = null;
            }
            customHandBackTexture = texture;
            customHandBackSprite = Sprite.Create(
                texture,
                new Rect(0f, 0f, texture.width, texture.height),
                new Vector2(0.5f, 0.5f),
                100f);
        }
        return customHandBackSprite;
    }

    private static Texture2D BytesToTexture(byte[] png, string name, bool markNonReadable) {
        if (png == null || png.Length == 0) {
            return null;
        }
        var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        texture.name = name;
        texture.filterMode = FilterMode.Bilinear;
        texture.wrapMode = TextureWrapMode.Clamp;
        if (!ImageConversion.LoadImage(texture, png, markNonReadable)) {
            UnityEngine.Object.Destroy(texture);
            return null;
        }
        return texture;
    }

    private static void DestroyCustomTextures() {
        if (!ownsRuntimeTextures) {
            CustomHandSprites.Clear();
            CustomHandTextures.Clear();
            CustomTableTextures.Clear();
            CustomTableSprites.Clear();
            return;
        }
        foreach (var pair in CustomHandSprites) {
            if (pair.Value != null) {
                UnityEngine.Object.Destroy(pair.Value);
            }
        }
        foreach (var pair in CustomHandTextures) {
            if (pair.Value != null) {
                UnityEngine.Object.Destroy(pair.Value);
            }
        }
        foreach (var pair in CustomTableSprites) {
            if (pair.Value != null) {
                UnityEngine.Object.Destroy(pair.Value);
            }
        }
        foreach (var pair in CustomTableTextures) {
            if (pair.Value != null) {
                UnityEngine.Object.Destroy(pair.Value);
            }
        }
        CustomHandSprites.Clear();
        CustomHandTextures.Clear();
        CustomTableSprites.Clear();
        CustomTableTextures.Clear();
        ownsRuntimeTextures = false;
    }
}
