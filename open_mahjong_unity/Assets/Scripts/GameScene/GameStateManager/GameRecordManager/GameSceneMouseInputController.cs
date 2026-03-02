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
/// </summary>
public class GameSceneMouseInputController : MonoBehaviour {
    public static GameSceneMouseInputController Instance { get; private set; }

    [SerializeField] private string state = "UnInit"; // recordstate / gamestate

    [Header("排除区域（可选）")]
    [Tooltip("当鼠标射线命中此 Rect（或其子物体）下的 UI 元素时，不处理点击/滚轮。比矩形包含更精确，透明空白处不会误拦截。")]
    [SerializeField] private RectTransform excludeRect;
    [Tooltip("用于将 excludeRect 投影到屏幕的相机，通常为渲染世界空间 Canvas 的 Camera。")]
    [SerializeField] private Camera worldCamera;

    private float _lastLeftClickTime = -1f;
    private const float DoubleClickThreshold = 0.3f;

    private readonly List<RaycastResult> _uiRaycastResults = new List<RaycastResult>(16);
    private PointerEventData _pointerEventData;

    private void Awake() {
        if (Instance == null) {
            Instance = this;
        } else {
            Destroy(gameObject);
        }
    }

    public void SetState(string newState) {
        state = newState;
    }

    private void Update() {
        if (IsPointerOverExcludeRect()) return;

        if (state == "recordstate") {
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
        } else if (state == "gamestate") {
            if (Input.GetMouseButtonDown(0)) {
                float t = Time.unscaledTime;
                if (t - _lastLeftClickTime <= DoubleClickThreshold) {
                    TryAutoMoqieFromSelfHand();
                    _lastLeftClickTime = -1f;
                } else {
                    _lastLeftClickTime = t;
                }
            }
        }
    }

    /// <summary>
    /// 鼠标射线是否命中排除 UI（excludeRect 及其子物体）。
    /// 注意：需要目标 UI 上存在可 Raycast 的 Graphic（如 Image/TMP_Text 且 Raycast Target 开启）。
    /// </summary>
    private bool IsPointerOverExcludeRect() {
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
    /// </summary>
    public void HandleExternalPointerClick(PointerEventData eventData) {
        if (state == "gamestate") {
            if (eventData.button == PointerEventData.InputButton.Left && eventData.clickCount >= 2) {
                TryAutoMoqieFromSelfHand();
            }
            return;
        }

        if (eventData.button == PointerEventData.InputButton.Left) {
            GameRecordManager.Instance.NextStep();
        } else if (eventData.button == PointerEventData.InputButton.Right) {
            GameRecordManager.Instance.BackStep();
        }
    }

    /// <summary>
    /// 设置排除区域，用于与 ControPanel 等世界空间 UI 重叠时排除点击。
    /// </summary>
    public void SetExcludeRect(RectTransform rect, Camera cam = null) {
        excludeRect = rect;
        worldCamera = cam;
    }

    private void TryAutoMoqieFromSelfHand() {
        if (NormalGameStateManager.Instance == null) return;
        var selfHandTiles = NormalGameStateManager.Instance.selfHandTiles;
        if (selfHandTiles == null || selfHandTiles.Count <= 0) return;
        int lastTileId = selfHandTiles[selfHandTiles.Count - 1];
        if (GameCanvas.Instance != null && GameCanvas.Instance.TriggerTileCardClick(lastTileId)) return;
        Debug.LogWarning($"自动出牌失败：无法找到牌ID {lastTileId} 对应的 TileCard");
    }
}
