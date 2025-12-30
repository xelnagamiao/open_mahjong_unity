using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.IO;

public class ConfigBoard : MonoBehaviour
{
    [SerializeField] private Button uploadTableclothButton;
    [SerializeField] private Button resetTableclothButton;
    [SerializeField] private TMP_Text tableclothStatusText;
    [SerializeField] private MultiplyWithAlphaMask tableclothRenderer;

    private const string TABLECLOTH_PATH_KEY = "CustomTableclothPath";

    public void Init()
    {
        uploadTableclothButton.onClick.AddListener(OnUploadTableclothButtonClick);
        resetTableclothButton.onClick.AddListener(OnResetTableclothButtonClick);
        UpdateTableclothStatus();
    }

    private void OnUploadTableclothButtonClick()
    {
        StartCoroutine(UploadTableclothCoroutine());
    }

    private IEnumerator UploadTableclothCoroutine()
    {
        Debug.Log("请使用文件选择插件来选择桌布文件，或者通过代码调用 UploadTableclothFromPath(string filePath) 方法");
        yield return null;
    }

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
            string tableclothDir = MultiplyWithAlphaMask.GetTableclothDirectory();
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
