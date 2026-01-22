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

        var (clothPath, clothIsCustom) = ConfigManager.Instance.GetSelectedTableCloth();

        if (!string.IsNullOrEmpty(clothPath)) {
            if (clothIsCustom) {
                // 加载自定义桌布
#if UNITY_WEBGL && !UNITY_EDITOR
                // WebGL平台：从PlayerPrefs加载
                if (PlayerPrefs.HasKey(clothPath)) {
                    string data = PlayerPrefs.GetString(clothPath);
                    string[] parts = data.Split('|');
                    if (parts.Length >= 1) {
                        try {
                            byte[] fileData = System.Convert.FromBase64String(parts[0]);
                            tableclothTexture = new Texture2D(2, 2);
                            if (UnityEngine.ImageConversion.LoadImage(tableclothTexture, fileData)) {
                                // 成功加载
                            } else {
                                Destroy(tableclothTexture);
                                tableclothTexture = null;
                            }
                        } catch (System.Exception e) {
                            Debug.LogError($"加载WebGL桌布失败: {e.Message}");
                            if (tableclothTexture != null) {
                                Destroy(tableclothTexture);
                                tableclothTexture = null;
                            }
                        }
                    }
                }
#else
                // 其他平台：从文件系统加载
                if (File.Exists(clothPath)) {
                    tableclothTexture = LoadTextureFromFile(clothPath);
                }
#endif
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

        var (edgePath, edgeIsCustom) = ConfigManager.Instance.GetSelectedTableEdge();

        if (!string.IsNullOrEmpty(edgePath)) {
            if (edgeIsCustom) {
                // 加载自定义桌边
#if UNITY_WEBGL && !UNITY_EDITOR
                // WebGL平台：从PlayerPrefs加载
                if (PlayerPrefs.HasKey(edgePath)) {
                    string data = PlayerPrefs.GetString(edgePath);
                    string[] parts = data.Split('|');
                    if (parts.Length >= 1) {
                        try {
                            byte[] fileData = System.Convert.FromBase64String(parts[0]);
                            edgeTexture = new Texture2D(2, 2);
                            if (UnityEngine.ImageConversion.LoadImage(edgeTexture, fileData)) {
                                // 成功加载
                            } else {
                                Destroy(edgeTexture);
                                edgeTexture = null;
                            }
                        } catch (System.Exception e) {
                            Debug.LogError($"加载WebGL桌边失败: {e.Message}");
                            if (edgeTexture != null) {
                                Destroy(edgeTexture);
                                edgeTexture = null;
                            }
                        }
                    }
                }
#else
                // 其他平台：从文件系统加载
                if (File.Exists(edgePath)) {
                    edgeTexture = LoadTextureFromFile(edgePath);
                }
#endif
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

}
