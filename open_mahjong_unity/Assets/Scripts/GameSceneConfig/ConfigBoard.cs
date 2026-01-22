using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.IO;
using System.Runtime.InteropServices;
using System;

public class ConfigBoard : MonoBehaviour {
    [SerializeField] private Button uploadTableclothButton; // 上传桌布按钮
    [SerializeField] private Button resetTableclothButton; // 重置桌布按钮
    [SerializeField] private TMP_Text tableclothStatusText; // 桌布状态文本
    [SerializeField] private Desktop tableclothRenderer; // 桌布渲染控制器
    [SerializeField] private Image tableclothPreviewImage; // 桌布预览图片
    [SerializeField] private Texture2D defaultTableclothImage; // 默认桌布图片

    private const string TABLECLOTH_PATH_KEY = "CustomTableclothPath"; // 自定义桌布路径的PlayerPrefs键名

#if UNITY_WEBGL && !UNITY_EDITOR
    // WebGL 平台：调用 JS 插件上传文件
    [DllImport("__Internal")]
    private static extern void UploadTablecloth(string gameObjectName, string methodName, string filter);
#endif

    /// <summary>
    /// 初始化配置面板
    /// </summary>
    public void Init() {
        uploadTableclothButton.onClick.AddListener(OnUploadTableclothButtonClick);
        resetTableclothButton.onClick.AddListener(OnResetTableclothButtonClick);
        UpdateTableclothStatus();
        UpdateTableclothPreview(); // 初始化时更新预览
    }

    /// <summary>
    /// 上传桌布按钮点击事件
    /// </summary>
    private void OnUploadTableclothButtonClick() {
#if UNITY_ANDROID || UNITY_IOS
        // 移动平台：使用 NativeFilePicker
        NativeFilePicker.PickFile(path => {
            if (!string.IsNullOrEmpty(path)) {
                UploadTableclothFromPath(path);
            }
        }, new string[] { "png", "jpg", "jpeg" });
#elif UNITY_WEBGL && !UNITY_EDITOR
        // WebGL 平台：使用 JS 插件上传文件
        UpdateTableclothStatus("正在选择文件...");
        UploadTablecloth(gameObject.name, "OnTableclothFileSelected", "image/png,image/jpeg,image/jpg");
#elif UNITY_STANDALONE || UNITY_EDITOR
        // 桌面平台：使用 StandaloneFileBrowser
        var extensions = new[] {
            new SFB.ExtensionFilter("Image Files", "png", "jpg", "jpeg")
        };
        string[] paths = SFB.StandaloneFileBrowser.OpenFilePanel("选择桌布图片", "", extensions, false);
        if (paths.Length > 0 && !string.IsNullOrEmpty(paths[0])) {
            UploadTableclothFromPath(paths[0]);
        }
#endif
    }

    /// <summary>
    /// WebGL 平台：JS 插件回调方法，接收文件数据
    /// 数据格式：base64Data|fileName|fileExtension
    /// </summary>
    private void OnTableclothFileSelected(string data) {
        if (string.IsNullOrEmpty(data)) {
            UpdateTableclothStatus("未选择文件");
            return;
        }

        try {
            // 解析数据：base64Data|fileName|fileExtension
            string[] parts = data.Split('|');
            if (parts.Length < 3) {
                UpdateTableclothStatus("文件数据格式错误");
                return;
            }

            string base64Data = parts[0]; // 包含 data:image/xxx;base64, 前缀
            string fileName = parts[1];
            string fileExtension = parts[2];

            // 提取 base64 数据（移除 data:image/xxx;base64, 前缀）
            int base64Index = base64Data.IndexOf(",");
            if (base64Index < 0) {
                UpdateTableclothStatus("文件数据格式错误");
                return;
            }
            string base64Content = base64Data.Substring(base64Index + 1);

            // 将 base64 转换为字节数组
            byte[] fileData = Convert.FromBase64String(base64Content);

            // 保存文件
            if (UploadTableclothFromBytes(fileData, fileExtension)) {
                UpdateTableclothStatus("上传成功");
            } else {
                UpdateTableclothStatus("上传失败");
            }
        } catch (Exception e) {
            Debug.LogError($"处理上传文件时出错: {e.Message}");
            UpdateTableclothStatus($"上传失败: {e.Message}");
        }
    }

