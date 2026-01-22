using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class TableCloth : MonoBehaviour
{
    public string filePath; // 文件路径名
    public bool isCustom = false; // 是否是玩家上传的桌布
    [SerializeField] public Image tableClothChoseImage;
    [SerializeField] public Image tableClothImage;
    [SerializeField] public Button tableClothButton;

    private string deletePath; // 待删除的文件路径

    private void Awake()
    {
        tableClothButton.onClick.AddListener(OnTableClothButtonClick);
        tableClothChoseImage.gameObject.SetActive(false);
    }

    public void OnTableClothButtonClick() { // 保存桌布选择
        ConfigManager.Instance.SetSelectedTableCloth(filePath, isCustom); // 保存选中路径到配置管理器
        FindObjectOfType<TableClothPanel>().ClearAllTableClothSelection(); // 清除其他选中状态并显示自身选中
        tableClothChoseImage.gameObject.SetActive(true); // 显示选中图片

        // 根据配置刷新桌布和边框
        RefreshDesktop();

        // 显示或隐藏删除按钮
        ShowDeleteButtonForCustomItem(); }

    // 根据ConfigManager的设置刷新桌布和边框
    private void RefreshDesktop() {
        Desktop.Instance.RefreshTablecloth();
        Desktop.Instance.RefreshEdge();
    }

    // 显示或隐藏删除按钮（仅对自定义项目）
    private void ShowDeleteButtonForCustomItem() {
        TableClothPanel panel = FindObjectOfType<TableClothPanel>();
        if (panel != null && panel.deleteButton != null) {
            if (isCustom) {
                panel.deleteButton.gameObject.SetActive(true);
                // 存储待删除路径
                deletePath = filePath;
                // 设置删除按钮的点击事件
                panel.deleteButton.onClick.RemoveAllListeners();
                panel.deleteButton.onClick.AddListener(DeleteCustomTableCloth);
            } else {
                panel.deleteButton.gameObject.SetActive(false);
            }
        }
    }

    // 删除自定义桌布
    private void DeleteCustomTableCloth() {
        if (!string.IsNullOrEmpty(deletePath)) {
            try {
#if UNITY_WEBGL && !UNITY_EDITOR
                // WebGL平台：删除PlayerPrefs中的数据
                if (PlayerPrefs.HasKey(deletePath)) {
                    PlayerPrefs.DeleteKey(deletePath);

                    // 从key列表中移除
                    string listKey = "TableclothKeysList";
                    string keysList = PlayerPrefs.GetString(listKey, "");
                    if (!string.IsNullOrEmpty(keysList)) {
                        string[] keys = keysList.Split(',');
                        List<string> updatedKeys = new List<string>(keys);
                        updatedKeys.Remove(deletePath);
                        string updatedList = string.Join(",", updatedKeys);
                        PlayerPrefs.SetString(listKey, updatedList);
                    }

                    PlayerPrefs.Save();
                    Debug.Log("成功删除WebGL桌布数据: " + deletePath);
                }
#else
                // 其他平台：删除文件系统中的文件
                if (System.IO.File.Exists(deletePath)) {
                    System.IO.File.Delete(deletePath);
                    Debug.Log("成功删除自定义桌布文件: " + deletePath);
                }
#endif

                // 刷新面板，移除已删除的项目
                SceneConfigPanel scenePanel = FindObjectOfType<SceneConfigPanel>();
                if (scenePanel != null) {
                    scenePanel.RefreshPage();
                }

                // 隐藏删除按钮
                TableClothPanel panel = FindObjectOfType<TableClothPanel>();
                if (panel != null && panel.deleteButton != null) {
                    panel.deleteButton.gameObject.SetActive(false);
                }
            } catch (System.Exception e) {
                Debug.LogError("删除失败: " + e.Message);
            }
        }
    }
}

