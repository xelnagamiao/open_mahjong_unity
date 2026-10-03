using UnityEngine;
using TMPro;

/// <summary>
/// 匹配已成功（found）后的进入游戏状态面板，与 <see cref="MatchLobbyView"/> 区分。
/// 应挂在 OverlayCanvas 上，不随 <see cref="MatchPanel"/> 窗口切换而隐藏。
/// </summary>
public class MatchFoundedPanel : MonoBehaviour {
    public static MatchFoundedPanel Instance { get; private set; }

    [SerializeField] private CanvasGroupFadeIn fadeIn;
    [SerializeField] private TMP_Text foundedMatchTypeText;
    [SerializeField] private TMP_Text foundedCountdownText;

    private CanvasGroup _canvasGroup;

    private void Awake() {
        if (Instance != null && Instance != this) {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        _canvasGroup = GetComponent<CanvasGroup>();
        gameObject.SetActive(false);
    }

    public void Show(string matchTypeName) {
        bool firstFound = !MatchStateManager.Instance.IsMatchFound;
        MatchStateManager.Instance.MarkMatchFound(matchTypeName);
        if (firstFound) {
            SoundManager.Instance.PlayGameStartSound();
        }
        Present(matchTypeName);
    }

    public void StopCountdownAndHide() {
        ResetCanvasGroupBeforeHide();
        gameObject.SetActive(false);
    }

    private void Present(string matchTypeName) {
        gameObject.SetActive(true);
        transform.SetAsLastSibling();
        EnsureCanvasGroupInteractive();
        if (foundedMatchTypeText != null) {
            foundedMatchTypeText.text = matchTypeName;
        }
        if (fadeIn != null) {
            fadeIn.PlayFadeIn();
        } else {
            EnsureCanvasGroupOpaque();
        }
        if (foundedCountdownText != null) foundedCountdownText.text = "正在进入对局…";
    }

    private CanvasGroup GetCanvasGroup() {
        if (_canvasGroup == null) {
            _canvasGroup = GetComponent<CanvasGroup>();
        }
        return _canvasGroup;
    }

    private void EnsureCanvasGroupInteractive() {
        CanvasGroup cg = GetCanvasGroup();
        if (cg == null) return;
        cg.interactable = true;
        cg.blocksRaycasts = true;
    }

    private void EnsureCanvasGroupOpaque() {
        CanvasGroup cg = GetCanvasGroup();
        if (cg == null) return;
        cg.alpha = 1f;
        cg.interactable = true;
        cg.blocksRaycasts = true;
    }

    private void ResetCanvasGroupBeforeHide() {
        CanvasGroup cg = GetCanvasGroup();
        if (cg == null) return;
        cg.alpha = 1f;
        cg.interactable = true;
        cg.blocksRaycasts = true;
    }

}
