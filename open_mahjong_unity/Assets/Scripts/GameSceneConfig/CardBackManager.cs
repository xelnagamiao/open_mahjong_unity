using System;
using System.IO;
using System.IO.Compression;
using UnityEngine;

/// <summary>
/// 3D 牌背配置管理器：负责把保存的牌背颜色/图片应用到共享材质与所有已存在的 Tile3D 实例。
/// </summary>
public static class CardBackManager
{
    public const string MaterialResourcePath = "Materials/Tiles/3DTile";
    public const string BackImageDirName = "CardBacks";
    public const string HandBgFileName = "hand-bg.png";
    public const string HandBackFileName = "hand-back.png";
    public const string TableBgFileName = "table-bg.png";

    public static Color CurrentColor { get; private set; } = ConfigManager.DefaultCardBackColor;
    public static Texture2D CurrentTexture { get; private set; }
    public static Texture2D CurrentHandBackground { get; private set; }
    public static Texture2D CurrentHandBack { get; private set; }
    public static Texture2D CurrentTableBackground { get; private set; }
    public static Color CurrentSideColor { get; private set; } = ConfigManager.DefaultSideColor;
    public static Color CurrentBackEdgeColor { get; private set; } = ConfigManager.DefaultBackEdgeColor;
    public static Color CurrentFrontEdgeColor { get; private set; } = Color.white;
    public static bool BackEdgeSyncEnabled { get; private set; } = true;
    public static CardEdgePanel.BackEdgeMode BackEdgeMode { get; private set; } = CardEdgePanel.BackEdgeMode.FollowBack;
    public static CardEdgePanel.FrontEdgeMode FrontEdgeMode { get; private set; } = CardEdgePanel.FrontEdgeMode.Independent;
    public static string HandBgFilePath => Path.Combine(Application.persistentDataPath, BackImageDirName, HandBgFileName);
    public static string HandBackFilePath => Path.Combine(Application.persistentDataPath, BackImageDirName, HandBackFileName);
    public static string TableBgFilePath => Path.Combine(Application.persistentDataPath, BackImageDirName, TableBgFileName);

    private static bool _savedConfigApplied;
    private static bool _handBgLoaded;
    private static bool _handBackLoaded;
    private static bool _tableBgLoaded;

    /// <summary>
    /// 场上 + 对象池（含 inactive）全部 Tile3D。
    /// 默认 FindObjectsByType 漏掉池内未激活牌，下次 Spawn 会仍是旧牌边。
    /// </summary>
    private static void ForEachTile3D(Action<Tile3D> apply)
    {
        if (apply == null) return;
        Tile3D[] tiles = UnityEngine.Object.FindObjectsByType<Tile3D>(
            FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < tiles.Length; i++)
        {
            Tile3D tile = tiles[i];
            if (tile != null) apply(tile);
        }
        if (MahjongObjectPool.Instance == null) return;
        MahjongObjectPool.Instance.ForEachPooledTile(pooled =>
        {
            Tile3D pooledTile = MahjongObjectPool.GetTile3D(pooled);
            if (pooledTile != null) apply(pooledTile);
        });
    }

    private static void ForEachVisualMaterial(Action<Material> apply)
    {
        if (apply == null) return;
        Material shared = Resources.Load<Material>(MaterialResourcePath);
        if (shared != null) apply(shared);
        if (MahjongObjectPool.Instance != null)
        {
            MahjongObjectPool.Instance.ForEachStandaloneMaterial(apply);
        }
    }

