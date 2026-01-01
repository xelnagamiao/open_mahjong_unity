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

    private const string TABLECLOTH_PATH_KEY = "CustomTableclothPath"; // 自定义桌布路径的PlayerPrefs键名

    public void Init()
    {
        uploadTableclothButton.onClick.AddListener(OnUploadTableclothButtonClick);
        resetTableclothButton.onClick.AddListener(OnResetTableclothButtonClick);
        UpdateTableclothStatus();
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
    public bool UploadTableclothFromPath(string filePath)
    {
        if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
        {
            UpdateTableclothStatus("文件不存在");
            return false;
        }

        string extension = Path.GetExtension(filePath).ToLower();
        if (extension != ".png" && extension != ".jpg" && extension != ".jpeg")
        {
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

            tableclothRenderer.LoadTablecloth();
            UpdateTableclothStatus("上传成功");
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
        tableclothRenderer.LoadTablecloth();
        UpdateTableclothStatus("已重置为默认桌布");
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
}
