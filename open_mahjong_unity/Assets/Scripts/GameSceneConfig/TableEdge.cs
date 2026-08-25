using UnityEngine;
using UnityEngine.UI;

public class TableEdge : MonoBehaviour
{
    public string filePath; // 文件路径名
    public bool isCustom = false; // 是否是玩家上传的桌边
    [SerializeField] public Image tableEdgeImage;
    [SerializeField] public Image tableEdgeChoseImage;
    [SerializeField] public Button tableEdgeButton;

    private string deletePath; // 待删除的文件路径

    private void Awake()
    {
        tableEdgeButton.onClick.AddListener(OnTableEdgeButtonClick);
        tableEdgeChoseImage.gameObject.SetActive(false);
    }

    public void OnTableEdgeButtonClick() { // 保存桌边选择
        ConfigManager.Instance.SetSelectedTableEdge(filePath, isCustom); // 保存选中路径到配置管理器
        TableEdgePanel panel = GetComponentInParent<TableEdgePanel>(true);
        if (panel != null) panel.ClearAllTableEdgeSelection();
        tableEdgeChoseImage.gameObject.SetActive(true); // 显示选中图片

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
        TableEdgePanel panel = GetComponentInParent<TableEdgePanel>(true);
        if (panel != null && panel.deleteButton != null) {
            if (isCustom) {
                panel.deleteButton.gameObject.SetActive(true);
                // 存储待删除路径
                deletePath = filePath;
                // 设置删除按钮的点击事件
                panel.deleteButton.onClick.RemoveAllListeners();
                panel.deleteButton.onClick.AddListener(DeleteCustomTableEdge);
            } else {
                panel.deleteButton.gameObject.SetActive(false);
            }
        }
    }

    // 删除自定义桌边
    private void DeleteCustomTableEdge() {
        if (string.IsNullOrEmpty(deletePath)) {
            return;
        }
        try {
#if UNITY_WEBGL && !UNITY_EDITOR
            UnityAssetIdb.Delete(deletePath, FinishDelete);
#else
            if (System.IO.File.Exists(deletePath)) {
                System.IO.File.Delete(deletePath);
                Debug.Log("成功删除自定义桌边文件: " + deletePath);
            }
            FinishDelete();
#endif
        } catch (System.Exception e) {
            Debug.LogError("删除失败: " + e.Message);
        }
    }

    private void FinishDelete() {
        SceneConfigPanel scenePanel = GetComponentInParent<SceneConfigPanel>(true);
        if (scenePanel != null) {
            scenePanel.RefreshPage();
        }
        TableEdgePanel panel = GetComponentInParent<TableEdgePanel>(true);
        if (panel != null && panel.deleteButton != null) {
            panel.deleteButton.gameObject.SetActive(false);
        }
    }
}
