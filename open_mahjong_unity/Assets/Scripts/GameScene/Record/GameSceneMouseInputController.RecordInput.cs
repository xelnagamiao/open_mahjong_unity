using UnityEngine;

public partial class GameSceneMouseInputController {
    private const float RecordHoldDelaySeconds = 0.4f;
    private const float RecordRepeatIntervalSeconds = 0.12f;
    private bool recordScreenHolding;
    private float recordNextRepeatTime;
    private int recordExpectedRound;
    private int recordExpectedNode;
    private int recordExpectedNavigationVersion;
    private int recordTouchFingerId = -1;
    private bool recordTouchWasPresent;

    private void UpdateRecordScreenInput() {
        // 触摸直接读取自己的坐标和 fingerId；不再同时处理 Unity 模拟出来的鼠标输入。
        if (Input.touchCount > 0) {
            Touch touch = Input.GetTouch(0);
            HandleRecordScreenTouch(Input.touchCount, touch.fingerId, touch.phase, touch.position);
            return;
        }
        if (recordTouchWasPresent) {
            recordTouchWasPresent = false;
            recordTouchFingerId = -1;
            CancelRecordScreenHold();
            return;
        }

        Vector2 position = Input.mousePosition;
        var manager = GameRecordManager.Instance;
        if (Input.GetMouseButton(1)) {
            CancelRecordScreenHold();
            if (Input.GetMouseButtonDown(1) && manager != null && !IsRecordScreenPointerBlocked(position)) {
                manager.StepRecordFromInput(false);
            }
            return;
        }

        float scroll = Input.mouseScrollDelta.y;
        if (Mathf.Abs(scroll) > 0.01f) {
            CancelRecordScreenHold();
            if (manager == null || manager.BlocksRecordNavigation || IsRecordScreenPointerBlocked(position)) return;
            bool shift = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
            if (shift) {
                if (scroll < 0f) manager.StepToNextRoundFromInput();
                else manager.GotoSelectRound(manager.currentRoundIndex - 1);
            } else {
                if (scroll < 0f) manager.NextXunmu();
                else manager.BackXunmu();
            }
            return;
        }
        HandleRecordScreenPointer(position, Input.GetMouseButtonDown(0), Input.GetMouseButton(0));
    }

    private void HandleRecordScreenTouch(int touchCount, int fingerId, TouchPhase phase, Vector2 position) {
        recordTouchWasPresent = true;
        // 多指操作取消快进；剩下的手指必须重新按下，不能在别的手指松开后自动续播。
        if (touchCount != 1) {
            recordTouchFingerId = -1;
            CancelRecordScreenHold();
            return;
        }
        bool began = phase == TouchPhase.Began;
        if (began) recordTouchFingerId = fingerId;
        if (fingerId != recordTouchFingerId) {
            CancelRecordScreenHold();
            return;
        }
        bool held = phase != TouchPhase.Ended && phase != TouchPhase.Canceled;
        HandleRecordScreenPointer(position, began, held);
        if (!held) recordTouchFingerId = -1;
    }

    private bool IsRecordScreenPointerBlocked(Vector2 position) {
        return position.x < 0f || position.y < 0f || position.x >= Screen.width || position.y >= Screen.height
            || IsPointerOverExcludeRect(position);
    }

    private void HandleRecordScreenPointer(Vector2 position, bool pressed, bool held) {
        var manager = GameRecordManager.Instance;
        if (state != StateRecord || !IsGameSceneForeground() || !held || manager == null
            || !manager.CanUseRecordStepInput(true) || IsRecordScreenPointerBlocked(position)) {
            CancelRecordScreenHold();
            return;
        }

        if (pressed) {
            recordScreenHolding = true;
            AdvanceRecordScreenStep(manager);
            recordNextRepeatTime = Time.unscaledTime + RecordHoldDelaySeconds;
            return;
        }
        if (!recordScreenHolding) return;
        if (manager.IsRecordAutoPlaying || (manager.IsSpectatorSession && manager.IsLiveSpectatorMode)
            || manager.currentRoundIndex != recordExpectedRound || manager.currentNode != recordExpectedNode
            || manager.RecordNavigationVersion != recordExpectedNavigationVersion) {
            CancelRecordScreenHold();
            return;
        }
        if (Time.unscaledTime < recordNextRepeatTime) return;
        AdvanceRecordScreenStep(manager);
        // 低帧率或同家动画未完成时不补发积压输入，松手只让正在执行的动画收尾。
        recordNextRepeatTime = Time.unscaledTime + RecordRepeatIntervalSeconds;
    }

    private void AdvanceRecordScreenStep(GameRecordManager manager) {
        manager.StepRecordFromInput(true);
        recordExpectedRound = manager.currentRoundIndex;
        recordExpectedNode = manager.currentNode;
        recordExpectedNavigationVersion = manager.RecordNavigationVersion;
    }

    private void CancelRecordScreenHold() => recordScreenHolding = false;

    private void OnDisable() => CancelRecordScreenHold();
    private void OnApplicationFocus(bool focused) { if (!focused) CancelRecordScreenHold(); }
    private void OnApplicationPause(bool paused) { if (paused) CancelRecordScreenHold(); }
}
