using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using System.IO;

public class TableClothPanel : MonoBehaviour {
    public GameObject tableclothPrefab; // 桌布预制体
    public Transform contentParent; // ScrollView的Content父对象

    private List<GameObject> tableclothItems = new List<GameObject>();

    // 加载所有桌布和边框资源
    public void LoadTablecloths() {
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
            tableCloth.filePath = "";
            tableCloth.isCustom = false;

            tableclothItems.Add(item);
        }
    }

    // 加载玩家上传的桌布
    void LoadCustomTablecloths() {
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
}