    /// <summary>把当前牌边/牌背/牌面背景写到材质（不含每牌唯一的 _FrontTex）。</summary>
    public static void SyncSharedVisualsToMaterial(Material mat)
    {
        if (mat == null) return;
        bool useSolid = ConfigManager.Instance != null && ConfigManager.Instance.TableFaceUseSolidColor;
        bool useBg = ConfigManager.Instance != null
            && ConfigManager.Instance.UseTableFaceBackground
            && CurrentTableBackground != null
            && !useSolid;
        bool coverFace = ResolveTableBgCoverFace();
        float aspect = (CurrentTableBackground != null && CurrentTableBackground.height > 0)
            ? (float)CurrentTableBackground.width / CurrentTableBackground.height
            : 0f;

        mat.SetTexture("_BackTex", CurrentTexture);
        mat.SetFloat("_BackTexBlend", CurrentTexture != null ? 1f : 0f);
        mat.SetFloat("_BackTexExtendEdge", 0f);
        mat.SetColor("_BackColor", CurrentColor);
        mat.SetColor("_SideColor", CurrentSideColor);
        mat.SetColor("_BackEdgeColor", CurrentBackEdgeColor);
        mat.SetColor("_FrontEdgeColor", CurrentFrontEdgeColor);
        mat.SetTexture("_FrontBgTex", CurrentTableBackground);
        mat.SetFloat("_FrontBgBlend", useBg ? 1f : 0f);
        mat.SetFloat("_FrontBgTexAspect", aspect);
        mat.SetFloat("_TableBgCoverFace", coverFace ? 1f : 0f);
        mat.SetFloat("_FrontTexExtendEdge", 0f);
        mat.SetColor("_TableFaceColor",
            ConfigManager.Instance != null ? ConfigManager.Instance.TableFaceColor : Color.white);
        mat.SetFloat("_TableFaceBlend", useSolid ? 1f : 0f);
    }

    /// <summary>Spawn / 改设置：实例 MPB + 该牌实际材质（含虹雀/自定义克隆）跟上当前牌边。</summary>
    public static void ApplyInstanceVisuals(Tile3D tile)
    {
        if (tile == null) return;
        SyncSharedVisualsToMaterial(tile.SharedTileMaterial);
        tile.ApplyBackVisual(CurrentColor, CurrentTexture);
        tile.ApplySideVisual(CurrentSideColor);
        tile.ApplyBackEdgeVisual(CurrentBackEdgeColor);
        tile.ApplyFrontEdgeVisual(CurrentFrontEdgeColor);
        bool useSolid = ConfigManager.Instance != null && ConfigManager.Instance.TableFaceUseSolidColor;
        bool showBg = ConfigManager.Instance != null
            && ConfigManager.Instance.UseTableFaceBackground
            && CurrentTableBackground != null
            && !useSolid;
        tile.ApplyFrontBgVisual(showBg ? CurrentTableBackground : null);
    }

    private static void ApplyInstanceVisualsToAllTiles()
    {
        ForEachVisualMaterial(SyncSharedVisualsToMaterial);
        ForEachTile3D(ApplyInstanceVisuals);
    }

    /// <summary>启动或切换设置后调用：读取 ConfigManager 并应用。</summary>
    public static void ApplySavedConfig()
    {
        if (ConfigManager.Instance == null) return;
        Color color = ConfigManager.Instance.CardBackColor;
        Texture2D texture = LoadSavedTexture();
        Apply(color, texture);
        ApplySideColor(ConfigManager.Instance.SideColor);
        BackEdgeSyncEnabled = ConfigManager.Instance.BackEdgeSyncEnabled;
        BackEdgeMode = ConfigManager.Instance.BackEdgeMode;
        ApplyBackEdgeColor(ResolveBackEdgeColor(BackEdgeMode, ConfigManager.Instance.BackEdgeColor));
        LoadSavedHandBackground();
        LoadSavedHandBack();
        LoadSavedTableBackground();
        FrontEdgeMode = ConfigManager.Instance.FrontEdgeMode;
        CurrentFrontEdgeColor = ConfigManager.Instance.FrontEdgeColor;
        // 先确保共享材质上的 _FrontBgTex 与 CurrentTableBackground 已同步，
        // 再按 UseTableFaceBackground 开关只切 blend，不会清空已上传的纹理。
        ApplyFrontEdgeColor(ResolveFrontEdgeColor(FrontEdgeMode, ConfigManager.Instance.FrontEdgeColor));
        SetTableFaceBackgroundEnabled(ConfigManager.Instance.UseTableFaceBackground);
        ApplyInstanceVisualsToAllTiles();
    }

    /// <summary>把正面侧边颜色应用到共享材质与所有 Tile3D 实例。</summary>
    public static void ApplySideColor(Color color)
    {
        CurrentSideColor = color;

        ForEachVisualMaterial(mat => mat.SetColor("_SideColor", color));
        ForEachTile3D(tile => tile.ApplySideVisual(color));
    }

