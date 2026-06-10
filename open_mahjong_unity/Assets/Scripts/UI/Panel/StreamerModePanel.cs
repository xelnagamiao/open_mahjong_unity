using TMPro;
using UnityEngine;

/// <summary>
/// 主播模式面板：主播模式开启时显示，与聊天面板互斥。
/// 在场景中自行搭建 UI，并将本组件挂在面板根节点上。
/// </summary>
public class StreamerModePanel : MonoBehaviour {
    public static StreamerModePanel Instance { get; private set; }

    [SerializeField] private TMP_Text titleText;
    [SerializeField] private TMP_Text hintText;

    private void Awake() {
        if (Instance != null && Instance != this) {
            Debug.LogWarning($"发现重复的 StreamerModePanel，销毁: {gameObject.name}");
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    private void Start() {
        WindowsManager.Instance?.ApplyStreamerModePanels();
    }

    private void OnDestroy() {
        if (Instance == this) {
            Instance = null;
        }
    }
}
