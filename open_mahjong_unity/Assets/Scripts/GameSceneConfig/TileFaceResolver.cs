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
    private static Sprite tableBackgroundSprite;
    private static Texture2D tableBackgroundTexture;
    private static Sprite customHandBackSprite;
    private static Texture2D customHandBackTexture;
    private static bool diskLoaded;
    private static bool webGlLoadStarted;
    private static bool ownsRuntimeTextures;
    private static int loadVersion;
    private static Sprite flatTableBackground;

    public static bool HasCustomStandardPack =>
        ConfigManager.Instance != null
        && TilePackIds.IsLayeredPack(ConfigManager.Instance.StandardTilePackId)
        && (CustomHandSprites.Count + CustomTableTextures.Count > 0
            || TilePackIds.IsBuiltinLayeredPack(ConfigManager.Instance.StandardTilePackId));

    public static IReadOnlyDictionary<int, Sprite> CustomHandPreview => CustomHandSprites;

    // 标准手牌统一自动叠底，不依赖旧 UseHandFaceBackground 存档。
    public static bool UsesLayeredHandFaces => true;

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
        if (TilePackIds.IsCustomPack(packId)) {
            LoadCustomPack();
        }
    }

    public static void SelectPack(string packId) {
        packId = TilePackIds.NormalizePackId(packId);
        loadVersion++;
        if (ConfigManager.Instance != null) {
            ConfigManager.Instance.SetStandardTilePackId(packId);
        }
        diskLoaded = false;
        webGlLoadStarted = false;
        DestroyCustomTextures();
        if (TilePackIds.IsCustomPack(packId)) {
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

    public static void ApplyLibraryPack(string id, TilePackImporter.Result imported) {
        if (!TilePackIds.IsCustomPack(id) || imported == null || !imported.Success) return;
        loadVersion++;
        ConfigManager.Instance?.SetStandardTilePackId(id);
        diskLoaded = webGlLoadStarted = true;
        ReplaceCustomTextures(imported.HandPngs, imported.TablePngs);
        NotifyChanged();
    }

    public static void SetUseHandFaceBackground(bool enabled) {
        if (ConfigManager.Instance != null) {
            ConfigManager.Instance.SetUseHandFaceBackground(enabled);
        }
        NotifyChanged();
    }

    public static Sprite LoadSprite(int tileId, bool applyWhiteDragonFaceSetting = true) {
        EnsureLoaded();
        if (HongqueTileVisual.IsHongqueId(tileId)) {
            return HongqueTileVisual.LoadSprite(tileId);
        }
        if (tileId == ConfigManager.HandBackImageId) {
            return LoadCustomHandBackSprite() ?? Resources.Load<Sprite>(TilePackIds.DefaultHandBackResource);
        }
        // 旧编号 1 是横向牌体底图，不属于标准牌面包。
        if (tileId == 1) return Resources.Load<Sprite>(TilePackIds.HandHorizontalBgResource);

        int faceId = ResolveFaceId(tileId, applyWhiteDragonFaceSetting);
        string packId = CurrentPackId();
        if (TilePackIds.IsBuiltinPack(packId)) {
            Sprite builtin = Resources.Load<Sprite>(TilePackIds.BuiltinHandResource(packId, faceId));
            if (builtin != null) {
                return builtin;
            }
        }
        else if (TilePackIds.IsCustomPack(packId)
            && CustomHandSprites.TryGetValue(faceId, out Sprite custom) && custom != null) {
            return custom;
        }

        if (OfficialSpriteCache.TryGetValue(faceId, out Sprite cached) && cached != null) {
            return cached;
        }
        Sprite official = Resources.Load<Sprite>(TilePackIds.BuiltinHandResource(TilePackIds.PackOfficial, faceId));
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
        int faceId = ResolveFaceId(tileId, applyWhiteDragonFaceSetting: true);
        string packId = CurrentPackId();
        if (TilePackIds.IsBuiltinLayeredPack(packId)) {
            Texture2D builtin = Resources.Load<Texture2D>(TilePackIds.BuiltinTableResource(packId, faceId));
            if (builtin != null) {
                return builtin;
            }
        }
        if (TilePackIds.IsCustomPack(packId)
            && CustomTableTextures.TryGetValue(faceId, out Texture2D table) && table != null) {
            return table;
        }
        return null;
    }

    /// <summary>按实际有图的来源判断；用于区分用户上传和官方缺图回退。</summary>
    public static bool IsUploadedTableFace(int tileId, bool applyWhiteDragonFaceSetting = true) {
        EnsureLoaded();
        if (HongqueTileVisual.IsHongqueId(tileId) || !TilePackIds.IsCustomPack(CurrentPackId())) return false;
        int faceId = ResolveFaceId(tileId, applyWhiteDragonFaceSetting);
        return CustomTableTextures.TryGetValue(faceId, out Texture2D texture) && texture != null;
    }

    public static Sprite LoadTableSprite(int tileId, bool applyWhiteDragonFaceSetting = true) {
        EnsureLoaded();
        if (HongqueTileVisual.IsHongqueId(tileId)) {
            return HongqueTileVisual.LoadSprite(tileId);
        }
        int faceId = ResolveFaceId(tileId, applyWhiteDragonFaceSetting);
        string packId = CurrentPackId();
        if (packId == TilePackIds.PackOfficial) {
            return Resources.Load<Sprite>(TilePackIds.BuiltinTableResource(TilePackIds.PackOfficial, faceId));
        }
        if (TilePackIds.IsBuiltinLayeredPack(packId)) {
            Sprite builtin = Resources.Load<Sprite>(TilePackIds.BuiltinTableResource(packId, faceId));
            if (builtin != null) {
                return builtin;
            }
        }
        if (TilePackIds.IsCustomPack(packId)
            && CustomTableSprites.TryGetValue(faceId, out Sprite cached) && cached != null) {
            return cached;
        }
        if (TilePackIds.IsCustomPack(packId)
            && CustomTableTextures.TryGetValue(faceId, out Texture2D texture) && texture != null) {
            Sprite created = Sprite.Create(
                texture,
                new Rect(0f, 0f, texture.width, texture.height),
                new Vector2(0.5f, 0.5f),
                100f, 0, SpriteMeshType.FullRect);
            CustomTableSprites[faceId] = created;
            return created;
        }
        // 缺失的桌面牌面仍回退桌面资源，不能把完整手牌牌体贴进 3D 预览。
        return Resources.Load<Sprite>(TilePackIds.BuiltinTableResource(TilePackIds.PackOfficial, faceId));
    }

    public static bool ShouldLayerHandFace(int tileId) {
        // 缺失上传牌面也会回退到透明官方图，因此不能按“是否有上传图”跳过底图。
        return tileId != ConfigManager.HandBackImageId && tileId != 1
            && !HongqueTileVisual.IsHongqueId(tileId) && TilePackIds.IsStandardFaceId(tileId);
    }

    public static Sprite LoadHandBackground() {
        Texture2D texture = PeekHandBackgroundTexture();
        if (texture == null) {
            return null;
        }
        if (handBackgroundTexture != texture) {
            ReplaceHandBackgroundSprite(texture);
        }
        return handBackgroundSprite;
    }

    /// <summary>
    /// 3D 牌面预览底图只读取 table-bg；未上传时由 3D 正面纯色/默认色铺底。
    /// </summary>
    public static Sprite LoadTableBackground() {
        Texture2D texture = PeekTableBackgroundTexture();
        if (texture == null) {
            return null;
        }
        if (tableBackgroundTexture != texture) {
            ReplaceTableBackgroundSprite(texture);
        }
        return tableBackgroundSprite;
    }

    public static Texture2D PeekTableBackgroundTexture() {
        return CardBackManager.LoadSavedTableBackground();
    }

    public static Sprite FlatTableBackground {
        get {
            if (flatTableBackground != null) return flatTableBackground;
            const int width = TileTextureLayout.TableRecommendedWidth / 4;
            const int height = TileTextureLayout.TableRecommendedHeight / 4;
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false) { name = "FlatTablePreviewBase" };
            var pixels = new Color32[width * height];
            for (int i = 0; i < pixels.Length; i++) pixels[i] = new Color32(255,255,255,255);
            texture.SetPixels32(pixels); texture.Apply(false, true);
            flatTableBackground = Sprite.Create(texture, new Rect(0,0,width,height), new Vector2(.5f,.5f));
            return flatTableBackground;
        }
    }

    public static Color TablePreviewBaseColor => ConfigManager.Instance != null && ConfigManager.Instance.TableFaceUseSolidColor
        ? ConfigManager.Instance.EffectiveTableFaceColor : ConfigManager.DefaultTableFaceFallbackColor;

    public static Texture2D PeekHandBackgroundTexture() {
        Texture2D custom = CardBackManager.LoadSavedHandBackground();
        if (custom != null) {
            return custom;
        }
        EnsureDefaultHandBackground();
        return defaultHandBackgroundTexture;
    }

    public static Sprite PreviewHand(int tileId) {
        return LoadSprite(tileId, applyWhiteDragonFaceSetting: false);
    }

    public static Sprite PreviewTable(int tileId) {
        return LoadTableSprite(tileId, applyWhiteDragonFaceSetting: false);
    }

    public static bool HasCustomFace(int tileId) {
        return HasPackHandFace(tileId) || HasPackTableFace(tileId);
    }

    public static int CountPackFaces() {
        string packId = CurrentPackId();
        if (TilePackIds.IsCustomPack(packId)) {
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
                cards[i].RefreshVisual();
            }
        }
        StaticCard[] staticCards = UnityEngine.Object.FindObjectsByType<StaticCard>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        for (int i = 0; i < staticCards.Length; i++) {
            if (staticCards[i] != null && staticCards[i].TileId >= 0) {
                staticCards[i].RefreshVisual();
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

    public static void NotifyTableBackgroundChanged() {
        if (tableBackgroundSprite != null) {
            ReplaceTableBackgroundSprite(null);
        }
        NotifyChanged();
    }

    public static void NotifyHandBackChanged() {
        if (customHandBackSprite != null) {
            DestroyTransient(customHandBackSprite);
            customHandBackSprite = null;
        }
        customHandBackTexture = null;
        NotifyChanged();
    }

    public static Texture2D PeekDefaultHandBackTexture() {
        Sprite sprite = Resources.Load<Sprite>(TilePackIds.DefaultHandBackResource);
        if (sprite != null && sprite.texture != null) {
            return sprite.texture;
        }
        return Resources.Load<Texture2D>(TilePackIds.DefaultHandBackResource);
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
        if (TilePackIds.IsBuiltinPack(packId)) {
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
        if (diskLoaded || webGlLoadStarted) return;
        diskLoaded = webGlLoadStarted = true;
        int version = loadVersion;
        string id = CurrentPackId();
        TilePackLibrary.Load(id, imported => {
            // 异步读取返回时，用户可能已切到另一套牌面。
            if (version != loadVersion || id != CurrentPackId()) return;
            ReplaceCustomTextures(imported.HandPngs, imported.TablePngs);
            NotifyChanged();
        }, error => {
            if (version == loadVersion && id == CurrentPackId()) Debug.LogWarning(error);
        });
    }

    private static int ResolveFaceId(int tileId, bool applyWhiteDragonFaceSetting) {
        if (applyWhiteDragonFaceSetting
            && ConfigManager.Instance != null
            && ConfigManager.Instance.UseBlankWhiteDragonFace(tileId)) {
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
                    100f, 0, SpriteMeshType.FullRect);
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
    }

    private static void ReplaceHandBackgroundSprite(Texture2D texture) {
        if (handBackgroundSprite != null) {
            DestroyTransient(handBackgroundSprite);
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
            100f, 0, SpriteMeshType.FullRect);
    }

    private static void ReplaceTableBackgroundSprite(Texture2D texture) {
        if (tableBackgroundSprite != null) {
            DestroyTransient(tableBackgroundSprite);
            tableBackgroundSprite = null;
        }
        tableBackgroundTexture = texture;
        if (texture == null) {
            return;
        }
        tableBackgroundSprite = Sprite.Create(
            texture,
            new Rect(0f, 0f, texture.width, texture.height),
            new Vector2(0.5f, 0.5f),
            100f, 0, SpriteMeshType.FullRect);
    }

    private static Sprite LoadCustomHandBackSprite() {
        Texture2D texture = CardBackManager.LoadSavedHandBack();
        if (texture == null) {
            return null;
        }
        if (customHandBackTexture != texture) {
            if (customHandBackSprite != null) {
                DestroyTransient(customHandBackSprite);
                customHandBackSprite = null;
            }
            customHandBackTexture = texture;
            customHandBackSprite = Sprite.Create(
                texture,
                new Rect(0f, 0f, texture.width, texture.height),
                new Vector2(0.5f, 0.5f),
                100f, 0, SpriteMeshType.FullRect);
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
            DestroyTransient(texture);
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
                DestroyTransient(pair.Value);
            }
        }
        foreach (var pair in CustomHandTextures) {
            if (pair.Value != null) {
                DestroyTransient(pair.Value);
            }
        }
        foreach (var pair in CustomTableSprites) {
            if (pair.Value != null) {
                DestroyTransient(pair.Value);
            }
        }
        foreach (var pair in CustomTableTextures) {
            if (pair.Value != null) {
                DestroyTransient(pair.Value);
            }
        }
        CustomHandSprites.Clear();
        CustomHandTextures.Clear();
        CustomTableSprites.Clear();
        CustomTableTextures.Clear();
        ownsRuntimeTextures = false;
    }
    private static void DestroyTransient(UnityEngine.Object value) {
        if (Application.isPlaying) UnityEngine.Object.Destroy(value);
        else UnityEngine.Object.DestroyImmediate(value);
    }

}
