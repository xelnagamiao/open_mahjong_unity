using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// 牌谱场景鼠标输入控制（不依赖射线检测，使用 Input + 屏幕矩形排除）：
/// - 左键：下一步
/// - 右键：上一步
/// - 滚轮下：下一巡
/// - 滚轮上：上一巡
/// - Shift + 滚轮下：下一局
/// - Shift + 滚轮上：上一局
/// 通过 Input 直接读取鼠标位置与按键，用「排除用 RectTransform」判断是否在面板上，避免射线导致的指针偏移。
/// 无需挂载在带 Graphic 的物体上，也不再依赖 PassThroughToWorldSpaceFilter。
/// ControPanel 点击仍可通过 HandleExternalPointerClick 转发，与本脚本的 Input 处理二选一生效，不会重复触发。
/// 对局态（StateGame）下由子状态 actionInputPhase 区分 askHandAction / askOtherAction，并结合 ConfigManager 的摸切与鸣牌「取消」快捷配置。
/// </summary>
public class GameSceneMouseInputController : MonoBehaviour {
    public static GameSceneMouseInputController Instance { get; private set; }

    public const string StateIdle = "Idle";
    public const string StateRecord = "recordstate";
    public const string StateGame = "gamestate";

    public const string InputPhaseNone = "";
    public const string InputPhaseAskHand = "askHandAction";
    public const string InputPhaseAskOther = "askOtherAction";

    [SerializeField] private string state = StateIdle;

    [Header("排除区域（可选）")]
    [Tooltip("当鼠标射线命中此 Rect（或其子物体）下的 UI 元素时，不处理点击/滚轮。比矩形包含更精确，透明空白处不会误拦截。")]
    [SerializeField] private RectTransform excludeRect;
    [Tooltip("用于将 excludeRect 投影到屏幕的相机，通常为渲染世界空间 Canvas 的 Camera。")]
    [SerializeField] private Camera worldCamera;

    private string actionInputPhase = InputPhaseNone;

    private float _lastLeftClickTime = -1f;
    private const float DoubleClickThreshold = 0.3f;

    private float _lastRightClickTime = -1f;
    private const float RightClickDebounceInterval = 0.05f;

    private readonly List<RaycastResult> _uiRaycastResults = new List<RaycastResult>(16);
    private PointerEventData _pointerEventData;

    public string ActionInputPhase => actionInputPhase;

    private void Awake() {
        if (Instance == null) {
            Instance = this;
        } else {
            Destroy(gameObject);
        }
    }

    public void SetState(string newState) {
        state = newState;
        SetActionInputPhase(InputPhaseNone);
    }

    public void SetActionInputPhase(string phase) {
        if (actionInputPhase == phase) return;
        actionInputPhase = phase;
        _lastLeftClickTime = -1f;
        _lastRightClickTime = -1f;
        HandCardSelectionController.Instance?.DisarmAll();
    }

    private void Update() {
        if (IsPointerOverExcludeRect()) return;

        if (state == StateRecord) {
            if (EndResultPanel.Instance != null && EndResultPanel.Instance.IsAwaitingRecordResultConfirm) {
                return;
            }
            if (Input.GetMouseButtonDown(0)) {
                GameRecordManager.Instance.NextStep();
            } else if (Input.GetMouseButtonDown(1)) {
                GameRecordManager.Instance.BackStep();
            }

            float scroll = Input.mouseScrollDelta.y;
            if (Mathf.Abs(scroll) > 0.01f) {
                bool isShiftPressed = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
                if (isShiftPressed) {
                    if (scroll < 0f) GameRecordManager.Instance.GotoSelectRound(GameRecordManager.Instance.currentRoundIndex + 1);
                    else if (scroll > 0f) GameRecordManager.Instance.GotoSelectRound(GameRecordManager.Instance.currentRoundIndex - 1);
                } else {
                    if (scroll < 0f) GameRecordManager.Instance.NextXunmu();
                    else if (scroll > 0f) GameRecordManager.Instance.BackXunmu();
                }
            }
        } else if (state == StateGame) {
            HandleGameStateMouseShortcutsFromInput();
        }
    }

