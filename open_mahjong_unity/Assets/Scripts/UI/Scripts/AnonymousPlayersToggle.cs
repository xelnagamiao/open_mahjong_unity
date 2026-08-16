using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Events;
using TMPro;

/// <summary>
/// 牌谱匿名玩家开关：
/// 挂在任意 TMP_Text（或 Button）上，点击时切换 RecordSetting.IsAnonymousPlayers。
/// 会自动写回颜色（开启 = 橙色 / 关闭 = 白色），与现有 RecordSetting 面板一致。
///
/// 使用方式：
/// 1. 在场景里复制/创建一个 TMP_Text（例如 "匿名玩家"）；
/// 2. AddComponent → AnonymousPlayersToggle，把 TMP_Text 拖到 targetText；
/// 3. 可选：调 initialStateOn、trueColor、falseColor。
///
/// 也可挂在任意 GameObject 上（带 Button / Image / TMP_Text 任一即可点击）。
/// </summary>
[DisallowMultipleComponent]
public class AnonymousPlayersToggle : MonoBehaviour {
    [Header("目标元素")]
    [Tooltip("被点击的 TMP_Text（也用作颜色刷新目标）。为空则自动取本物体上的 TMP_Text")]
    [SerializeField] private TMP_Text targetText;

    [Tooltip("可选：独立的可点击 Graphic（不填则取 targetText 或本物体的 Graphic）")]
    [SerializeField] private Graphic clickTarget;

    [Header("颜色配置")]
    [SerializeField] private Color trueColor = new Color(1f, 0.5f, 0f, 1f);
    [SerializeField] private Color falseColor = Color.white;

    [Header("初始状态")]
    [Tooltip("勾选则启动时默认为开启（用于面板默认展示）")]
    [SerializeField] private bool initialStateOn = false;

    [Header("额外回调（可选）")]
    [Tooltip("切换时除了 RecordSetting 逻辑外，还要做的事。例如：自己改图标、播放音效")]
    public UnityEvent<bool> onStateChanged;

    private Button button;

    private void Awake() {
        if (targetText == null) targetText = GetComponent<TMP_Text>();
        if (clickTarget == null) {
            if (targetText != null) clickTarget = targetText;
            else clickTarget = GetComponent<Graphic>();
        }
    }

    private void Start() {
        // 启动时若已有 RecordSetting，则不强行覆盖它的状态；否则按 initialStateOn 写入
        var rs = RecordSetting.Instance;
        bool currentOn = rs != null ? rs.IsAnonymousPlayers : initialStateOn;
        ApplyColor(currentOn);
    }

    private void OnEnable() {
        if (clickTarget == null) return;
        button = clickTarget.GetComponent<Button>();
        if (button == null) {
            button = clickTarget.gameObject.AddComponent<Button>();
        }
        button.onClick.RemoveListener(Toggle);
        button.onClick.AddListener(Toggle);
    }

    private void OnDisable() {
        if (button != null) {
            button.onClick.RemoveListener(Toggle);
        }
    }

    public void Toggle() {
        var rs = RecordSetting.Instance;
        if (rs == null) {
            Debug.LogWarning("[AnonymousPlayersToggle] RecordSetting.Instance is null; toggle ignored.");
            return;
        }
        bool nowOn = !rs.IsAnonymousPlayers;
        rs.SetAnonymousPlayers(nowOn);
        ApplyColor(nowOn);
        onStateChanged?.Invoke(nowOn);
    }

    private void ApplyColor(bool on) {
        if (targetText != null) targetText.color = on ? trueColor : falseColor;
    }
}