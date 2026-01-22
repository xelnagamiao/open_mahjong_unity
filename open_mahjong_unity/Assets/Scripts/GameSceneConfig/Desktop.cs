using UnityEngine;
using System.IO;

public class Desktop : MonoBehaviour {
    [SerializeField] private MeshRenderer meshRenderer; // 目标MeshRenderer组件
    [SerializeField] private Texture2D defaultTableclothTexture; // 默认桌布纹理

    private const string TABLECLOTH_PATH_KEY = "CustomTableclothPath"; // 自定义桌布路径的PlayerPrefs键名
    private const string TABLECLOTH_DIR = "Tablecloths"; // 桌布保存目录名

    private Material tableclothMaterial; // 桌布材质（元素0）
    private Material edgeMaterial; // 边框材质（元素1）

    private void Awake() {
        RefreshTablecloth(); // 刷新桌布
        RefreshEdge(); // 刷新边框
        Debug.Log("桌布和边框渲染成功");
    }

    // 刷新桌布功能
    public void RefreshTablecloth(){
        // 获取MeshRenderer组件的材质
        meshRenderer = GetComponent<MeshRenderer>();
        Material[] materials = meshRenderer.materials;

        if (materials.Length > 0) {
            tableclothMaterial = materials[0];
        }

        // 加载桌布（使用默认纹理）
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

        if (tableclothMaterial != null) {
            tableclothMaterial.mainTexture = tableclothTexture; // 设置主纹理
        }
    }

    // 刷新边框功能
    public void RefreshEdge(){
        // 获取MeshRenderer组件的材质
        meshRenderer = GetComponent<MeshRenderer>();
        Material[] materials = meshRenderer.materials;

        if (materials.Length > 1) {
            edgeMaterial = materials[1];
        }

        // 边框功能已移除，不加载任何纹理
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
