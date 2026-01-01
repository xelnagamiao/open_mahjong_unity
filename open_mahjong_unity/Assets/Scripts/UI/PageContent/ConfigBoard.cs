using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.IO;
using SFB;

public class ConfigBoard : MonoBehaviour
{
    [SerializeField] private Button uploadTableclothButton; // 上传桌布按钮
    [SerializeField] private Button resetTableclothButton; // 重置桌布按钮
    [SerializeField] private TMP_Text tableclothStatusText; // 桌布状态文本
    [SerializeField] private TableclothOverlayController tableclothRenderer; // 桌布渲染控制器
    [SerializeField] private Image tableclothPreviewImage; // 桌布预览图片
    [SerializeField] private Texture2D defaultTableclothImage; // 默认桌布图片

    private const string TABLECLOTH_PATH_KEY = "CustomTableclothPath"; // 自定义桌布路径的PlayerPrefs键名

    public void Init()
    {
        uploadTableclothButton.onClick.AddListener(OnUploadTableclothButtonClick);
        resetTableclothButton.onClick.AddListener(OnResetTableclothButtonClick);
        UpdateTableclothStatus();
        UpdateTableclothPreview(); // 初始化时更新预览
    }

    private void OnUploadTableclothButtonClick()
    {
#if UNITY_ANDROID || UNITY_IOS
        // 使用NativeFilePicker
        NativeFilePicker.PickFile(path => {
            if (!string.IsNullOrEmpty(path))
            {
                UploadTableclothFromPath(path);
            }
        }, new string[] { "png", "jpg", "jpeg" });
#elif UNITY_STANDALONE || UNITY_EDITOR
        // 使用StandaloneFileBrowser
        var extensions = new[] {
            new ExtensionFilter("Image Files", "png", "jpg", "jpeg")
        };
        string[] paths = StandaloneFileBrowser.OpenFilePanel("选择桌布图片", "", extensions, false);
        if (paths.Length > 0 && !string.IsNullOrEmpty(paths[0]))
        {
            UploadTableclothFromPath(paths[0]);
        }
#endif
    }

    // 从文件路径上传桌布
    public bool UploadTableclothFromPath(string filePath){
        if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
        {
            UpdateTableclothStatus("文件不存在");
            return false;
        }

        string extension = Path.GetExtension(filePath).ToLower();
        if (extension != ".png" && extension != ".jpg" && extension != ".jpeg"){
            UpdateTableclothStatus("不支持的文件格式");
            return false;
        }

        try
        {
            string tableclothDir = TableclothOverlayController.GetTableclothDirectory();
            if (!Directory.Exists(tableclothDir))
            {
                Directory.CreateDirectory(tableclothDir);
            }

            string fileName = $"tablecloth_{System.DateTime.Now.Ticks}{extension}";
            string targetPath = Path.Combine(tableclothDir, fileName);
            File.Copy(filePath, targetPath, true);

            PlayerPrefs.SetString(TABLECLOTH_PATH_KEY, targetPath);
            PlayerPrefs.Save();

            tableclothRenderer.RefreshTablecloth();
            UpdateTableclothStatus("上传成功");
            UpdateTableclothPreview(targetPath); // 更新预览图片
            return true;
        }
        catch (System.Exception e)
        {
            Debug.LogError($"上传桌布时出错: {e.Message}");
            UpdateTableclothStatus($"上传失败: {e.Message}");
            return false;
        }
    }

    private void OnResetTableclothButtonClick()
    {
        PlayerPrefs.DeleteKey(TABLECLOTH_PATH_KEY);
        PlayerPrefs.Save();
        tableclothRenderer.RefreshTablecloth();
        UpdateTableclothStatus("已重置为默认桌布");
        UpdateTableclothPreview(); // 重置为默认预览
    }

    // 更新桌布状态显示
    private void UpdateTableclothStatus(string customMessage = null)
    {
        if (!string.IsNullOrEmpty(customMessage))
        {
            tableclothStatusText.text = customMessage;
            return;
        }

        string customPath = PlayerPrefs.GetString(TABLECLOTH_PATH_KEY, "");
        if (!string.IsNullOrEmpty(customPath) && File.Exists(customPath))
        {
            tableclothStatusText.text = $"当前桌布: {Path.GetFileName(customPath)}";
        }
        else
        {
            tableclothStatusText.text = "当前桌布: 默认桌布";
        }
    }

    // 更新桌布预览图片
    private void UpdateTableclothPreview(string customPath = null)
    {
        if (tableclothPreviewImage == null) return;

        Texture2D textureToShow = null;

        // 如果提供了自定义路径，尝试加载
        if (!string.IsNullOrEmpty(customPath) && File.Exists(customPath))
        {
            textureToShow = LoadTextureFromFile(customPath);
        }
        else
        {
            // 检查PlayerPrefs中是否有保存的路径
            string savedPath = PlayerPrefs.GetString(TABLECLOTH_PATH_KEY, "");
            if (!string.IsNullOrEmpty(savedPath) && File.Exists(savedPath))
            {
                textureToShow = LoadTextureFromFile(savedPath);
            }
        }

        // 如果没有自定义纹理，使用默认图片
        if (textureToShow == null && defaultTableclothImage != null)
        {
            textureToShow = defaultTableclothImage;
        }

        // 将Texture2D转换为Sprite并显示
        if (textureToShow != null)
        {
            Sprite sprite = Sprite.Create(
                textureToShow,
                new Rect(0, 0, textureToShow.width, textureToShow.height),
                new Vector2(0.5f, 0.5f)
            );
            tableclothPreviewImage.sprite = sprite;
        }
    }

    // 从文件路径加载纹理（用于预览）
    private Texture2D LoadTextureFromFile(string filePath)
    {
        try
        {
            byte[] fileData = File.ReadAllBytes(filePath);
            Texture2D texture = new Texture2D(2, 2);
            
            if (ImageConversion.LoadImage(texture, fileData))
            {
                return texture;
            }
            
            Destroy(texture);
            return null;
        }
        catch (System.Exception e)
        {
            Debug.LogError($"加载预览纹理文件时出错: {filePath}, 错误: {e.Message}");
            return null;
        }
    }
}