    /// <summary>把背面侧边颜色应用到共享材质与所有 Tile3D 实例。</summary>
    public static void ApplyBackEdgeColor(Color color)
    {
        CurrentBackEdgeColor = color;

        ForEachVisualMaterial(mat => mat.SetColor("_BackEdgeColor", color));
        ForEachTile3D(tile => tile.ApplyBackEdgeVisual(color));
    }

    /// <summary>
    /// 设置背面侧边颜色同步开关：同步开启时背面侧边跟随牌背颜色，关闭后可单独设置。
    /// </summary>
    public static void SetBackEdgeSync(bool enabled)
    {
        SetBackEdgeMode(
            enabled ? CardEdgePanel.BackEdgeMode.FollowBack : CardEdgePanel.BackEdgeMode.Independent,
            ConfigManager.Instance != null ? ConfigManager.Instance.BackEdgeColor : CurrentBackEdgeColor);
    }

    /// <summary>
    /// 设置背面侧边颜色模式：独立 / 跟随牌背 / 跟随正面边缘。
    /// </summary>
    public static void SetBackEdgeMode(CardEdgePanel.BackEdgeMode mode, Color independentColor)
    {
        BackEdgeMode = mode;
        BackEdgeSyncEnabled = mode == CardEdgePanel.BackEdgeMode.FollowBack;
        ApplyBackEdgeColor(ResolveBackEdgeColor(mode, independentColor));
    }

    public static Color ResolveBackEdgeColor(CardEdgePanel.BackEdgeMode mode, Color independentColor)
    {
        switch (mode)
        {
            case CardEdgePanel.BackEdgeMode.FollowBack:
                return CurrentColor;
            case CardEdgePanel.BackEdgeMode.FollowFront:
                return ConfigManager.Instance != null ? ConfigManager.Instance.FrontEdgeColor : independentColor;
            default:
                return independentColor;
        }
    }

    /// <summary>
    /// 保证保存的牌背配置至少应用一次（对局开始取牌、面板打开等入口都能触发）。
    /// ConfigManager 尚未就绪时不置位，留待下次再试。
    /// </summary>
    public static void EnsureSavedConfigApplied()
    {
        if (_savedConfigApplied) return;
        if (ConfigManager.Instance == null) return;
        _savedConfigApplied = true;
        ApplySavedConfig();
    }

    /// <summary>把牌背颜色与图片应用到共享材质 + 所有 Tile3D。</summary>
    public static void Apply(Color color, Texture2D texture)
    {
        CurrentColor = color;
        CurrentTexture = texture;

        ForEachVisualMaterial(mat =>
        {
            mat.SetColor("_BackColor", color);
            mat.SetTexture("_BackTex", texture);
            mat.SetFloat("_BackTexBlend", texture != null ? 1f : 0f);
        });

        ForEachTile3D(tile => tile.ApplyBackVisual(color, texture));

        // 背面侧边颜色跟随牌背模式时，牌背颜色变化会连带更新背面侧边。
        if (BackEdgeMode == CardEdgePanel.BackEdgeMode.FollowBack)
        {
            ApplyBackEdgeColor(color);
        }
        _savedConfigApplied = true;
        RefreshEdgePanelPreviews();
    }

    /// <summary>读取保存的牌背图片（桌面读文件，WebGL 读 IndexedDB）。</summary>
    public static Texture2D LoadSavedTexture()
    {
        if (ConfigManager.Instance == null) return null;
        (string path, bool isCustom) = ConfigManager.Instance.GetSelectedCardBackImage();
        if (string.IsNullOrEmpty(path) || !isCustom) return null;

#if UNITY_WEBGL && !UNITY_EDITOR
        return UnityAssetIdb.LoadTexture(path);
#else
        if (!File.Exists(path)) return null;
        try { return LoadTextureFromFile(path); }
        catch (Exception e)
        {
            Debug.LogWarning($"牌背图片读取失败: {e.Message}");
            return null;
        }
#endif
    }

