using UnityEngine;
using System.IO;

public class TableclothOverlayController : MonoBehaviour
{
    [SerializeField] private MeshRenderer meshRenderer; // 目标MeshRenderer组件
    [SerializeField] private Texture2D defaultTableclothTexture; // 默认桌布纹理
    [SerializeField] private Texture2D overlayTexture; // 覆盖纹理（带透明通道的基础纹理）

    private const string TABLECLOTH_PATH_KEY = "CustomTableclothPath"; // 自定义桌布路径的PlayerPrefs键名
    private const string TABLECLOTH_DIR = "Tablecloths"; // 桌布保存目录名

    private Material material; // 材质实例
    
    // Shader属性ID缓存
    private static readonly int TableclothTexID = Shader.PropertyToID("_TableclothTex"); // 桌布纹理属性ID
    private static readonly int OverlayTexID = Shader.PropertyToID("_OverlayTex"); // 覆盖纹理属性ID

    private void Awake()
    {
        if (meshRenderer == null)
        {
            meshRenderer = GetComponent<MeshRenderer>();
        }

        material = meshRenderer.material;
        SetOverlayTexture(overlayTexture);
        LoadTablecloth();
    }

    // 加载桌布（优先加载玩家自定义的，如果没有则使用默认纹理）
    public void LoadTablecloth()
    {
        Texture2D tableclothTexture = null;

        string customPath = PlayerPrefs.GetString(TABLECLOTH_PATH_KEY, "");
        if (!string.IsNullOrEmpty(customPath) && File.Exists(customPath))
        {
            tableclothTexture = LoadTextureFromFile(customPath);
        }

        if (tableclothTexture == null)
        {
            tableclothTexture = defaultTableclothTexture;
        }

        SetTablecloth(tableclothTexture);
    }

    // 从文件路径加载纹理
    private Texture2D LoadTextureFromFile(string filePath)
    {
        try
        {
            byte[] fileData = File.ReadAllBytes(filePath);
            Texture2D texture = new Texture2D(2, 2);
            
            if (ImageConversion.LoadImage(texture, fileData))
            {
                return texture;
            }
            
            Destroy(texture);
            return null;
        }
        catch (System.Exception e)
        {
            Debug.LogError($"加载纹理文件时出错: {filePath}, 错误: {e.Message}");
            return null;
        }
    }

    // 设置桌布纹理
    public void SetTablecloth(Texture2D tableclothTexture)
    {
        if (tableclothTexture == null) return;
        material.SetTexture(TableclothTexID, tableclothTexture);
    }

    // 设置覆盖纹理
    public void SetOverlayTexture(Texture2D overlay)
    {
        if (overlay == null) return;
        material.SetTexture(OverlayTexID, overlay);
    }

    // 获取桌布保存的完整目录路径
    public static string GetTableclothDirectory()
    {
        return Path.Combine(Application.persistentDataPath, TABLECLOTH_DIR);
    }
}
