using UnityEngine;
using UnityEngine.UI;
using System.IO;
using System.Collections.Generic;

public class TableEdgePanel : MonoBehaviour {
    public GameObject tableEdgePrefab; // 边框预制体
    public Transform contentParent; // ScrollView的Content父对象
    [SerializeField] public Button deleteButton; // 删除按钮

    private List<GameObject> tableEdgeItems = new List<GameObject>();


    // 初始化面板
    public void LoadTableEdges() {
        // 隐藏删除按钮
        deleteButton.gameObject.SetActive(false);
        
        // 清空现有的项
        ClearTableEdges();

        // 加载Resources中的边框纹理
        LoadTexturesFromResources("image/Board/Edge");

        // 加载玩家上传的边框
        LoadCustomTableEdges();
    }

    // 从Resources加载纹理
    void LoadTexturesFromResources(string resourcePath) {
        // 加载纹理资源
        Texture2D[] textures = Resources.LoadAll<Texture2D>(resourcePath);

        foreach (Texture2D texture in textures) {
            // 实例化预制体
            GameObject item = Instantiate(tableEdgePrefab, contentParent);

            // 获取TableEdge脚本
            TableEdge tableEdge = item.GetComponent<TableEdge>();

            // 使用预设的Image组件设置纹理为Sprite
            Sprite sprite = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), new Vector2(0.5f, 0.5f));
            tableEdge.tableEdgeImage.sprite = sprite;
            tableEdge.tableEdgeImage.color = Color.white; // 确保颜色为白色以正确显示纹理

            // 设置TableEdge脚本属性
            tableEdge.filePath = texture.name; // 保存纹理名称作为标识符
            tableEdge.isCustom = false;

            tableEdgeItems.Add(item);
        }
    }

    // 加载玩家上传的边框
    void LoadCustomTableEdges() {
#if UNITY_WEBGL && !UNITY_EDITOR
        // WebGL平台：从PlayerPrefs加载上传的文件
        LoadCustomTableEdgesFromPlayerPrefs();
#else
        // 其他平台：从文件系统加载
        LoadCustomTableEdgesFromFileSystem();
#endif
    }

    // 从文件系统加载自定义桌边
    void LoadCustomTableEdgesFromFileSystem() {
        // 获取边框保存目录
        string customDir = Path.Combine(Application.persistentDataPath, "TableEdges");

        if (!Directory.Exists(customDir)) {
            return; // 目录不存在，直接返回
        }

        // 获取所有图片文件
        string[] imageExtensions = { "*.png", "*.jpg", "*.jpeg", "*.bmp", "*.tga" };
        List<string> imageFiles = new List<string>();

        foreach (string extension in imageExtensions) {
            string[] files = Directory.GetFiles(customDir, extension);
            imageFiles.AddRange(files);
        }

        foreach (string filePath in imageFiles) {
            // 实例化预制体
            GameObject item = Instantiate(tableEdgePrefab, contentParent);

            // 加载纹理
            Texture2D texture = LoadTextureFromFile(filePath);
            if (texture != null) {
                // 获取TableEdge脚本
                TableEdge tableEdge = item.GetComponent<TableEdge>();

                // 使用预设的Image组件设置纹理为Sprite
                Sprite sprite = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), new Vector2(0.5f, 0.5f));
                tableEdge.tableEdgeImage.sprite = sprite;
                tableEdge.tableEdgeImage.color = Color.white; // 确保颜色为白色以正确显示纹理

                // 设置TableEdge脚本属性
                tableEdge.filePath = filePath;
                tableEdge.isCustom = true;

                tableEdgeItems.Add(item);
            } else {
                // 加载失败，销毁预制体
                Destroy(item);
            }
        }
    }

    // 从PlayerPrefs加载自定义桌边（WebGL平台）
    void LoadCustomTableEdgesFromPlayerPrefs() {
        // 遍历所有PlayerPrefs key，查找桌边相关的key
        List<string> tableEdgeKeys = new List<string>();

        // 从维护的key列表中获取所有桌边key
        string listKey = "TableEdgeKeysList";
        string keysList = PlayerPrefs.GetString(listKey, "");

        if (!string.IsNullOrEmpty(keysList)) {
            string[] keys = keysList.Split(',');
            foreach (string key in keys) {
                if (PlayerPrefs.HasKey(key)) {
                    tableEdgeKeys.Add(key);
                }
            }
        }

        // 兼容旧版本：查找旧的固定key
        string legacyKey = "UploadedFile_tableedge";
        if (PlayerPrefs.HasKey(legacyKey) && !tableEdgeKeys.Contains(legacyKey)) {
            tableEdgeKeys.Add(legacyKey);
        }

        foreach (string key in tableEdgeKeys) {
            if (PlayerPrefs.HasKey(key)) {
                string data = PlayerPrefs.GetString(key);
                string[] parts = data.Split('|');
                if (parts.Length >= 2) {
                    string base64Data = parts[0];
                    string fileExtension = parts[1];
                    string originalFileName = parts.Length >= 3 ? parts[2] : "uploaded_file";

                    try {
                        // 将base64转换为字节数组
                        byte[] fileData = System.Convert.FromBase64String(base64Data);
                        Texture2D texture = new Texture2D(2, 2);

                        if (UnityEngine.ImageConversion.LoadImage(texture, fileData)) {
                            // 实例化预制体
                            GameObject item = Instantiate(tableEdgePrefab, contentParent);

                            // 获取TableEdge脚本
                            TableEdge tableEdge = item.GetComponent<TableEdge>();

                            // 使用预设的Image组件设置纹理为Sprite
                            Sprite sprite = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), new Vector2(0.5f, 0.5f));
                            tableEdge.tableEdgeImage.sprite = sprite;
                            tableEdge.tableEdgeImage.color = Color.white; // 确保颜色为白色以正确显示纹理

                            // 设置TableEdge脚本属性
                            tableEdge.filePath = key; // 使用唯一key作为标识符
                            tableEdge.isCustom = true;

                            tableEdgeItems.Add(item);
                        }
                    } catch (System.Exception e) {
                        Debug.LogError($"加载PlayerPrefs中的纹理失败: {key}, 错误: {e.Message}");
                    }
                }
            }
        }
    }

    // 从文件路径加载纹理
    private Texture2D LoadTextureFromFile(string filePath) {
        try {
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

    // 清空显示
    public void ClearTableEdges() {
        foreach (GameObject item in tableEdgeItems) { Destroy(item); }
        tableEdgeItems.Clear();
    }

    // 清除所有桌边的选中状态
    public void ClearAllTableEdgeSelection() {
        foreach (GameObject item in tableEdgeItems) {
            TableEdge tableEdge = item.GetComponent<TableEdge>();
            if (tableEdge != null) { tableEdge.tableEdgeChoseImage.gameObject.SetActive(false); }
        }
    }
}
