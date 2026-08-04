using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 随机桌面按钮挂接：自动查找名字含“随机桌面 / RandomTable”的按钮并绑定点击事件；
/// 也可手动把按钮拖到 targetButton。点击后调用 Game3DManager.GenerateRandomTable()。
/// </summary>
public class RandomTableButton : MonoBehaviour
{
    [Tooltip("可选：手动指定按钮；留空则按名字自动查找")]
    [SerializeField] private Button targetButton;

    private void Awake()
    {
        Button btn = targetButton != null ? targetButton : FindRandomTableButton();
        if (btn == null)
        {
            Debug.LogWarning("[RandomTableButton] 未找到随机桌面按钮（按钮名需包含“随机桌面”或 RandomTable）");
            return;
        }
        btn.onClick.RemoveListener(GenerateRandomTable);
        btn.onClick.AddListener(GenerateRandomTable);
    }

    /// <summary>场景设置打开时调用：把场景里手工添加的随机桌面按钮挂到生成逻辑上。</summary>
    public static void HookExistingButton()
    {
        Button btn = FindRandomTableButton();
        if (btn == null) return;
        btn.onClick.RemoveListener(GenerateRandomTable);
        btn.onClick.AddListener(GenerateRandomTable);
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

    private static Button FindRandomTableButton()
    {
        Button[] all = Object.FindObjectsByType<Button>(FindObjectsSortMode.None);
        foreach (Button b in all)
        {
            if (b == null || b.gameObject == null) continue;
            string name = b.gameObject.name;
            if (string.IsNullOrEmpty(name)) continue;
            if (!name.Contains("随机桌面") && !name.Contains("RandomTable")) continue;
            // 代码内置的牌背面盘按钮已自带监听，跳过避免重复触发
            if (CardBackConfigPanel.Instance != null && b.transform.IsChildOf(CardBackConfigPanel.Instance.transform)) continue;
            return b;
        }
        return null;
    }
}
