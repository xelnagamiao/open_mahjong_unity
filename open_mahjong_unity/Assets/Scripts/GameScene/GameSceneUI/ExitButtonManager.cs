using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 牌谱/观战页面顶部的两个退出按钮统一管理器：
/// - 正常对局：两枚按钮都隐藏
/// - 牌谱回放：仅显示「退出牌谱」
/// - 直播观战 / 观战内牌谱：仅显示「退出观战」
/// 由 GameRecordManager 在切换模式时显式调用，保证在场景层级中无论按钮挂在哪个父节点都能可靠隐藏。
/// </summary>
public class ExitButtonManager : MonoBehaviour {
    public static ExitButtonManager Instance { get; private set; }

    [SerializeField] private Button quitRecordButton;
    [SerializeField] private Button quitSpectatorButton;

    private void Awake() {
        Instance = this;
        HideAll();
    }

    /// <summary>正常对局：隐藏全部退出按钮。</summary>
    public void HideAll() {
        if (quitRecordButton != null) quitRecordButton.gameObject.SetActive(false);
        if (quitSpectatorButton != null) quitSpectatorButton.gameObject.SetActive(false);
    }

    /// <summary>牌谱模式：仅显示退出牌谱按钮。</summary>
    public void ShowForRecord() {
        if (quitRecordButton != null) quitRecordButton.gameObject.SetActive(true);
        if (quitSpectatorButton != null) quitSpectatorButton.gameObject.SetActive(false);
    }

    /// <summary>直播观战 / 观战内牌谱：仅显示退出观战按钮。</summary>
    public void ShowForSpectator() {
        if (quitRecordButton != null) quitRecordButton.gameObject.SetActive(false);
        if (quitSpectatorButton != null) quitSpectatorButton.gameObject.SetActive(true);
    }

    /// <summary>对外暴露按钮引用，供 GameRecordManager 等绑定 onClick。</summary>
    public Button QuitRecordButton => quitRecordButton;
    public Button QuitSpectatorButton => quitSpectatorButton;
}