    /// <summary>从磁盘文件加载纹理。</summary>
    public static Texture2D LoadTextureFromFile(string filePath)
    {
        if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath)) return null;
        try
        {
            byte[] data = File.ReadAllBytes(filePath);
            Texture2D tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (ImageConversion.LoadImage(tex, data, false)) return tex;
            UnityEngine.Object.Destroy(tex);
        }
        catch (Exception e)
        {
            Debug.LogWarning($"加载牌背图片失败: {filePath}, {e.Message}");
        }
        return null;
    }

    public static Texture2D LoadSavedHandBackground()
    {
        if (_handBgLoaded) return CurrentHandBackground;
        _handBgLoaded = true;
        if (ConfigManager.Instance == null) return null;
        (string path, bool isCustom) = ConfigManager.Instance.GetSelectedHandBackground();
        if (string.IsNullOrEmpty(path) || !isCustom) return null;
#if UNITY_WEBGL && !UNITY_EDITOR
        CurrentHandBackground = UnityAssetIdb.LoadTexture(path);
#else
        if (!File.Exists(path)) return null;
        try { CurrentHandBackground = LoadTextureFromFile(path); }
        catch (Exception e)
        {
            Debug.LogWarning($"手牌背景读取失败: {e.Message}");
            CurrentHandBackground = null;
        }
#endif
        return CurrentHandBackground;
    }

    public static void PersistHandBackground(byte[] png)
    {
        if (png == null || png.Length == 0) return;
#if UNITY_WEBGL && !UNITY_EDITOR
        UnityAssetIdb.Put(UnityAssetIdb.KeyHandBg, png, null);
        if (ConfigManager.Instance != null)
        {
            ConfigManager.Instance.SetSelectedHandBackground(UnityAssetIdb.KeyHandBg, true);
        }
        ReplaceHandBackground(UnityAssetIdb.ToTexture(png));
#else
        try
        {
            Directory.CreateDirectory(Path.Combine(Application.persistentDataPath, BackImageDirName));
            File.WriteAllBytes(HandBgFilePath, png);
            if (ConfigManager.Instance != null)
            {
                ConfigManager.Instance.SetSelectedHandBackground(HandBgFilePath, true);
            }
            ReplaceHandBackground(BytesToTexture(png));
        }
        catch (Exception e)
        {
            Debug.LogWarning("保存手牌背景失败: " + e.Message);
        }
#endif
        TileFaceResolver.NotifyHandBackgroundChanged();
    }

    public static void PersistHandBack(byte[] png)
    {
        if (png == null || png.Length == 0) return;
#if UNITY_WEBGL && !UNITY_EDITOR
        UnityAssetIdb.Put(UnityAssetIdb.KeyHandBack, png, null);
        if (ConfigManager.Instance != null)
        {
            ConfigManager.Instance.SetSelectedHandBack(UnityAssetIdb.KeyHandBack, true);
        }
        ReplaceHandBack(UnityAssetIdb.ToTexture(png));
#else
        try
        {
            Directory.CreateDirectory(Path.Combine(Application.persistentDataPath, BackImageDirName));
            File.WriteAllBytes(HandBackFilePath, png);
            if (ConfigManager.Instance != null)
            {
                ConfigManager.Instance.SetSelectedHandBack(HandBackFilePath, true);
            }
            ReplaceHandBack(BytesToTexture(png));
        }
        catch (Exception e)
        {
            Debug.LogWarning("保存手牌牌背失败: " + e.Message);
        }
#endif
        TileFaceResolver.NotifyHandBackChanged();
    }

    public static Texture2D LoadSavedHandBack()
    {
        if (_handBackLoaded) return CurrentHandBack;
        _handBackLoaded = true;
        if (ConfigManager.Instance == null) return null;
        (string path, bool isCustom) = ConfigManager.Instance.GetSelectedHandBack();
        if (string.IsNullOrEmpty(path) || !isCustom) return null;
#if UNITY_WEBGL && !UNITY_EDITOR
        CurrentHandBack = UnityAssetIdb.LoadTexture(path);
#else
        if (!File.Exists(path)) return null;
        try { CurrentHandBack = LoadTextureFromFile(path); }
        catch (Exception e)
        {
            Debug.LogWarning($"手牌牌背读取失败: {e.Message}");
            CurrentHandBack = null;
        }
#endif
        return CurrentHandBack;
    }

    public static Texture2D LoadSavedTableBackground()
    {
        if (_tableBgLoaded) return CurrentTableBackground;
        _tableBgLoaded = true;
        if (ConfigManager.Instance == null) return null;
        (string path, bool isCustom) = ConfigManager.Instance.GetSelectedTableBackground();
        if (string.IsNullOrEmpty(path) || !isCustom) return null;
#if UNITY_WEBGL && !UNITY_EDITOR
        CurrentTableBackground = UnityAssetIdb.LoadTexture(path);
#else
        if (!File.Exists(path)) return null;
        try { CurrentTableBackground = LoadTextureFromFile(path); }
        catch (Exception e)
        {
            Debug.LogWarning($"3D 牌面背景读取失败: {e.Message}");
            CurrentTableBackground = null;
        }
#endif
        // 重启时把已存的 3D 牌面背景 aspect 同步到 shader，避免首次切「使用」时仍按整张 UV 采样
        ApplyTableBackgroundAspect(CurrentTableBackground);
        return CurrentTableBackground;
    }

    public static void PersistTableBackground(byte[] png)
    {
        if (png == null || png.Length == 0) return;
#if UNITY_WEBGL && !UNITY_EDITOR
        UnityAssetIdb.Put(UnityAssetIdb.KeyTableBg, png, null);
        if (ConfigManager.Instance != null)
        {
            ConfigManager.Instance.SetSelectedTableBackground(UnityAssetIdb.KeyTableBg, true);
        }
        Texture2D tex = UnityAssetIdb.ToTexture(png);
        ReplaceTableBackground(tex);
        ApplyTableBackgroundAspect(tex);
#else
        try
        {
            Directory.CreateDirectory(Path.Combine(Application.persistentDataPath, BackImageDirName));
            File.WriteAllBytes(TableBgFilePath, png);
            if (ConfigManager.Instance != null)
            {
                ConfigManager.Instance.SetSelectedTableBackground(TableBgFilePath, true);
            }
            Texture2D tex = BytesToTexture(png);
            ReplaceTableBackground(tex);
            ApplyTableBackgroundAspect(tex);
        }
        catch (Exception e)
        {
            Debug.LogWarning("保存 3D 牌面背景失败: " + e.Message);
            return;
        }
#endif
        // 上传背景后自动开启「使用 3D 牌面背景」，避免出现图已上传但 UI 还是关闭态。
        SetTableFaceBackgroundEnabled(true);
    }

    /// <summary>把上传 3D 牌面背景的宽高比写入共享材质，shader 据此按 220:366 比例压缩 UV。</summary>
    private static void ApplyTableBackgroundAspect(Texture2D tex)
    {
        float aspect = (tex != null && tex.height > 0)
            ? (float)tex.width / tex.height
            : 0f;
        ForEachVisualMaterial(mat => mat.SetFloat("_FrontBgTexAspect", aspect));
    }

    public static void ClearPersistedTableBackground()
    {
        if (ConfigManager.Instance != null)
        {
            ConfigManager.Instance.SetSelectedTableBackground("", false);
        }
#if UNITY_WEBGL && !UNITY_EDITOR
        UnityAssetIdb.Delete(UnityAssetIdb.KeyTableBg, null);
#else
        try
        {
            if (File.Exists(TableBgFilePath)) File.Delete(TableBgFilePath);
        }
        catch (Exception e)
        {
            Debug.LogWarning("删除 3D 牌面背景失败: " + e.Message);
        }
#endif
        ReplaceTableBackground(null);
        // 清空背景后顺手关闭「使用 3D 牌面背景」，与手牌牌面背景行为一致。
        SetTableFaceBackgroundEnabled(false);
        ForEachVisualMaterial(mat => mat.SetFloat("_FrontBgTexAspect", 0f));
    }

    /// <summary>
    /// 「使用 / 不使用 3D 牌面背景」开关：只切 blend，不动 <see cref="CurrentTableBackground"/> 与磁盘存档。
    /// 持久化与纹理替换由 <see cref="PersistTableBackground"/> / <see cref="ClearPersistedTableBackground"/> 单独完成。
    /// </summary>
    public static void SetTableFaceBackgroundEnabled(bool enabled)
    {
        if (ConfigManager.Instance != null)
        {
            ConfigManager.Instance.SetUseTableFaceBackground(enabled);
        }
        ApplyInstanceVisualsToAllTiles();
        RefreshFollowedFrontEdge();
    }

    /// <summary>3D 牌面纯色：与「使用 3D 牌面背景」互斥，开启后自动关闭背景。</summary>
    public static void SetTableFaceSolidColorEnabled(bool enabled)
    {
        if (ConfigManager.Instance != null)
        {
            ConfigManager.Instance.SetTableFaceUseSolidColor(enabled);
        }
        ApplyInstanceVisualsToAllTiles();
        RefreshFollowedFrontEdge();
    }

    public static void SetTableFaceColor(Color color)
    {
        if (ConfigManager.Instance != null)
        {
            ConfigManager.Instance.SetTableFaceColor(color);
        }
        ForEachVisualMaterial(mat => mat.SetColor("_TableFaceColor", color));
        RefreshFollowedFrontEdge();
    }

    /// <summary>把 3D 牌正面侧边颜色应用到共享材质与所有 Tile3D 实例。</summary>
    public static void ApplyFrontEdgeColor(Color color)
    {
        CurrentFrontEdgeColor = color;
        ForEachVisualMaterial(mat => mat.SetColor("_FrontEdgeColor", color));
        ForEachTile3D(tile => tile.ApplyFrontEdgeVisual(color));
    }

    public static Color ResolveFrontEdgeColor(CardEdgePanel.FrontEdgeMode mode, Color independentColor)
    {
        switch (mode)
        {
            case CardEdgePanel.FrontEdgeMode.FollowTableBg:
                if (ConfigManager.Instance != null && ConfigManager.Instance.TableFaceUseSolidColor)
                    return ConfigManager.Instance.TableFaceColor;
                if (ConfigManager.Instance != null
                    && ConfigManager.Instance.UseTableFaceBackground
                    && CurrentTableBackground != null)
                    return Color.white;
                return ConfigManager.DefaultTableFaceFallbackColor;
            case CardEdgePanel.FrontEdgeMode.FollowBackEdge:
                return ConfigManager.Instance != null
                    ? ConfigManager.Instance.BackEdgeColor
                    : independentColor;
            default:
                return independentColor;
        }
    }

    /// <summary>由 CardEdgePanel / ConfigManager 调用：整体应用正面边缘模式。</summary>
    public static void SetFrontEdgeMode(CardEdgePanel.FrontEdgeMode mode, Color independentColor)
    {
        FrontEdgeMode = mode;
        Color resolved = ResolveFrontEdgeColor(mode, independentColor);
        if (ConfigManager.Instance != null)
        {
            ConfigManager.Instance.SetFrontEdgeColor(independentColor);
            ConfigManager.Instance.SetFrontEdgeMode(mode);
        }
        CurrentFrontEdgeColor = resolved;
        ApplyFrontEdgeColor(resolved);
        ApplyInstanceVisualsToAllTiles();
    }

    private static bool ResolveTableBgCoverFace()
    {
        CardEdgePanel.FrontEdgeMode mode = ConfigManager.Instance != null
            ? ConfigManager.Instance.FrontEdgeMode
            : FrontEdgeMode;
        return mode == CardEdgePanel.FrontEdgeMode.FollowTableBg;
    }

    private static void RefreshFollowedFrontEdge()
    {
        CardEdgePanel.FrontEdgeMode mode = ConfigManager.Instance != null
            ? ConfigManager.Instance.FrontEdgeMode
            : FrontEdgeMode;
        Color independent = ConfigManager.Instance != null
            ? ConfigManager.Instance.FrontEdgeColor
            : CurrentFrontEdgeColor;
        ApplyFrontEdgeColor(ResolveFrontEdgeColor(mode, independent));
        RefreshEdgePanelPreviews();
    }

    private static void RefreshEdgePanelPreviews()
    {
        if (CardEdgePanel.Instance != null)
        {
            CardEdgePanel.Instance.RefreshPreviews();
        }
    }

    public static void ClearPersistedHandBack()
    {
        if (ConfigManager.Instance != null)
        {
            ConfigManager.Instance.SetSelectedHandBack("", false);
        }
#if UNITY_WEBGL && !UNITY_EDITOR
        UnityAssetIdb.Delete(UnityAssetIdb.KeyHandBack, null);
#else
        try
        {
            if (File.Exists(HandBackFilePath)) File.Delete(HandBackFilePath);
        }
        catch (Exception e)
        {
            Debug.LogWarning("删除手牌牌背失败: " + e.Message);
        }
#endif
        ReplaceHandBack(null);
        TileFaceResolver.NotifyHandBackChanged();
    }

    public static void PersistCardBackImage(byte[] png)
    {
        if (png == null || png.Length == 0) return;
        Texture2D tex;
#if UNITY_WEBGL && !UNITY_EDITOR
        UnityAssetIdb.Put(UnityAssetIdb.KeyCardBack, png, null);
        if (ConfigManager.Instance != null)
        {
            ConfigManager.Instance.SetSelectedCardBackImage(UnityAssetIdb.KeyCardBack, true);
        }
        tex = UnityAssetIdb.ToTexture(png);
#else
        try
        {
            Directory.CreateDirectory(Path.Combine(Application.persistentDataPath, BackImageDirName));
            string target = Path.Combine(Application.persistentDataPath, BackImageDirName,
                "CardBack_" + DateTime.Now.ToString("yyyyMMddHHmmssfff") + ".png");
            File.WriteAllBytes(target, png);
            if (ConfigManager.Instance != null)
            {
                ConfigManager.Instance.SetSelectedCardBackImage(target, true);
            }
            tex = BytesToTexture(png);
        }
        catch (Exception e)
        {
            Debug.LogWarning("保存牌背图片失败: " + e.Message);
            return;
        }
#endif
        Color color = ConfigManager.Instance != null ? ConfigManager.Instance.CardBackColor : CurrentColor;
        Apply(color, tex);
    }

    public static void ClearPersistedCardBack()
    {
        if (ConfigManager.Instance != null)
        {
            ConfigManager.Instance.SetSelectedCardBackImage("", false);
        }
#if UNITY_WEBGL && !UNITY_EDITOR
        UnityAssetIdb.Delete(UnityAssetIdb.KeyCardBack, null);
#endif
        Color color = ConfigManager.Instance != null ? ConfigManager.Instance.CardBackColor : CurrentColor;
        Apply(color, null);
    }

    public static void ClearPersistedHandBackground()
    {
        if (ConfigManager.Instance != null)
        {
            ConfigManager.Instance.SetSelectedHandBackground("", false);
        }
#if UNITY_WEBGL && !UNITY_EDITOR
        UnityAssetIdb.Delete(UnityAssetIdb.KeyHandBg, null);
#else
        try
        {
            if (File.Exists(HandBgFilePath)) File.Delete(HandBgFilePath);
        }
        catch (Exception e)
        {
            Debug.LogWarning("删除手牌背景失败: " + e.Message);
        }
#endif
        ReplaceHandBackground(null);
        TileFaceResolver.NotifyHandBackgroundChanged();
    }

    public static bool IsZip(byte[] bytes)
    {
        return bytes != null && bytes.Length >= 4 && bytes[0] == 0x50 && bytes[1] == 0x4B;
    }

    public static bool TryParseBodyZip(byte[] bytes, out byte[] backPng, out byte[] handBgPng)
    {
        backPng = null;
        handBgPng = null;
        if (!IsZip(bytes)) return false;
        try
        {
            using (var stream = new MemoryStream(bytes, false))
            using (var archive = new ZipArchive(stream, ZipArchiveMode.Read, true))
            {
                foreach (ZipArchiveEntry entry in archive.Entries)
                {
                    if (string.IsNullOrEmpty(entry.Name) || entry.FullName.EndsWith("/")) continue;
                    string name = Path.GetFileName(entry.FullName).ToLowerInvariant();
                    if (!name.EndsWith(".png")) continue;
                    byte[] png;
                    using (Stream open = entry.Open())
                    using (var memory = new MemoryStream())
                    {
                        open.CopyTo(memory);
                        png = memory.ToArray();
                    }
                    if (IsHandBgFileName(name)) handBgPng = png;
                    else if (IsBackFileName(name)) backPng = png;
                }
            }
        }
        catch
        {
            return false;
        }
        return backPng != null || handBgPng != null;
    }

    public static bool TryParseFaceBodyZip(byte[] bytes, out byte[] handBackPng, out byte[] handBgPng)
    {
        handBackPng = null;
        handBgPng = null;
        if (!IsZip(bytes)) return false;
        try
        {
            using (var stream = new MemoryStream(bytes, false))
            using (var archive = new ZipArchive(stream, ZipArchiveMode.Read, true))
            {
                foreach (ZipArchiveEntry entry in archive.Entries)
                {
                    if (string.IsNullOrEmpty(entry.Name) || entry.FullName.EndsWith("/")) continue;
                    string name = Path.GetFileName(entry.FullName).ToLowerInvariant();
                    if (!name.EndsWith(".png")) continue;
                    byte[] png;
                    using (Stream open = entry.Open())
                    using (var memory = new MemoryStream())
                    {
                        open.CopyTo(memory);
                        png = memory.ToArray();
                    }
                    if (IsHandBgFileName(name)) handBgPng = png;
                    else if (IsHandBackFileName(name)) handBackPng = png;
                }
            }
        }
        catch
        {
            return false;
        }
        return handBackPng != null || handBgPng != null;
    }

    public static bool TryParseTableBgZip(byte[] bytes, out byte[] tableBgPng)
    {
        tableBgPng = null;
        if (!IsZip(bytes)) return false;
        try
        {
            using (var stream = new MemoryStream(bytes, false))
            using (var archive = new ZipArchive(stream, ZipArchiveMode.Read, true))
            {
                foreach (ZipArchiveEntry entry in archive.Entries)
                {
                    if (string.IsNullOrEmpty(entry.Name) || entry.FullName.EndsWith("/")) continue;
                    string name = Path.GetFileName(entry.FullName).ToLowerInvariant();
                    if (!name.EndsWith(".png")) continue;
                    if (!IsTableBgFileName(name)) continue;
                    using (Stream open = entry.Open())
                    using (var memory = new MemoryStream())
                    {
                        open.CopyTo(memory);
                        tableBgPng = memory.ToArray();
                    }
                    break;
                }
            }
        }
        catch
        {
            return false;
        }
        return tableBgPng != null;
    }

    public static bool IsHandBgFileName(string name)
    {
        if (string.IsNullOrEmpty(name)) return false;
        string lower = Path.GetFileName(name).ToLowerInvariant();
        return lower.Contains("hand-bg") || lower.Contains("handbg") || lower == "front.png";
    }

    public static bool IsHandBackFileName(string name)
    {
        if (string.IsNullOrEmpty(name)) return false;
        string lower = Path.GetFileName(name).ToLowerInvariant();
        if (IsHandBgFileName(lower)) return false;
        return lower.Contains("hand-back")
            || lower.Contains("handback")
            || lower.Contains("hand_back")
            || lower == "0.png"
            || lower == "back.png"
            || lower.Contains("ura-back");
    }

    public static bool IsBackFileName(string name)
    {
        if (string.IsNullOrEmpty(name)) return false;
        string lower = Path.GetFileName(name).ToLowerInvariant();
        return lower.Contains("cardback") || lower == "back.png" || (lower.Contains("back") && !IsHandBgFileName(lower) && !IsTableBgFileName(lower));
    }

    public static bool IsTableBgFileName(string name)
    {
        if (string.IsNullOrEmpty(name)) return false;
        string lower = Path.GetFileName(name).ToLowerInvariant();
        if (IsHandBgFileName(lower)) return false;
        return lower.Contains("table-bg")
            || lower.Contains("tablebg")
            || lower.Contains("table_bg")
            || lower == "table.png"
            || lower.Contains("3d-bg");
    }

    private static void ReplaceHandBackground(Texture2D texture)
    {
        if (CurrentHandBackground != null && CurrentHandBackground != texture)
        {
            UnityEngine.Object.Destroy(CurrentHandBackground);
        }
        CurrentHandBackground = texture;
        _handBgLoaded = true;
    }

    private static void ReplaceHandBack(Texture2D texture)
    {
        if (CurrentHandBack != null && CurrentHandBack != texture)
        {
            UnityEngine.Object.Destroy(CurrentHandBack);
        }
        CurrentHandBack = texture;
        _handBackLoaded = true;
    }

    private static void ReplaceTableBackground(Texture2D texture)
    {
        if (CurrentTableBackground != null && CurrentTableBackground != texture)
        {
            UnityEngine.Object.Destroy(CurrentTableBackground);
        }
        CurrentTableBackground = texture;
        _tableBgLoaded = true;
    }

    public static Texture2D DecodePng(byte[] png)
    {
        return BytesToTexture(png);
    }

    private static Texture2D BytesToTexture(byte[] png)
    {
        if (png == null || png.Length == 0) return null;
        var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        if (!ImageConversion.LoadImage(texture, png, false))
        {
            UnityEngine.Object.Destroy(texture);
            return null;
        }
        return texture;
    }
}
