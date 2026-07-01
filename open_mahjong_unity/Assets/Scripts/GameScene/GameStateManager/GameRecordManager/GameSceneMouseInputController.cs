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

    [Header("附加排除根（跨 Canvas，如 OverlayCanvas）")]
    [Tooltip("额外的排除根 RectTransform（一般是另一个根 Canvas 的 RectTransform）。命中其子树时同样拦截快捷键。与 excludeRect 共享同一次 RaycastAll，不会增加射线开销。")]
    [SerializeField] private RectTransform[] additionalExcludeRects;

    private string actionInputPhase = InputPhaseNone;

    private float _lastLeftClickTime = -1f;
    private const float DoubleClickThreshold = 0.3f;

    private float _lastRightClickTime = -1f;
    private const float RightClickDebounceInterval = 0.05f;

    // 右键按下瞬间的快照。手牌 OnPointerClick 在抬起时触发，须对照按下时而非抬起时的 phase/权限。
    private string _rightPressSnapshotPhase = InputPhaseNone;
    private bool _rightPressMoqieEligible;
    private bool _rightPressPassEligible;

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
        string prev = actionInputPhase;
        actionInputPhase = phase;
        ClearStaleHandInput($"phase {prev} -> {phase}");
    }

    /// <summary>
    /// 回合切换时清理手牌输入残留（指针按下缓存、右键阶段快照等）。
    /// phase 未变时 SetActionInputPhase 不会调用，须由 doAction/ClearAction 等显式触发。
    /// </summary>
    public void ClearStaleHandInput(string reason) {
        _lastLeftClickTime = -1f;
        _lastRightClickTime = -1f;
        _rightPressSnapshotPhase = InputPhaseNone;
        _rightPressMoqieEligible = false;
        _rightPressPassEligible = false;
        // 注意：这里不再 DisarmAll。两次点击模式下立起手牌是自由的，别人回合（每次轮转都会走 ClearAction）
        // 乃至轮到自己时都不强制收回；立起态只在立起其它牌 / 确认出牌 / 该牌被销毁时才落下。
        TileCard.ClearPendingPointerState();
        // 同步中止尚未松手的拖拽/按压会话，避免左右键同时点击、回合切换时某张牌被永久绑定为 dragCard 无法出牌。
        HandCardDragController.Instance?.AbortActivePress($"ClearStaleHandInput:{reason}");
        Debug.Log($"[HandInput] 清理输入缓存 | 原因={reason} | phase={actionInputPhase}");
    }

    private void Update() {
        // 对局/牌谱挂后台回到主菜单等非游戏窗口时，禁止任何快捷键，避免右键摸切/双击/滚轮误触。
        if (!IsGameSceneForeground()) return;

        // 右键快照须在 excludeRect 判断之前记录：手牌 UI 常在排除区内，否则抬起路径拿不到 pressPhase。
        if (state == StateGame && Input.GetMouseButtonDown(1)) {
            RecordRightPressSnapshot("Update按下");
        }

        if (IsPointerOverExcludeRect()) return;

        if (state == StateRecord) {
            if (GameRecordManager.Instance != null && GameRecordManager.Instance.BlocksRecordNavigation) {
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
            TryDisarmHandSelectionOnClickOutside();
            HandleGameStateMouseShortcutsFromInput();
        }
    }

    /// <summary>
    /// 两次点击确认模式下，点击到非手牌区域（空白处/其它UI）时落下已立起的牌，
    /// 顺带取消其固定提示——给玩家一个"取消选择"的途径。点到手牌本身则交由手牌自身处理。
    /// </summary>
    private void TryDisarmHandSelectionOnClickOutside() {
        if (!Input.GetMouseButtonDown(0)) return;
        if (ConfigManager.Instance == null || !ConfigManager.Instance.IsHandCutConfirmEnabled) return;
        HandCardSelectionController selection = HandCardSelectionController.Instance;
        if (selection == null) return;
        if (IsPointerOverSelfHandCard()) return;
        selection.DisarmAll();
    }

    private void LateUpdate() {
        if (state == StateGame && !Input.GetMouseButton(1)) {
            _rightPressSnapshotPhase = InputPhaseNone;
            _rightPressMoqieEligible = false;
            _rightPressPassEligible = false;
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
        if (EventSystem.current == null) return false;
        bool hasMain = excludeRect != null;
        bool hasExtra = additionalExcludeRects != null && additionalExcludeRects.Length > 0;
        if (!hasMain && !hasExtra) return false;

        if (_pointerEventData == null) _pointerEventData = new PointerEventData(EventSystem.current);
        _pointerEventData.Reset();
        _pointerEventData.position = Input.mousePosition;

        _uiRaycastResults.Clear();
        EventSystem.current.RaycastAll(_pointerEventData, _uiRaycastResults);

        for (int i = 0; i < _uiRaycastResults.Count; i++) {
            GameObject hit = _uiRaycastResults[i].gameObject;
            if (hit == null) continue;
            if (BelongsToAnyExcludeRoot(hit.transform)) {
                return true;
            }
        }
        return false;
    }

    private bool BelongsToAnyExcludeRoot(Transform t) {
        if (excludeRect != null && (t == excludeRect.transform || t.IsChildOf(excludeRect.transform))) {
            return true;
        }
        if (additionalExcludeRects != null) {
            for (int i = 0; i < additionalExcludeRects.Length; i++) {
                RectTransform r = additionalExcludeRects[i];
                if (r != null && (t == r.transform || t.IsChildOf(r.transform))) {
                    return true;
                }
            }
        }
        return false;
    }

    /// <summary>
    /// 当前是否处于游戏/牌谱前台窗口。挂后台回到主菜单等场景时返回 false，用于屏蔽快捷键。
    /// WindowsManager 不可用时回退为 true，保留既有行为。
    /// </summary>
    private bool IsGameSceneForeground() {
        WindowsManager wm = WindowsManager.Instance;
        if (wm == null) return true;
        string w = wm.GetCurrentWindow();
        return w == "game" || w == "recordscene";
    }

    /// <summary>
    /// 手牌 UI 右键按下时补录快照（EventSystem 的 PointerDown 早于 PointerClick，且可能先于本脚本 Update）。
    /// 右键仅走快捷键，不会触发出牌。
    /// </summary>
    public void NotifyHandCardRightPointerDown() {
        if (state != StateGame) {
            return;
        }
        RecordRightPressSnapshot("手牌PointerDown");
    }

    private void RecordRightPressSnapshot(string source) {
        _rightPressSnapshotPhase = actionInputPhase;
        _rightPressMoqieEligible = actionInputPhase == InputPhaseAskHand && HasCutPermission();
        _rightPressPassEligible = actionInputPhase == InputPhaseAskOther && HasPassPermission();
        Debug.Log($"[HandInput] 右键按下 | 来源={source} | pressPhase={_rightPressSnapshotPhase} | moqieEligible={_rightPressMoqieEligible} | passEligible={_rightPressPassEligible}");
    }

    /// <summary>
    /// 允许世界空间面板（如 ControPanel）转发点击，避免被顶层射线截断。与 Update 中的 Input 处理互斥（同一次点击只处理一次）。
    /// 对局态下摸切/鸣牌取消与 Update 使用相同子状态与配置判断。
    /// </summary>
    public void HandleExternalPointerClick(PointerEventData eventData) {
        // 主菜单等非游戏前台窗口不接收转发点击，避免挂后台时 ControPanel/手牌仍触发快捷操作。
        if (!IsGameSceneForeground()) return;

        if (state == StateGame) {
            NormalGameStateManager gsm = NormalGameStateManager.Instance;
            ConfigManager cfg = ConfigManager.Instance;
            if (eventData.button == PointerEventData.InputButton.Right) {
                string pressPhase = _rightPressSnapshotPhase;
                string releasePhase = actionInputPhase;
                // 手牌抬起时若未拿到按下快照（excludeRect / 脚本顺序），用抬起瞬间的 phase 兜底摸切/过牌资格。
                bool moqieEligible = _rightPressMoqieEligible
                    || (pressPhase == InputPhaseNone && releasePhase == InputPhaseAskHand && HasCutPermission());
                bool passEligible = _rightPressPassEligible
                    || (pressPhase == InputPhaseNone && releasePhase == InputPhaseAskOther && HasPassPermission());
                Debug.Log($"[HandInput] 手牌右键抬起 | pressPhase={pressPhase} | releasePhase={releasePhase} | moqieEligible={moqieEligible} | passEligible={passEligible}");
                if (moqieEligible) {
                    if (cfg.MoqieShortcutMode == 1 && TryConsumeRightClickShortcut()) {
                        Debug.Log("[HandInput] 触发摸切(手牌抬起路径)");
                        TryAutoMoqieFromSelfHand();
                    }
                } else if (passEligible) {
                    if (cfg.AskOtherPassShortcutMode == 0 && TryConsumeRightClickShortcut()) {
                        Debug.Log("[HandInput] 触发过牌(手牌抬起路径)");
                        GameCanvas.Instance.TrySendPassFromShortcut();
                    }
                } else if (pressPhase != InputPhaseNone && pressPhase != releasePhase) {
                    Debug.LogWarning($"[HandInput] 拦截跨阶段右键抬起 | pressPhase={pressPhase} | releasePhase={releasePhase}");
                }
                _rightPressSnapshotPhase = InputPhaseNone;
                _rightPressMoqieEligible = false;
                _rightPressPassEligible = false;
                return;
            }
            if (actionInputPhase == InputPhaseAskHand && gsm.allowActionList.Contains("cut")) {
                if (cfg.MoqieShortcutMode == 0) {
                    if (eventData.button == PointerEventData.InputButton.Left && eventData.clickCount >= 2) {
                        TryAutoMoqieFromSelfHand();
                    }
                }
            } else if (actionInputPhase == InputPhaseAskOther && gsm.allowActionList.Contains("pass")) {
                if (cfg.AskOtherPassShortcutMode == 1) {
                    if (eventData.button == PointerEventData.InputButton.Left && eventData.clickCount >= 2) {
                        GameCanvas.Instance.TrySendPassFromShortcut();
                    }
                }
            }
            return;
        }

        if (state == StateRecord) {
            if (GameRecordManager.Instance != null && GameRecordManager.Instance.BlocksRecordNavigation) {
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
        Debug.Log("[HandInput] 触发摸切(Update按下路径)");
        if (GameCanvas.Instance.TriggerMoqieHandCardClick()) return;
        Debug.LogWarning("[HandInput] 摸切失败：手牌容器中没有可出的牌");
    }

    private bool HasCutPermission() {
        NormalGameStateManager gsm = NormalGameStateManager.Instance;
        return gsm != null && gsm.allowActionList.Contains("cut");
    }

    private bool HasPassPermission() {
        NormalGameStateManager gsm = NormalGameStateManager.Instance;
        return gsm != null && gsm.allowActionList.Contains("pass");
    }

    private bool IsPointerOverSelfHandCard() {
        return GameCanvas.Instance != null
            && GameCanvas.Instance.IsPointerOverSelfHandCard(Input.mousePosition);
    }
}