    private void HandleGameStateMouseShortcutsFromInput() {
        NormalGameStateManager gsm = NormalGameStateManager.Instance;
        ConfigManager cfg = ConfigManager.Instance;

        if (actionInputPhase == InputPhaseAskHand && gsm.allowActionList.Contains("cut")) {
            if (cfg.MoqieShortcutMode == 1) {
                if (Input.GetMouseButtonDown(1) && TryConsumeRightClickShortcut()) {
                    TryAutoMoqieFromSelfHand();
                }
            } else if (cfg.MoqieShortcutMode == 0) {
                if (Input.GetMouseButtonDown(0) && !IsPointerOverSelfHandCard()) {
                    float t = Time.unscaledTime;
                    if (t - _lastLeftClickTime <= DoubleClickThreshold) {
                        TryAutoMoqieFromSelfHand();
                        _lastLeftClickTime = -1f;
                    } else {
                        _lastLeftClickTime = t;
                    }
                }
            }
        } else if (actionInputPhase == InputPhaseAskOther && gsm.allowActionList.Contains("pass")) {
            if (cfg.AskOtherPassShortcutMode == 0) {
                if (Input.GetMouseButtonDown(1) && TryConsumeRightClickShortcut()) {
                    GameCanvas.Instance.TrySendPassFromShortcut();
                }
            } else if (cfg.AskOtherPassShortcutMode == 1) {
                if (Input.GetMouseButtonDown(0)) {
                    float t = Time.unscaledTime;
                    if (t - _lastLeftClickTime <= DoubleClickThreshold) {
                        GameCanvas.Instance.TrySendPassFromShortcut();
                        _lastLeftClickTime = -1f;
                    } else {
                        _lastLeftClickTime = t;
                    }
                }
            }
        }
    }

    public bool IsPointerOverExcludeRect() {
        if (excludeRect == null) return false;
        if (EventSystem.current == null) return false;

        if (_pointerEventData == null) _pointerEventData = new PointerEventData(EventSystem.current);
        _pointerEventData.Reset();
        _pointerEventData.position = Input.mousePosition;

        _uiRaycastResults.Clear();
        EventSystem.current.RaycastAll(_pointerEventData, _uiRaycastResults);

        for (int i = 0; i < _uiRaycastResults.Count; i++) {
            GameObject hit = _uiRaycastResults[i].gameObject;
            if (hit == null) continue;
            if (hit.transform == excludeRect.transform || hit.transform.IsChildOf(excludeRect.transform)) {
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// 允许世界空间面板（如 ControPanel）转发点击，避免被顶层射线截断。与 Update 中的 Input 处理互斥（同一次点击只处理一次）。
    /// 对局态下摸切/鸣牌取消与 Update 使用相同子状态与配置判断。
    /// </summary>
    public void HandleExternalPointerClick(PointerEventData eventData) {
        if (state == StateGame) {
            NormalGameStateManager gsm = NormalGameStateManager.Instance;
            ConfigManager cfg = ConfigManager.Instance;
            if (actionInputPhase == InputPhaseAskHand && gsm.allowActionList.Contains("cut")) {
                if (cfg.MoqieShortcutMode == 1) {
                    if (eventData.button == PointerEventData.InputButton.Right && TryConsumeRightClickShortcut()) {
                        TryAutoMoqieFromSelfHand();
                    }
                } else if (cfg.MoqieShortcutMode == 0) {
                    if (eventData.button == PointerEventData.InputButton.Left && eventData.clickCount >= 2) {
                        TryAutoMoqieFromSelfHand();
                    }
                }
            } else if (actionInputPhase == InputPhaseAskOther && gsm.allowActionList.Contains("pass")) {
                if (cfg.AskOtherPassShortcutMode == 0) {
                    if (eventData.button == PointerEventData.InputButton.Right && TryConsumeRightClickShortcut()) {
                        GameCanvas.Instance.TrySendPassFromShortcut();
                    }
                } else if (cfg.AskOtherPassShortcutMode == 1) {
                    if (eventData.button == PointerEventData.InputButton.Left && eventData.clickCount >= 2) {
                        GameCanvas.Instance.TrySendPassFromShortcut();
                    }
                }
            }
            return;
        }

        if (state == StateRecord) {
            if (EndResultPanel.Instance != null && EndResultPanel.Instance.IsAwaitingRecordResultConfirm) {
                return;
            }
            if (eventData.button == PointerEventData.InputButton.Left) {
                GameRecordManager.Instance.NextStep();
            } else if (eventData.button == PointerEventData.InputButton.Right) {
                GameRecordManager.Instance.BackStep();
            }
        }
    }

    /// <summary>
    /// 设置排除区域，用于与 ControPanel 等世界空间 UI 重叠时排除点击。
    /// </summary>
    public void SetExcludeRect(RectTransform rect, Camera cam = null) {
        excludeRect = rect;
        worldCamera = cam;
    }

    private bool TryConsumeRightClickShortcut() {
        float t = Time.unscaledTime;
        if (t - _lastRightClickTime < RightClickDebounceInterval) {
            return false;
        }
        _lastRightClickTime = t;
        return true;
    }

    private void TryAutoMoqieFromSelfHand() {
        if (GameCanvas.Instance.TriggerMoqieHandCardClick()) return;
        Debug.LogWarning("自动出牌失败：手牌容器中没有可出的牌");
    }

    private bool IsPointerOverSelfHandCard() {
        return GameCanvas.Instance != null
            && GameCanvas.Instance.IsPointerOverSelfHandCard(Input.mousePosition);
    }
}
