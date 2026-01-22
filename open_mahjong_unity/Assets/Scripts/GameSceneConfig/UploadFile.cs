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

#if UNITY_WEBGL && !UNITY_EDITOR
    // WebGL平台：JS插件声明
    [System.Runtime.InteropServices.DllImport("__Internal")]
    private static extern void UploadFileJS(string gameObjectName, string methodName, string filter);
#endif

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

        // 根据保存路径确定是桌布还是桌边
        bool isTableCloth = resolvedSavePath.Contains("Tablecloths");
        bool isTableEdge = resolvedSavePath.Contains("TableEdges");

        // ========== 平台分发处理 ==========

#if UNITY_ANDROID || UNITY_IOS
        // 移动平台：使用NativeFilePicker
        NativeFilePicker.PickFile(path => {
            if (!string.IsNullOrEmpty(path)) {
                SaveFileToPath(path, resolvedSavePath);
            }
        }, new string[] { "png", "jpg", "jpeg" });

#elif UNITY_WEBGL && !UNITY_EDITOR
        // WebGL平台：使用JS插件
        UploadFileJS(gameObject.name, "OnFileSelected", "image/png,image/jpeg,image/jpg");
        PlayerPrefs.SetString("CurrentSavePath", resolvedSavePath);
        PlayerPrefs.SetInt("CurrentIsTableCloth", isTableCloth ? 1 : 0);
        PlayerPrefs.SetInt("CurrentIsTableEdge", isTableEdge ? 1 : 0);
        PlayerPrefs.Save();

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

#if UNITY_WEBGL && !UNITY_EDITOR
    // ========== WebGL平台专用 ==========
    // JS插件回调：处理上传的文件数据（base64格式：data|filename|extension）
    private void OnFileSelected(string data) {
        if (string.IsNullOrEmpty(data)) {
            Debug.LogError("WebGL: 未选择文件");
            return;
        }

        try {
            string[] parts = data.Split('|');
            if (parts.Length < 3) {
                Debug.LogError("WebGL: 文件数据格式错误");
                return;
            }

            // 解析base64数据（去除data:image/...;base64,前缀）
            string base64Data = parts[0];
            string fileName = parts[1];
            string fileExtension = parts[2];

            int base64Index = base64Data.IndexOf(",");
            if (base64Index < 0) {
                Debug.LogError("WebGL: base64数据格式错误");
                return;
            }

            string base64Content = base64Data.Substring(base64Index + 1);
            byte[] fileData = Convert.FromBase64String(base64Content);

            // 获取保存路径和类型信息
            string currentSavePath = PlayerPrefs.GetString("CurrentSavePath",
                savePath.Replace("Application.persistentDataPath", Application.persistentDataPath));
            bool currentIsTableCloth = PlayerPrefs.GetInt("CurrentIsTableCloth", 1) == 1;
            bool currentIsTableEdge = PlayerPrefs.GetInt("CurrentIsTableEdge", 0) == 1;

            SaveFileFromBytes(fileData, currentSavePath, fileExtension, currentIsTableCloth, currentIsTableEdge, fileName);
        } catch (Exception e) {
            Debug.LogError($"WebGL文件处理错误: {e.Message}");
        }
    }
#endif

    // ========== 文件保存方法 ==========

    // 从源文件路径保存到目标路径（桌面/Android/iOS平台）
    private void SaveFileToPath(string sourcePath, string targetPath) {
        if (!File.Exists(sourcePath)) {
            Debug.LogError("源文件不存在: " + sourcePath);
            return;
        }

        try {
            // 处理目录路径：自动添加文件名
            if (Directory.Exists(targetPath) || string.IsNullOrEmpty(Path.GetExtension(targetPath))) {
                string sourceFileName = Path.GetFileName(sourcePath);
                targetPath = Path.Combine(targetPath, sourceFileName);
            }

            // 创建目标目录
            EnsureDirectoryExists(targetPath);

            // 执行文件复制
            File.Copy(sourcePath, targetPath, true);
            Debug.Log("文件保存成功: " + targetPath);

            // 通知SceneConfigPanel刷新当前页面
            NotifyPanelRefresh();
        } catch (System.Exception e) {
            Debug.LogError($"保存文件失败: {e.Message}");
            Debug.LogError($"源路径: {sourcePath}, 目标路径: {targetPath}");
        }
    }

    // 从字节数组保存文件（支持所有平台）
    private void SaveFileFromBytes(byte[] fileData, string targetPath, string fileExtension, bool isTableCloth = true, bool isTableEdge = false, string originalFileName = "") {
        if (fileData == null || fileData.Length == 0) {
            Debug.LogError("文件数据为空");
            return;
        }

        try {
#if UNITY_WEBGL && !UNITY_EDITOR
            // WebGL平台：使用时间戳生成唯一key保存到PlayerPrefs
            string base64Data = Convert.ToBase64String(fileData);
            string timestamp = System.DateTime.Now.ToString("yyyyMMddHHmmssfff");
            string typePrefix = isTableCloth ? "Tablecloth" : "TableEdge";
            string key = $"{typePrefix}_{timestamp}";
            string dataToSave = base64Data + "|" + fileExtension + "|" + (originalFileName ?? "uploaded_file");

            PlayerPrefs.SetString(key, dataToSave);

            // 维护已保存文件的列表
            string listKey = isTableCloth ? "TableclothKeysList" : "TableEdgeKeysList";
            string existingList = PlayerPrefs.GetString(listKey, "");
            string updatedList = string.IsNullOrEmpty(existingList) ? key : existingList + "," + key;
            PlayerPrefs.SetString(listKey, updatedList);

            PlayerPrefs.Save();
            Debug.Log("WebGL文件保存成功: " + key);

            // 通知SceneConfigPanel刷新当前页面
            NotifyPanelRefresh();
#else
            // 其他平台：保存到文件系统
            // 处理目录路径：自动添加文件名
            if (Directory.Exists(targetPath) || !Path.HasExtension(targetPath)) {
                string fileName = "uploaded_texture" + fileExtension;
                targetPath = Path.Combine(targetPath, fileName);
            }

            // 创建目标目录并确保扩展名
            EnsureDirectoryExists(targetPath);
            if (string.IsNullOrEmpty(Path.GetExtension(targetPath))) {
                targetPath += fileExtension;
            }

            // 写入文件
            File.WriteAllBytes(targetPath, fileData);
            Debug.Log("文件保存成功: " + targetPath);

            // 通知SceneConfigPanel刷新当前页面
            NotifyPanelRefresh();
#endif
        } catch (System.Exception e) {
            Debug.LogError($"保存文件失败: {e.Message}");
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
