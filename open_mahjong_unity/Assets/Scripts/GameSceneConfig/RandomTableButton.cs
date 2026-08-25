using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 随机桌面：把场景里的按钮拖到 targetButton。点击后调用 Game3DManager.GenerateRandomTable()。
/// </summary>
public class RandomTableButton : MonoBehaviour
{
    [SerializeField] private Button targetButton;

    private void Awake()
    {
        if (targetButton == null) targetButton = GetComponent<Button>();
        if (targetButton == null) return;
        targetButton.onClick.RemoveListener(GenerateRandomTable);
        targetButton.onClick.AddListener(GenerateRandomTable);
    }

    public static void GenerateRandomTable()
    {
        if (Game3DManager.Instance != null)
        {
            Game3DManager.Instance.GenerateRandomTable();
        }
        else
        {
            Debug.LogWarning("Game3DManager 不存在，无法生成随机桌面");
        }
    }

    /// <summary>
    /// 进入场景设置时预览用：对局/观战/牌谱阅览中静默跳过，不弹提示。
    /// </summary>
    public static void TryGeneratePreviewTable()
    {
        if (GameSessionGuard.BlocksRandomTable) return;
        GenerateRandomTable();
    }
}
