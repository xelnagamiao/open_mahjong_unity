using UnityEngine;
using System.IO;

public class Desktop : MonoBehaviour {
    public static Desktop Instance { get; private set; }

    [SerializeField] private MeshRenderer meshRenderer; // 目标MeshRenderer组件
    [SerializeField] private Texture2D defaultTableclothTexture; // 默认桌布纹理
    [SerializeField] private Texture2D defaultTableEdgeTexture; // 默认桌边纹理

    private Material tableclothMaterial; // 桌布材质（元素0）
    private Material edgeMaterial; // 边框材质（元素1）

    private void Awake() {
        if (Instance != null && Instance != this) {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    private void Start(){
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

        // 从ConfigManager获取桌布设置
        Texture2D tableclothTexture = null;
        string clothPath = "";
        bool clothIsCustom = false;
        if (ConfigManager.Instance != null) {
            (clothPath, clothIsCustom) = ConfigManager.Instance.GetSelectedTableCloth();
        }

        if (!string.IsNullOrEmpty(clothPath)) {
            if (clothIsCustom) {
                tableclothTexture = LoadCustomTexture(clothPath);
            } else {
                // 加载内置桌布（从Resources文件夹加载）
                string resourcePath = "image/Board/TableCloth/" + clothPath;
                tableclothTexture = Resources.Load<Texture2D>(resourcePath);
            }
        }

        // 如果没有找到纹理，使用默认纹理
        if (tableclothTexture == null && defaultTableclothTexture != null) {
            tableclothTexture = defaultTableclothTexture;
        }

        if (tableclothTexture == null) {
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

        // 从ConfigManager获取桌边设置
        Texture2D edgeTexture = null;
        string edgePath = "";
        bool edgeIsCustom = false;
        if (ConfigManager.Instance != null) {
            (edgePath, edgeIsCustom) = ConfigManager.Instance.GetSelectedTableEdge();
        }

        if (!string.IsNullOrEmpty(edgePath)) {
            if (edgeIsCustom) {
                edgeTexture = LoadCustomTexture(edgePath);
            } else {
                // 加载内置桌边（从Resources文件夹加载）
                string resourcePath = "image/Board/Edge/" + edgePath;
                edgeTexture = Resources.Load<Texture2D>(resourcePath);
            }
        }

        // 如果没有找到纹理，使用默认纹理
        if (edgeTexture == null && defaultTableEdgeTexture != null) {
            edgeTexture = defaultTableEdgeTexture;
        }

        if (edgeMaterial != null && edgeTexture != null) {
            edgeMaterial.mainTexture = edgeTexture; // 设置主纹理
        }
    }

    private static Texture2D LoadCustomTexture(string path) {
#if UNITY_WEBGL && !UNITY_EDITOR
        return UnityAssetIdb.LoadTexture(path);
#else
        if (File.Exists(path)) {
            return LoadTextureFromFile(path);
        }
        return null;
#endif
    }

    // 从文件路径加载纹理
    private static Texture2D LoadTextureFromFile(string filePath){
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

}
