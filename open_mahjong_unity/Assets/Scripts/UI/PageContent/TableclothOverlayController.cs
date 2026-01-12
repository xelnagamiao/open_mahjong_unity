using UnityEngine;
using System.IO;

public class TableclothOverlayController : MonoBehaviour {
    [SerializeField] private MeshRenderer meshRenderer; // 目标MeshRenderer组件
    [SerializeField] private Texture2D defaultTableclothTexture; // 默认桌布纹理
    [SerializeField] private Texture2D overlayTexture; // 覆盖纹理（带透明通道的基础纹理）

    private const string TABLECLOTH_PATH_KEY = "CustomTableclothPath"; // 自定义桌布路径的PlayerPrefs键名
    private const string TABLECLOTH_DIR = "Tablecloths"; // 桌布保存目录名

    private Material material; // 材质实例
    
    // Shader属性ID缓存
    private static readonly int TableclothTexID = Shader.PropertyToID("_TableclothTex"); // 桌布纹理属性ID
    private static readonly int OverlayTexID = Shader.PropertyToID("_OverlayTex"); // 覆盖纹理属性ID

    private void Awake() {
        RefreshTablecloth(); // 刷新桌布
        Debug.Log("桌布渲染成功");
    }

    // 刷新桌布功能（合并加载桌布和设置覆盖纹理的逻辑）
    public void RefreshTablecloth(){
        // 获取MeshRenderer组件的第一个材质
        meshRenderer = GetComponent<MeshRenderer>();
        material = meshRenderer.materials[0];

        // 加载桌布（优先加载玩家自定义的，如果没有则使用默认纹理）
        Texture2D tableclothTexture = null;

        string customPath = PlayerPrefs.GetString(TABLECLOTH_PATH_KEY, "");
        if (!string.IsNullOrEmpty(customPath) && File.Exists(customPath)){
            tableclothTexture = LoadTextureFromFile(customPath);
        } else if (defaultTableclothTexture != null){
            tableclothTexture = defaultTableclothTexture;
        } else {
            Debug.LogError("没有找到桌布纹理");
            return;
        }

        material.SetTexture(TableclothTexID, tableclothTexture); // 更新桌布纹理
        material.SetTexture(OverlayTexID, overlayTexture); // 更新覆盖纹理
    }

    // 从文件路径加载纹理
    private Texture2D LoadTextureFromFile(string filePath){
        try{
            byte[] fileData = File.ReadAllBytes(filePath);
            Texture2D texture = new Texture2D(2, 2);
            
            if (ImageConversion.LoadImage(texture, fileData)) {
                return texture;
            }
            
            Destroy(texture);
            return null;
        } catch (System.Exception e) {
            Debug.LogError($"加载纹理文件时出错: {filePath}, 错误: {e.Message}");
            return null;
        }
    }

    // 获取桌布保存的完整目录路径
    public static string GetTableclothDirectory() {
        return Path.Combine(Application.persistentDataPath, TABLECLOTH_DIR);
    }
}
