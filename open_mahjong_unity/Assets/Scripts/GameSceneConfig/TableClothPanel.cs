using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using System.IO;

public class TableClothPanel : MonoBehaviour {
    public GameObject tableclothPrefab; // 桌布预制体
    public Transform contentParent; // ScrollView的Content父对象
    [SerializeField] public Button deleteButton; // 删除按钮

    private List<GameObject> tableclothItems = new List<GameObject>();

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
            // 实例化预制体
            GameObject item = Instantiate(tableclothPrefab, contentParent);

            // 获取TableCloth脚本
            TableCloth tableCloth = item.GetComponent<TableCloth>();

            // 使用预设的Image组件设置纹理为Sprite
            Sprite sprite = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), new Vector2(0.5f, 0.5f));
            tableCloth.tableClothImage.sprite = sprite;
            tableCloth.tableClothImage.color = Color.white; // 确保颜色为白色以正确显示纹理

            // 设置TableCloth脚本属性
            tableCloth.filePath = texture.name; // 保存纹理名称作为标识符
            tableCloth.isCustom = false;

            tableclothItems.Add(item);
        }
    }

    // 加载玩家上传的桌布
    void LoadCustomTablecloths() {
#if UNITY_WEBGL && !UNITY_EDITOR
        // WebGL平台：从PlayerPrefs加载上传的文件
        LoadCustomTableclothsFromPlayerPrefs();
#else
        // 其他平台：从文件系统加载
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
            // 实例化预制体
            GameObject item = Instantiate(tableclothPrefab, contentParent);

            // 加载纹理
            Texture2D texture = LoadTextureFromFile(filePath);
            if (texture != null) {
                // 获取TableCloth脚本
                TableCloth tableCloth = item.GetComponent<TableCloth>();

                // 使用预设的Image组件设置纹理为Sprite
                Sprite sprite = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), new Vector2(0.5f, 0.5f));
                tableCloth.tableClothImage.sprite = sprite;
                tableCloth.tableClothImage.color = Color.white; // 确保颜色为白色以正确显示纹理

                // 设置TableCloth脚本属性
                tableCloth.filePath = filePath;
                tableCloth.isCustom = true;

                tableclothItems.Add(item);
            } else {
                // 加载失败，销毁预制体
                Destroy(item);
            }
        }
    }

    // 从PlayerPrefs加载自定义桌布（WebGL平台）
    void LoadCustomTableclothsFromPlayerPrefs() {
        // 遍历所有PlayerPrefs key，查找桌布相关的key
        List<string> tableclothKeys = new List<string>();

        // 从维护的key列表中获取所有桌布key
        string listKey = "TableclothKeysList";
        string keysList = PlayerPrefs.GetString(listKey, "");

        if (!string.IsNullOrEmpty(keysList)) {
            string[] keys = keysList.Split(',');
            foreach (string key in keys) {
                if (PlayerPrefs.HasKey(key)) {
                    tableclothKeys.Add(key);
                }
            }
        }

        // 兼容旧版本：查找旧的固定key
        string legacyKey = "UploadedFile_tablecloth";
        if (PlayerPrefs.HasKey(legacyKey) && !tableclothKeys.Contains(legacyKey)) {
            tableclothKeys.Add(legacyKey);
        }

        foreach (string key in tableclothKeys) {
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
                            GameObject item = Instantiate(tableclothPrefab, contentParent);

                            // 获取TableCloth脚本
                            TableCloth tableCloth = item.GetComponent<TableCloth>();

                            // 使用预设的Image组件设置纹理为Sprite
                            Sprite sprite = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), new Vector2(0.5f, 0.5f));
                            tableCloth.tableClothImage.sprite = sprite;
                            tableCloth.tableClothImage.color = Color.white; // 确保颜色为白色以正确显示纹理

                            // 设置TableCloth脚本属性
                            tableCloth.filePath = key; // 使用唯一key作为标识符
                            tableCloth.isCustom = true;

                            tableclothItems.Add(item);
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
