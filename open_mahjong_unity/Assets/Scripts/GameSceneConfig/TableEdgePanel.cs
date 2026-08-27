using UnityEngine;
using UnityEngine.UI;
using System.IO;
using System.Collections.Generic;

public class TableEdgePanel : MonoBehaviour {
    public GameObject tableEdgePrefab; // 边框预制体
    public Transform contentParent; // ScrollView的Content父对象
    [SerializeField] public Button deleteButton; // 删除按钮

    private List<GameObject> tableEdgeItems = new List<GameObject>();
    private int customLoadSerial;

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
            AddTableEdgeItem(texture, texture.name, false);
        }
    }

    // 加载玩家上传的边框
    void LoadCustomTableEdges() {
#if UNITY_WEBGL && !UNITY_EDITOR
        int serial = ++customLoadSerial;
        UnityAssetIdb.EnsureReady(() => {
            if (serial != customLoadSerial) {
                return;
            }
            LoadCustomTableEdgesFromIndexedDb();
        });
#else
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
            Texture2D texture = LoadTextureFromFile(filePath);
            if (texture != null) {
                AddTableEdgeItem(texture, filePath, true);
            }
        }
    }

    // 从 IndexedDB 加载自定义桌边（WebGL）
    void LoadCustomTableEdgesFromIndexedDb() {
        List<string> keys = UnityAssetIdb.KeysWithPrefix(UnityAssetIdb.PrefixTableEdge);
        for (int i = 0; i < keys.Count; i++) {
            Texture2D texture = UnityAssetIdb.LoadTexture(keys[i]);
            if (texture == null) {
                continue;
            }
            AddTableEdgeItem(texture, keys[i], true);
        }
    }

    void AddTableEdgeItem(Texture2D texture, string path, bool custom) {
        GameObject item = Instantiate(tableEdgePrefab, contentParent);
        TableEdge tableEdge = item.GetComponent<TableEdge>();
        Sprite sprite = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), new Vector2(0.5f, 0.5f));
        tableEdge.tableEdgeImage.sprite = sprite;
        tableEdge.tableEdgeImage.color = Color.white;
        tableEdge.filePath = path;
        tableEdge.isCustom = custom;
        tableEdgeItems.Add(item);
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
