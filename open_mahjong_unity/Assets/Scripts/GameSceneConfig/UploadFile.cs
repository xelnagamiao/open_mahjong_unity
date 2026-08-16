using UnityEngine;
using UnityEngine.UI;
using System.IO;
using System;

public class UploadFile : MonoBehaviour {
    [SerializeField] private Button uploadButton; // 上传按钮
    [SerializeField] private string savePath = ""; // 保存路径
    // 在Inspector中输入以下路径
    // "Application.persistentDataPath/Tablecloths" = 桌布完整路径
    // "Application.persistentDataPath/TableEdges" = 边框完整路径

    private const string ImageAccept = "image/png,image/jpeg,image/jpg,image/webp";

    private void Start() {
        uploadButton.onClick.AddListener(OnUploadButtonClick);

        // 关键诊断：检查并清理可能存在的同名文件冲突
        string persistentPath = Application.persistentDataPath;
        string tableclothsPath = Path.Combine(persistentPath, "Tablecloths");

        Debug.Log($"路径诊断 - PersistentDataPath: {persistentPath}");
        Debug.Log($"路径诊断 - Tablecloths目录存在: {Directory.Exists(tableclothsPath)}");
        Debug.Log($"路径诊断 - Tablecloths文件存在: {File.Exists(tableclothsPath)}");

        // 清理可能存在的同名文件
        if (File.Exists(tableclothsPath)) {
            try {
                File.Delete(tableclothsPath);
                Debug.Log("已清理同名文件: " + tableclothsPath);
            } catch (System.Exception e) {
                Debug.LogError("清理同名文件失败: " + e.Message);
            }
        }
    }

    private void OnUploadButtonClick() {
        if (string.IsNullOrEmpty(savePath)) {
            Debug.LogError("保存路径为空，请在Inspector中设置savePath");
            return;
        }

        // 解析路径中的Application.persistentDataPath占位符
        string resolvedSavePath = savePath.Replace("Application.persistentDataPath", Application.persistentDataPath);

        // ========== 平台分发处理 ==========

#if (UNITY_ANDROID || UNITY_IOS) && !UNITY_EDITOR
        // 移动平台：从相册选择图片
        NativeGallery.GetImageFromGallery(path => {
            if (!string.IsNullOrEmpty(path)) {
                SaveFileToPath(path, resolvedSavePath);
            }
        }, "选择图片", "image/*");

#elif UNITY_WEBGL && !UNITY_EDITOR
        bool isTableEdge = resolvedSavePath.Contains("TableEdges");
        string prefix = isTableEdge ? UnityAssetIdb.PrefixTableEdge : UnityAssetIdb.PrefixTablecloth;
        UnityAssetIdb.PickAndPut(prefix, ImageAccept, (_, bytes) => {
            if (bytes == null || bytes.Length == 0) {
                Debug.LogError("WebGL: 未选择文件");
                return;
            }
            NotifyPanelRefresh();
        }, err => {
            if (!string.IsNullOrEmpty(err) && err != "empty") {
                Debug.LogError("WebGL 上传失败: " + err);
            }
        });

#else
        // 桌面平台：使用StandaloneFileBrowser
        var extensions = new[] {
            new SFB.ExtensionFilter("Image Files", "png", "jpg", "jpeg")
        };
        string[] paths = SFB.StandaloneFileBrowser.OpenFilePanel("选择文件", "", extensions, false);
        if (paths.Length > 0 && !string.IsNullOrEmpty(paths[0])) {
            SaveFileToPath(paths[0], resolvedSavePath);
        }
#endif
    }

    // ========== 文件保存方法 ==========

    // 从源文件路径保存到目标路径（桌面/Android/iOS平台）
    private void SaveFileToPath(string sourcePath, string targetPath) {
        if (!File.Exists(sourcePath)) {
            Debug.LogError("源文件不存在: " + sourcePath);
            return;
        }

        try {
            // 处理目录路径：使用时间戳生成唯一文件名，避免覆盖已有文件
            if (Directory.Exists(targetPath) || string.IsNullOrEmpty(Path.GetExtension(targetPath))) {
                string extension = Path.GetExtension(sourcePath);
                if (string.IsNullOrEmpty(extension)) {
                    extension = ".jpg";
                }
                string typePrefix = targetPath.Contains("TableEdges") ? "TableEdge" : "Tablecloth";
                string fileName = $"{typePrefix}_{DateTime.Now:yyyyMMddHHmmssfff}{extension}";
                targetPath = Path.Combine(targetPath, fileName);
            }

            // 创建目标目录
            EnsureDirectoryExists(targetPath);

            // 执行文件复制
            File.Copy(sourcePath, targetPath, false);
            Debug.Log("文件保存成功: " + targetPath);

            // 通知SceneConfigPanel刷新当前页面
            NotifyPanelRefresh();
        } catch (System.Exception e) {
            Debug.LogError($"保存文件失败: {e.Message}");
            Debug.LogError($"源路径: {sourcePath}, 目标路径: {targetPath}");
        }
    }

    // ========== 辅助方法 ==========

    // 确保目标路径的目录存在
    private void EnsureDirectoryExists(string targetPath) {
        string targetDir = Path.GetDirectoryName(targetPath);
        if (targetDir != null && !Directory.Exists(targetDir)) {
            try {
                Directory.CreateDirectory(targetDir);
                Debug.Log("创建目录成功: " + targetDir);
            } catch (System.Exception dirEx) {
                Debug.LogError($"创建目录失败: {targetDir}, 错误: {dirEx.Message}");
                if (File.Exists(targetDir)) {
                    Debug.LogError($"存在同名文件: {targetDir}，请删除该文件或更改保存路径");
                }
                throw; // 重新抛出异常
            }
        }
    }

    // 通知SceneConfigPanel刷新当前页面
    private void NotifyPanelRefresh() {
        NotificationManager.Instance.ShowTip("",true,"文件上传成功");
        SceneConfigPanel sceneConfigPanel = FindObjectOfType<SceneConfigPanel>();
        if (sceneConfigPanel != null) {
            sceneConfigPanel.RefreshPage();
            Debug.Log("已通知SceneConfigPanel刷新页面");
        } else {
            Debug.LogWarning("未找到SceneConfigPanel实例，无法刷新页面");
        }
    }
}