    /// <summary>
    /// 从文件路径上传桌布（桌面和移动平台使用）
    /// </summary>
    public bool UploadTableclothFromPath(string filePath){
        if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath)) {
            UpdateTableclothStatus("文件不存在");
            return false;
        }

        string extension = Path.GetExtension(filePath).ToLower();
        if (extension != ".png" && extension != ".jpg" && extension != ".jpeg"){
            UpdateTableclothStatus("不支持的文件格式");
            return false;
        }

        try {
            // 读取文件数据
            byte[] fileData = File.ReadAllBytes(filePath);
            return UploadTableclothFromBytes(fileData, extension);
        } catch (System.Exception e) {
            Debug.LogError($"上传桌布时出错: {e.Message}");
            UpdateTableclothStatus($"上传失败: {e.Message}");
            return false;
        }
    }

    /// <summary>
    /// 从字节数组上传桌布（通用方法，支持所有平台）
    /// </summary>
    private bool UploadTableclothFromBytes(byte[] fileData, string extension) {
        if (fileData == null || fileData.Length == 0) {
            UpdateTableclothStatus("文件数据为空");
            return false;
        }

        if (extension != ".png" && extension != ".jpg" && extension != ".jpeg") {
            UpdateTableclothStatus("不支持的文件格式");
            return false;
        }

        try {
            string tableclothDir = Desktop.GetTableclothDirectory();
            if (!Directory.Exists(tableclothDir)) {
                Directory.CreateDirectory(tableclothDir);
            }

            string fileName = $"tablecloth_{System.DateTime.Now.Ticks}{extension}";
            string targetPath = Path.Combine(tableclothDir, fileName);
            
            // 写入文件数据
            File.WriteAllBytes(targetPath, fileData);

            PlayerPrefs.SetString(TABLECLOTH_PATH_KEY, targetPath);
            PlayerPrefs.Save();

            tableclothRenderer.RefreshTablecloth();
            UpdateTableclothPreview(targetPath); // 更新预览图片
            return true;
        } catch (System.Exception e) {
            Debug.LogError($"上传桌布时出错: {e.Message}");
            UpdateTableclothStatus($"上传失败: {e.Message}");
            return false;
        }
    }

    /// <summary>
    /// 重置桌布按钮点击事件
    /// </summary>
    private void OnResetTableclothButtonClick() {
        PlayerPrefs.DeleteKey(TABLECLOTH_PATH_KEY);
        PlayerPrefs.Save();
        tableclothRenderer.RefreshTablecloth();
        UpdateTableclothStatus("已重置为默认桌布");
        UpdateTableclothPreview(); // 重置为默认预览
    }

    /// <summary>
    /// 更新桌布状态显示
    /// </summary>
    private void UpdateTableclothStatus(string customMessage = null) {
        if (!string.IsNullOrEmpty(customMessage)) {
            tableclothStatusText.text = customMessage;
            return;
        }

        string customPath = PlayerPrefs.GetString(TABLECLOTH_PATH_KEY, "");
        if (!string.IsNullOrEmpty(customPath) && File.Exists(customPath)) {
            tableclothStatusText.text = $"当前桌布: {Path.GetFileName(customPath)}";
        } else {
            tableclothStatusText.text = "当前桌布: 默认桌布";
        }
    }

    /// <summary>
    /// 更新桌布预览图片
    /// </summary>
    private void UpdateTableclothPreview(string customPath = null) {
        if (tableclothPreviewImage == null) return;

        Texture2D textureToShow = null;

        // 如果提供了自定义路径，尝试加载
        if (!string.IsNullOrEmpty(customPath) && File.Exists(customPath)) {
            textureToShow = LoadTextureFromFile(customPath);
        } else {
            // 检查PlayerPrefs中是否有保存的路径
            string savedPath = PlayerPrefs.GetString(TABLECLOTH_PATH_KEY, "");
            if (!string.IsNullOrEmpty(savedPath) && File.Exists(savedPath)) {
                textureToShow = LoadTextureFromFile(savedPath);
            }
        }

        // 如果没有自定义纹理，使用默认图片
        if (textureToShow == null && defaultTableclothImage != null) {
            textureToShow = defaultTableclothImage;
        }

        // 将Texture2D转换为Sprite并显示
        if (textureToShow != null) {
            Sprite sprite = Sprite.Create(
                textureToShow,
                new Rect(0, 0, textureToShow.width, textureToShow.height),
                new Vector2(0.5f, 0.5f)
            );
            tableclothPreviewImage.sprite = sprite;
        }
    }

    /// <summary>
    /// 从文件路径加载纹理（用于预览）
    /// </summary>
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
            Debug.LogError($"加载预览纹理文件时出错: {filePath}, 错误: {e.Message}");
            return null;
        }
    }
}
