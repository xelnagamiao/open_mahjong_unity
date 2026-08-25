using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using System.IO;

public class TableClothPanel : MonoBehaviour {
    public GameObject tableclothPrefab; // 桌布预制体
    public Transform contentParent; // ScrollView的Content父对象
    [SerializeField] public Button deleteButton; // 删除按钮

    private List<GameObject> tableclothItems = new List<GameObject>();
    private int customLoadSerial;

    // 加载所有桌布和边框资源
    public void LoadTablecloths() {
        // 隐藏删除按钮
        deleteButton.gameObject.SetActive(false);

        // 清空现有的项
        ClearTablecloths();

        // 加载Resources中的桌布纹理
        LoadTexturesFromResources("image/Board/TableCloth");

        // 加载玩家上传的桌布
        LoadCustomTablecloths();
    }

    // 加载资源文件夹中的纹理
    void LoadTexturesFromResources(string resourcePath) {
        // 加载纹理资源
        Texture2D[] textures = Resources.LoadAll<Texture2D>(resourcePath);

        foreach (Texture2D texture in textures) {
            AddTableclothItem(texture, texture.name, false);
        }
    }

    // 加载玩家上传的桌布
    void LoadCustomTablecloths() {
#if UNITY_WEBGL && !UNITY_EDITOR
        int serial = ++customLoadSerial;
        UnityAssetIdb.EnsureReady(() => {
            if (serial != customLoadSerial) {
                return;
            }
            LoadCustomTableclothsFromIndexedDb();
        });
#else
        LoadCustomTableclothsFromFileSystem();
#endif
    }

    // 从文件系统加载自定义桌布
    void LoadCustomTableclothsFromFileSystem() {
        // 获取桌布保存目录
        string tableclothDir = Path.Combine(Application.persistentDataPath, "Tablecloths");

        if (!Directory.Exists(tableclothDir)) {
            return; // 目录不存在，直接返回
        }

        // 获取所有图片文件
        string[] imageExtensions = { "*.png", "*.jpg", "*.jpeg", "*.bmp", "*.tga" };
        List<string> imageFiles = new List<string>();

        foreach (string extension in imageExtensions) {
            string[] files = Directory.GetFiles(tableclothDir, extension);
            imageFiles.AddRange(files);
        }

        foreach (string filePath in imageFiles) {
            Texture2D texture = LoadTextureFromFile(filePath);
            if (texture != null) {
                AddTableclothItem(texture, filePath, true);
            }
        }
    }

    // 从 IndexedDB 加载自定义桌布（WebGL）
    void LoadCustomTableclothsFromIndexedDb() {
        List<string> keys = UnityAssetIdb.KeysWithPrefix(UnityAssetIdb.PrefixTablecloth);
        for (int i = 0; i < keys.Count; i++) {
            Texture2D texture = UnityAssetIdb.LoadTexture(keys[i]);
            if (texture == null) {
                continue;
            }
            AddTableclothItem(texture, keys[i], true);
        }
    }

    void AddTableclothItem(Texture2D texture, string path, bool custom) {
        GameObject item = Instantiate(tableclothPrefab, contentParent);
        TableCloth tableCloth = item.GetComponent<TableCloth>();
        Sprite sprite = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), new Vector2(0.5f, 0.5f));
        tableCloth.tableClothImage.sprite = sprite;
        tableCloth.tableClothImage.color = Color.white;
        tableCloth.filePath = path;
        tableCloth.isCustom = custom;
        tableclothItems.Add(item);
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
    public void ClearTablecloths() {
        foreach (GameObject item in tableclothItems) { Destroy(item); }
        tableclothItems.Clear();
    }

    // 清除所有桌布的选中状态
    public void ClearAllTableClothSelection() {
        foreach (GameObject item in tableclothItems) {
            TableCloth tableCloth = item.GetComponent<TableCloth>();
            if (tableCloth != null) { tableCloth.tableClothChoseImage.gameObject.SetActive(false); }
        }
    }

}
