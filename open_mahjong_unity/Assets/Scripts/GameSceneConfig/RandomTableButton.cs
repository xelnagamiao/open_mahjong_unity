using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 随机桌面：场景里把按钮拖到 targetButton。
/// </summary>
public class RandomTableButton : MonoBehaviour
{
    [SerializeField] private Button targetButton;

    private void Awake()
    {
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

    public static void TryGeneratePreviewTable()
    {
        if (GameSessionGuard.BlocksRandomTable) return;
        GenerateRandomTable();
    }
}
