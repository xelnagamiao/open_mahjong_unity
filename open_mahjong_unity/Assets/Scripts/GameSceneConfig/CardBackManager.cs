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

    public static Color CurrentColor { get; private set; } = ConfigManager.DefaultCardBackColor;
    public static Texture2D CurrentTexture { get; private set; }
    public static Texture2D CurrentHandBackground { get; private set; }
    public static Texture2D CurrentHandBack { get; private set; }
    public static Color CurrentSideColor { get; private set; } = ConfigManager.DefaultSideColor;
    public static Color CurrentBackEdgeColor { get; private set; } = ConfigManager.DefaultBackEdgeColor;
    public static bool BackEdgeSyncEnabled { get; private set; } = true;
    public static CardEdgePanel.BackEdgeMode BackEdgeMode { get; private set; } = CardEdgePanel.BackEdgeMode.FollowBack;
    public static bool BackTexExtendEdge { get; private set; }
    public static string HandBgFilePath => Path.Combine(Application.persistentDataPath, BackImageDirName, HandBgFileName);
    public static string HandBackFilePath => Path.Combine(Application.persistentDataPath, BackImageDirName, HandBackFileName);

    private static bool _savedConfigApplied;
    private static bool _handBgLoaded;
    private static bool _handBackLoaded;

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
        ApplyBackTexExtendEdge(ConfigManager.Instance.BackTexExtendEdge);
        LoadSavedHandBackground();
        LoadSavedHandBack();
    }

    /// <summary>把正面侧边颜色应用到共享材质与所有 Tile3D 实例。</summary>
    public static void ApplySideColor(Color color)
    {
        CurrentSideColor = color;

        Material shared = Resources.Load<Material>(MaterialResourcePath);
        if (shared != null)
        {
            shared.SetColor("_SideColor", color);
        }

        Tile3D[] tiles = UnityEngine.Object.FindObjectsByType<Tile3D>(FindObjectsSortMode.None);
        foreach (Tile3D tile in tiles)
        {
            if (tile != null) tile.ApplySideVisual(color);
        }

        // 对象池内未部署的牌也要同步：FindObjectsByType 只找得到激活实例，
        // 池内 inactive 牌的实例颜色若不更新，下次 Spawn 时仍是旧的正边缘颜色。
        if (MahjongObjectPool.Instance != null)
        {
            MahjongObjectPool.Instance.ForEachPooledTile(pooled =>
            {
                Tile3D pooledTile = pooled != null ? pooled.GetComponent<Tile3D>() : null;
                if (pooledTile != null) pooledTile.ApplySideVisual(color);
            });
        }

        // 背面边缘颜色跟随正面边缘模式时，正面边缘变化会连带更新背面边缘。
        if (BackEdgeMode == CardEdgePanel.BackEdgeMode.FollowFront)
        {
            ApplyBackEdgeColor(color);
        }
    }

    /// <summary>把背面侧边颜色应用到共享材质与所有 Tile3D 实例。</summary>
    public static void ApplyBackEdgeColor(Color color)
    {
        CurrentBackEdgeColor = color;

        Material shared = Resources.Load<Material>(MaterialResourcePath);
        if (shared != null)
        {
            shared.SetColor("_BackEdgeColor", color);
        }

        Tile3D[] tiles = UnityEngine.Object.FindObjectsByType<Tile3D>(FindObjectsSortMode.None);
        foreach (Tile3D tile in tiles)
        {
            if (tile != null) tile.ApplyBackEdgeVisual(color);
        }

        if (MahjongObjectPool.Instance != null)
        {
            MahjongObjectPool.Instance.ForEachPooledTile(pooled =>
            {
                Tile3D pooledTile = pooled != null ? pooled.GetComponent<Tile3D>() : null;
                if (pooledTile != null) pooledTile.ApplyBackEdgeVisual(color);
            });
        }
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

    private static Color ResolveBackEdgeColor(CardEdgePanel.BackEdgeMode mode, Color independentColor)
    {
        switch (mode)
        {
            case CardEdgePanel.BackEdgeMode.FollowBack:
                return CurrentColor;
            case CardEdgePanel.BackEdgeMode.FollowFront:
                return CurrentSideColor;
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

        // 共享材质：之后新生成的牌自动继承
        Material shared = Resources.Load<Material>(MaterialResourcePath);
        if (shared != null)
        {
            shared.SetColor("_BackColor", color);
            shared.SetTexture("_BackTex", texture);
            // 图片叠加在牌背颜色上方：有图时 blend=1（不乘算颜色），无图时 blend=0（纯色）
            shared.SetFloat("_BackTexBlend", texture != null ? 1f : 0f);
        }

        // 已有实例：更新实例颜色 + 材质贴图
        Tile3D[] tiles = UnityEngine.Object.FindObjectsByType<Tile3D>(FindObjectsSortMode.None);
        foreach (Tile3D tile in tiles)
        {
            if (tile != null) tile.ApplyBackVisual(color, texture);
        }

        // 对象池内未部署的牌也要同步：FindObjectsByType 只找得到激活实例，
        // 池内 inactive 牌的实例颜色若不更新，下次 Spawn 时仍是旧牌背。
        if (MahjongObjectPool.Instance != null)
        {
            MahjongObjectPool.Instance.ForEachPooledTile(pooled =>
            {
                Tile3D pooledTile = pooled != null ? pooled.GetComponent<Tile3D>() : null;
                if (pooledTile != null) pooledTile.ApplyBackVisual(color, texture);
            });
        }

        // 背面侧边颜色跟随牌背模式时，牌背颜色变化会连带更新背面侧边。
        if (BackEdgeMode == CardEdgePanel.BackEdgeMode.FollowBack)
        {
            ApplyBackEdgeColor(color);
        }
        ApplyBackTexExtendEdge(BackTexExtendEdge);
        _savedConfigApplied = true;
    }

    /// <summary>牌背图片是否铺到背部边缘。</summary>
    public static void ApplyBackTexExtendEdge(bool enabled)
    {
        BackTexExtendEdge = enabled;
        float value = enabled && CurrentTexture != null ? 1f : 0f;
        Material shared = Resources.Load<Material>(MaterialResourcePath);
        if (shared != null)
        {
            shared.SetFloat("_BackTexExtendEdge", value);
        }
        if (CurrentTexture != null)
        {
            CurrentTexture.wrapMode = enabled ? TextureWrapMode.Clamp : TextureWrapMode.Repeat;
        }

        Tile3D[] tiles = UnityEngine.Object.FindObjectsByType<Tile3D>(FindObjectsSortMode.None);
        foreach (Tile3D tile in tiles)
        {
            if (tile != null) tile.ApplyBackVisual(CurrentColor, CurrentTexture);
        }
        if (MahjongObjectPool.Instance != null)
        {
            MahjongObjectPool.Instance.ForEachPooledTile(pooled =>
            {
                Tile3D pooledTile = pooled != null ? pooled.GetComponent<Tile3D>() : null;
                if (pooledTile != null) pooledTile.ApplyBackVisual(CurrentColor, CurrentTexture);
            });
        }
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
        return lower.Contains("cardback") || lower == "back.png" || (lower.Contains("back") && !IsHandBgFileName(lower));
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
