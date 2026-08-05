using System;
using System.IO;
using UnityEngine;

/// <summary>
/// 3D 牌背配置管理器：负责把保存的牌背颜色/图片应用到共享材质与所有已存在的 Tile3D 实例。
/// </summary>
public static class CardBackManager
{
    public const string MaterialResourcePath = "Materials/Tiles/3DTile";
    public const string BackImageDirName = "CardBacks";
    public const string WebGLImageKey = "CardBackImageData";

    public static Color CurrentColor { get; private set; } = ConfigManager.DefaultCardBackColor;
    public static Texture2D CurrentTexture { get; private set; }
    public static Color CurrentSideColor { get; private set; } = ConfigManager.DefaultSideColor;
    public static Color CurrentBackEdgeColor { get; private set; } = ConfigManager.DefaultBackEdgeColor;
    public static bool BackEdgeSyncEnabled { get; private set; } = true;
    public static CardEdgePanel.BackEdgeMode BackEdgeMode { get; private set; } = CardEdgePanel.BackEdgeMode.FollowBack;
    private static bool _savedConfigApplied;

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
        _savedConfigApplied = true;
    }

    /// <summary>读取保存的牌背图片（桌面读文件，WebGL 读 PlayerPrefs base64）。</summary>
    public static Texture2D LoadSavedTexture()
    {
        if (ConfigManager.Instance == null) return null;
        (string path, bool isCustom) = ConfigManager.Instance.GetSelectedCardBackImage();
        if (string.IsNullOrEmpty(path)) return null;

        byte[] bytes = null;
#if UNITY_WEBGL && !UNITY_EDITOR
        if (isCustom && PlayerPrefs.HasKey(path))
        {
            string data = PlayerPrefs.GetString(path);
            string[] parts = data.Split('|');
            if (parts.Length >= 1)
            {
                try { bytes = Convert.FromBase64String(parts[0]); }
                catch (Exception e) { Debug.LogWarning($"牌背图片解码失败: {e.Message}"); }
            }
        }
#else
        if (isCustom && File.Exists(path))
        {
            try { bytes = File.ReadAllBytes(path); }
            catch (Exception e) { Debug.LogWarning($"牌背图片读取失败: {e.Message}"); }
        }
#endif
        if (bytes == null || bytes.Length == 0) return null;

        Texture2D tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        if (ImageConversion.LoadImage(tex, bytes))
        {
            return tex;
        }
        UnityEngine.Object.Destroy(tex);
        return null;
    }

    /// <summary>从磁盘文件加载纹理。</summary>
    public static Texture2D LoadTextureFromFile(string filePath)
    {
        if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath)) return null;
        try
        {
            byte[] data = File.ReadAllBytes(filePath);
            Texture2D tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (ImageConversion.LoadImage(tex, data)) return tex;
            UnityEngine.Object.Destroy(tex);
        }
        catch (Exception e)
        {
            Debug.LogWarning($"加载牌背图片失败: {filePath}, {e.Message}");
        }
        return null;
    }
}
