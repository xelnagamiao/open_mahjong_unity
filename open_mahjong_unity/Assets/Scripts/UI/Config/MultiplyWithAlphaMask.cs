using UnityEngine;
using System.IO;

public class MultiplyWithAlphaMask : MonoBehaviour
{
    [SerializeField] private Texture2D baseTexture; // 正片叠底的基础纹理
    [SerializeField] private Texture2D defaultTableclothTexture; // 默认桌布纹理

    private const string TABLECLOTH_PATH_KEY = "CustomTableclothPath"; // 自定义桌布路径的PlayerPrefs键名
    private const string TABLECLOTH_DIR = "Tablecloths"; // 桌布保存目录名

    private Material multiplyMaterial; // 正片叠底材质
    private MeshRenderer targetMeshRenderer; // 目标MeshRenderer组件
    private static Shader multiplyShader; // 正片叠底Shader
    private static readonly int BlendTexProperty = Shader.PropertyToID("_BlendTex"); // 混合纹理属性ID
    private static readonly int MainTexProperty = Shader.PropertyToID("_MainTex"); // 主纹理属性ID

    private void Awake()
    {
        multiplyShader = Shader.Find("Custom/MultiplyBlend3D");
        targetMeshRenderer = GetComponent<MeshRenderer>();
        
        multiplyMaterial = new Material(multiplyShader);
        multiplyMaterial.SetTexture(MainTexProperty, baseTexture);
        
        Material[] materials = targetMeshRenderer.materials;
        materials[0] = multiplyMaterial;
        targetMeshRenderer.materials = materials;

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

        multiplyMaterial.SetTexture(BlendTexProperty, tableclothTexture);
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

    // 获取桌布保存的完整目录路径
    public static string GetTableclothDirectory()
    {
        return Path.Combine(Application.persistentDataPath, TABLECLOTH_DIR);
    }
}
