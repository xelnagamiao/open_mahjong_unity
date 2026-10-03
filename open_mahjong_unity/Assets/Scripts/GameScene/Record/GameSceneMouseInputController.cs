using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// 牌谱场景鼠标/触屏输入控制（使用 Input + UI 射线排除）：
/// - 空白处左键/触屏：下一步，长按连续前进
/// - 右键：上一步
/// - 滚轮下：下一巡
/// - 滚轮上：上一巡
/// - Shift + 滚轮下：下一局
/// - Shift + 滚轮上：上一局
/// 通过 Input 读取指针位置与按键，命中排除根下的 UI 时不推进。
/// 无需挂载在带 Graphic 的物体上，也不再依赖 PassThroughToWorldSpaceFilter。
/// 牌谱输入统一由 Update 处理；ControPanel 等的抬起转发只用于对局快捷操作，避免牌谱重复步进。
/// 对局态（StateGame）下由子状态 actionInputPhase 区分 askHandAction / askOtherAction，并结合 ConfigManager 的摸切与鸣牌「取消」快捷配置。
/// </summary>
public partial class GameSceneMouseInputController : MonoBehaviour {
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
    /// <summary>本次右键按住期间是否已消费过快捷操作（按下路径 pass/摸切后，抬起路径不再重复触发）。</summary>
    private bool _rightHoldShortcutConsumed;

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
        CancelRecordScreenHold();
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
        HandCardDragController.Instance.AbortActivePress($"ClearStaleHandInput:{reason}");
        Debug.Log($"[HandInput] 清理输入缓存 | 原因={reason} | phase={actionInputPhase}");
    }

    private void Update() {
        // 对局/牌谱挂后台回到主菜单等非游戏窗口时，禁止任何快捷键，避免右键摸切/双击/滚轮误触。
        if (!IsGameSceneForeground()) {
            CancelRecordScreenHold();
            return;
        }

        if (state == StateRecord) {
            UpdateRecordScreenInput();
            return;
        }

        // 右键快照须在 excludeRect 判断之前记录：手牌 UI 常在排除区内，否则抬起路径拿不到 pressPhase。
        if (state == StateGame && Input.GetMouseButtonDown(1)) {
            RecordRightPressSnapshot("Update按下");
        }

        if (IsPointerOverExcludeRect()) return;

        if (state == StateGame) {
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
        if (!GameSettings.Current.IsHandCutConfirmEnabled) return;
        HandCardSelectionController selection = HandCardSelectionController.Instance;
        if (selection == null) return;
        if (IsPointerOverSelfHandCard()) return;
        selection.DisarmAll();
    }

    private void LateUpdate() {
        if (!Input.GetMouseButton(1)) {
            _rightPressSnapshotPhase = InputPhaseNone;
            _rightPressMoqieEligible = false;
            _rightPressPassEligible = false;
            _rightHoldShortcutConsumed = false;
        }
    }

    private void HandleGameStateMouseShortcutsFromInput() {
        NormalGameStateManager gsm = NormalGameStateManager.Instance;
        IGameSettings cfg = GameSettings.Current;

        if (actionInputPhase == InputPhaseAskHand && gsm.allowActionList.Contains("cut")) {
            if (cfg.MoqieShortcutMode == 1) {
                if (Input.GetMouseButtonDown(1) && TryConsumeRightClickShortcut()) {
                    TryAutoMoqieFromSelfHand("Update按下路径", markRightHold: true);
                }
            } else if (cfg.MoqieShortcutMode == 0) {
                if (Input.GetMouseButtonDown(0) && !IsPointerOverSelfHandCard()) {
                    float t = Time.unscaledTime;
                    if (t - _lastLeftClickTime <= DoubleClickThreshold) {
                        TryAutoMoqieFromSelfHand("Update双击路径", markRightHold: false);
                        _lastLeftClickTime = -1f;
                    } else {
                        _lastLeftClickTime = t;
                    }
                }
            }
        } else if (actionInputPhase == InputPhaseAskOther && HasPassPermission()) {
            if (cfg.AskOtherPassShortcutMode == 0) {
                if (Input.GetMouseButtonDown(1) && TryConsumeRightClickShortcut()) {
                    TrySendPassFromShortcut("Update按下路径");
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
        return IsPointerOverExcludeRect(Input.mousePosition);
    }

    private bool IsPointerOverExcludeRect(Vector2 screenPosition) {
        if (EventSystem.current == null) return false;
        FreeGameState freeState = FreeGameState.Active;
        if (freeState != null && freeState.IsActive && freeState.BlocksTableShortcuts) return true;
        bool hasMain = excludeRect != null;
        bool hasExtra = additionalExcludeRects != null && additionalExcludeRects.Length > 0;
        bool hasFreeUi = freeState != null && freeState.IsActive;
        if (!hasMain && !hasExtra && !hasFreeUi) return false;

        if (_pointerEventData == null) _pointerEventData = new PointerEventData(EventSystem.current);
        _pointerEventData.Reset();
        _pointerEventData.position = screenPosition;

        _uiRaycastResults.Clear();
        EventSystem.current.RaycastAll(_pointerEventData, _uiRaycastResults);

        for (int i = 0; i < _uiRaycastResults.Count; i++) {
            GameObject hit = _uiRaycastResults[i].gameObject;
            if (hit == null) continue;
            // 自由模式面板是动态挂到 GameCanvas 的，不在场景预设的 GamePanel 排除树内。
            // 读消息、调分和取转移牌时，不应同时触发右键摸切等牌桌快捷操作。
            if (hasFreeUi && (hit.GetComponentInParent<FreeModeHud>() != null
                || hit.GetComponentInParent<FreeModeActivityPanel>() != null
                || hit.GetComponentInParent<FreeModeTransferTileHitTarget>() != null)) {
                return true;
            }
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
        string w = GameHost.Current.CurrentWindow;
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
    /// 对局允许世界空间面板（如 ControPanel）转发点击；牌谱只处理原始按下/按住输入。
    /// 对局态下摸切/鸣牌取消与 Update 使用相同子状态与配置判断。
    /// </summary>
    public void HandleExternalPointerClick(PointerEventData eventData) {
        // 主菜单等非游戏前台窗口不接收转发点击，避免挂后台时 ControPanel/手牌仍触发快捷操作。
        if (!IsGameSceneForeground()) return;

        if (state == StateGame) {
            NormalGameStateManager gsm = NormalGameStateManager.Instance;
            IGameSettings cfg = GameSettings.Current;
            if (eventData.button == PointerEventData.InputButton.Right) {
                if (_rightHoldShortcutConsumed) {
                    Debug.Log("[HandInput] 右键抬起路径已跳过：本次按住已在按下路径消费快捷操作");
                    _rightPressSnapshotPhase = InputPhaseNone;
                    _rightPressMoqieEligible = false;
                    _rightPressPassEligible = false;
                    return;
                }

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
                        TryAutoMoqieFromSelfHand("手牌抬起路径", markRightHold: true);
                    }
                } else if (passEligible) {
                    if (cfg.AskOtherPassShortcutMode == 0 && TryConsumeRightClickShortcut()) {
                        TrySendPassFromShortcut("手牌抬起路径");
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
                        TryAutoMoqieFromSelfHand("手牌双击路径", markRightHold: false);
                    }
                }
            } else if (actionInputPhase == InputPhaseAskOther && HasPassPermission()) {
                if (cfg.AskOtherPassShortcutMode == 1) {
                    if (eventData.button == PointerEventData.InputButton.Left && eventData.clickCount >= 2) {
                        GameCanvas.Instance.TrySendPassFromShortcut();
                    }
                }
            }
            return;
        }

        // 牌谱由 UpdateRecordScreenInput 唯一处理。抬起时再次转发会多走一步，
        // 也会让从 UI 按下再移到空白处的点击穿透排除区域。
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

    private void MarkRightHoldShortcutConsumed(string source) {
        _rightHoldShortcutConsumed = true;
        Debug.Log($"[HandInput] 右键按住会话已消费 | 来源={source}");
    }

    private void TrySendPassFromShortcut(string source) {
        if (!HasPassPermission()) return;
        GameCanvas.Instance.TrySendPassFromShortcut();
        MarkRightHoldShortcutConsumed(source);
    }

    private void TryAutoMoqieFromSelfHand(string source, bool markRightHold) {
        Debug.Log($"[HandInput] 触发摸切({source})");
        if (GameCanvas.Instance.TriggerMoqieHandCardClick()) {
            if (markRightHold) {
                MarkRightHoldShortcutConsumed(source);
            }
            return;
        }
        Debug.LogWarning("[HandInput] 摸切失败：手牌容器中没有可出的牌");
    }

    private bool HasCutPermission() {
        return NormalGameStateManager.Instance.allowActionList.Contains("cut");
    }

    private bool HasPassPermission() {
        NormalGameStateManager gsm = NormalGameStateManager.Instance;
        if (gsm == null) return false;
        return ActionWords.Any(gsm.allowActionList, ActionWordKind.Pass);
    }

    private bool IsPointerOverSelfHandCard() {
        return GameCanvas.Instance.IsPointerOverSelfHandCard(Input.mousePosition);
    }
}
