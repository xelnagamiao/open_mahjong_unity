using UnityEngine;
using UnityEngine.EventSystems;
using System.Collections.Generic;

/// <summary>
/// 牌谱场景鼠标输入控制：
/// - 左键：下一步
/// - 右键：上一步
/// - 滚轮下：下一巡
/// - 滚轮上：上一巡
/// - Shift + 滚轮下：下一局
/// - Shift + 滚轮上：上一局
/// 鼠标悬停在本脚本挂载物体上时输入生效。
/// </summary>
public class GameSceneMouseInputController : MonoBehaviour, IPointerClickHandler, IScrollHandler, IPointerEnterHandler, IPointerExitHandler {
    public static GameSceneMouseInputController Instance { get; private set; }
    [SerializeField] private string state = "UnInit"; // recordstate / gamestate

    private bool isPointerHovering = false;

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

    public void OnPointerClick(PointerEventData eventData) {
        HandlePointerClick(eventData);
    }

    /// <summary>
    /// 允许其他 UI（如世界空间面板）转发点击，避免输入被顶层 Raycast 截断。
    /// </summary>
    public void HandleExternalPointerClick(PointerEventData eventData) {
        HandlePointerClick(eventData);
    }

    private void HandlePointerClick(PointerEventData eventData) {
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

    public void OnPointerEnter(PointerEventData eventData) {
        isPointerHovering = true;
    }

    public void OnPointerExit(PointerEventData eventData) {
        isPointerHovering = false;
    }

    public void OnScroll(PointerEventData eventData) {
        if (state == "gamestate") return;
        if (!isPointerHovering) return;

        bool isShiftPressed = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
        if (isShiftPressed) {
            if (eventData.scrollDelta.y < 0f) {
                GameRecordManager.Instance.GotoSelectRound(GameRecordManager.Instance.currentRoundIndex + 1);
            } else if (eventData.scrollDelta.y > 0f) {
                GameRecordManager.Instance.GotoSelectRound(GameRecordManager.Instance.currentRoundIndex - 1);
            }
            return;
        }

        if (eventData.scrollDelta.y < 0f) {
            GameRecordManager.Instance.NextXunmu();
        } else if (eventData.scrollDelta.y > 0f) {
            GameRecordManager.Instance.BackXunmu();
        }
    }

    private void TryAutoMoqieFromSelfHand() {
        List<int> selfHandTiles = NormalGameStateManager.Instance.selfHandTiles;
        if (selfHandTiles.Count <= 0) return;
        int lastTileId = selfHandTiles[selfHandTiles.Count - 1];
        bool success = GameCanvas.Instance.TriggerTileCardClick(lastTileId);
        if (!success) {
            Debug.LogWarning($"自动出牌失败：无法找到牌ID {lastTileId} 对应的 TileCard");
        }
    }
}
