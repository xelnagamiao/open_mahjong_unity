using System.Collections;
using UnityEngine;

public partial class Game3DManager : MonoBehaviour {
    /// <summary>
    /// 河/补花区单张牌沿 widthdirection 占用的"槽宽"。立直横置牌沿 widthdirection 长度变为 cardHeight。
    /// 与组合牌共享同一套基础宽高常量，只是布局规则不同——河/补花保留 *1.05 间隙，组合牌不需要。
    /// </summary>
    private float DiscardSlotWidth(bool isHorizontal) {
        return isHorizontal ? heightSpacing : widthSpacing;
    }

    /// <summary>
    /// 计算当前位置在指定行（从 SetPosition 的子物体里读取已存在牌的横置标记）的中心偏移。
    /// 同行内若前一张是横置，则整体向后挪 cardHeight*1.05 - cardWidth*1.05 ≈ 一张高度差，
    /// 自然实现"立直牌后续仍续接，但横置牌"的视觉效果。col=0 时 cumOffset 始终为 0（与原版一致）。
    /// </summary>
    private float ComputeRowCenterOffset(Transform SetPosition, int row, int col, int cardsPerRow, bool selfHorizontal) {
        if (col == 0) return 0f;
        float cumOffset = 0f;
        float prevSlotWidth = -1f;
        for (int j = 0; j < col; j++) {
            int childIndex = row * cardsPerRow + j;
            if (childIndex >= SetPosition.childCount) break;
            Tile3D prevTile = SetPosition.GetChild(childIndex).GetComponent<Tile3D>();
            bool prevHorizontal = prevTile != null && prevTile.isRiichiHorizontal;
            float prevWidth = DiscardSlotWidth(prevHorizontal);
            if (prevSlotWidth < 0f) { prevSlotWidth = prevWidth; continue; }
            cumOffset += 0.5f * (prevSlotWidth + prevWidth);
            prevSlotWidth = prevWidth;
        }
        cumOffset += 0.5f * (prevSlotWidth + DiscardSlotWidth(selfHorizontal));
        return cumOffset;
    }

    // 放置3D卡牌 id-位置-类型-玩家位置（协程版本）
    private IEnumerator Set3DTileCoroutine(int tileId, Transform SetPosition, string SetType, string PlayerPosition, bool isMoqie = false, bool isRiichi = false) {
        Debug.Log($"Set3DTileCoroutine:{tileId} {SetType}, {PlayerPosition}, riichi={isRiichi}");

        Vector3 currentPosition = SetPosition.position;
        Quaternion rotation = Quaternion.identity;

        // 获取不同玩家的相对右侧和相对下侧
        Vector3 widthdirection = Vector3.zero;
        Vector3 heightdirection = Vector3.zero;
        if (PlayerPosition == "self") {
            widthdirection = RightDirection;
            heightdirection = BackDirection;
            rotation = SelfTileWorldRotation(SetPosition);
        }
        else if (PlayerPosition == "left") {
            widthdirection = BackDirection;
            heightdirection = LeftDirection;
            rotation = Quaternion.Euler(90, 0, 90);
        }
        else if (PlayerPosition == "top") {
            widthdirection = LeftDirection;
            heightdirection = FrontDirection;
            rotation = Quaternion.Euler(90, 0, 0);
        }
        else if (PlayerPosition == "right") {
            widthdirection = FrontDirection;
            heightdirection = RightDirection;
            rotation = Quaternion.Euler(90, 0, 270);
        }

        bool isRecordSet = SetType == "Record";
        // 立直横置：基础姿势再绕世界 Y 轴顺时针 90°，使长边沿 widthdirection 排布
        if (isRiichi && SetType == "Discard") {
            rotation = Quaternion.Euler(0, 90, 0) * rotation;
        }
        // 添加随机的 z 轴旋转（正负3度），模拟手牌排列的自然效果
        if (!isRecordSet) {
            Vector3 euler = rotation.eulerAngles;
            euler.z += Random.Range(-3f, 3f);
            rotation = Quaternion.Euler(euler);
        }

        int cardsPerRow;
        if (SetType == "Discard") {
            cardsPerRow = 6;
        }
        else if (SetType == "Buhua") {
            cardsPerRow = 4;
        }
        else {
            Debug.LogError($"SetType {SetType} 错误");
            cardsPerRow = 10;
        }

        int index = SetPosition.childCount;
        int row = index / cardsPerRow;
        int col = index % cardsPerRow;

        bool useHorizontalLayout = isRiichi && SetType == "Discard";
        float colOffset = ComputeRowCenterOffset(SetPosition, row, col, cardsPerRow, useHorizontalLayout);
        currentPosition += widthdirection.normalized * colOffset;
        currentPosition += heightdirection.normalized * heightSpacing * row;

        Debug.Log($"创建卡片 {SetPosition.childCount}, 牌ID: {tileId}");
        GameObject cardObj = MahjongObjectPool.Instance.Spawn(tileId, currentPosition, rotation);
        if (cardObj == null) {
            Debug.LogError($"无法从对象池获取牌: {tileId}");
            yield break;
        }
        if (SetType == "Discard") {
            lastCutJiagang3DObject = cardObj;
        }
        // 立直横置标记写入 Tile3D，归还对象池时会被清掉
        Tile3D tile3D = cardObj.GetComponent<Tile3D>();
        if (tile3D != null) tile3D.isRiichiHorizontal = useHorizontalLayout;
        cardObj.transform.SetParent(SetPosition, worldPositionStays: true);
        cardObj.name = $"Card_{SetPosition.childCount}";

        if (Card3DHoverManager.Instance != null) {
            Card3DHoverManager.Instance.RegisterCard(cardObj, tileId);
        }

        if (isMoqie && Card3DHoverManager.Instance != null) {
            Card3DHoverManager.Instance.SetCardGrayOverlay(cardObj, Card3DHoverManager.Instance.MoqieOverlayColor, Card3DHoverManager.Instance.MoqieOverlayIntensity);
        }

        Vector3 startPosition = GetLastRemovePos(PlayerPosition);
        if (PlayerPosition == "self") {
            startPosition = selfPosPanel.outputPos != null ? selfPosPanel.outputPos.position : selfPosPanel.cardsPosition.position;
        }

        if (SetType == "Discard") {
            // 同一家上一张飞牌若仍未结束则先终止，避免同家并发飞牌引用混乱；按玩家隔离不影响他家
            StopDiscardMoveCoroutine(PlayerPosition);
            Coroutine moveCo = StartCoroutine(MoveCardFromRemovePosition(cardObj, currentPosition, startPosition));
            _discardMoveCoroutinesByPlayer[PlayerPosition] = moveCo;
            yield return moveCo;
            if (_discardMoveCoroutinesByPlayer.TryGetValue(PlayerPosition, out Coroutine cur) && cur == moveCo) {
                _discardMoveCoroutinesByPlayer[PlayerPosition] = null;
            }
        }
        else {
            yield return StartCoroutine(MoveCardFromRemovePosition(cardObj, currentPosition, startPosition));
        }
    }

    // 放置3D卡牌 id-位置-类型-玩家位置
    private void Set3DTile(int tileId, Transform SetPosition, string SetType, string PlayerPosition, bool isMoqie = false, bool isRiichi = false) {
        Debug.Log($"Set3DTile:{tileId} {SetType}, {PlayerPosition}, riichi={isRiichi}");

        Vector3 currentPosition = SetPosition.position;
        Quaternion rotation = Quaternion.identity;
        bool isRecordSet = SetType == "Record";

        Vector3 widthdirection = Vector3.zero;
        Vector3 heightdirection = Vector3.zero;
        if (PlayerPosition == "self") {
            widthdirection = RightDirection;
            heightdirection = BackDirection;
            rotation = isRecordSet ? RecordHandTileRotation(PlayerPosition) : SelfTileWorldRotation(SetPosition);
        }
        else if (PlayerPosition == "left") {
            widthdirection = BackDirection;
            heightdirection = LeftDirection;
            rotation = isRecordSet ? RecordHandTileRotation(PlayerPosition) : Quaternion.Euler(90, 0, 90);
        }
        else if (PlayerPosition == "top") {
            widthdirection = LeftDirection;
            heightdirection = FrontDirection;
            rotation = isRecordSet ? RecordHandTileRotation(PlayerPosition) : Quaternion.Euler(90, 0, 0);
        }
        else if (PlayerPosition == "right") {
            widthdirection = FrontDirection;
            heightdirection = RightDirection;
            rotation = isRecordSet ? RecordHandTileRotation(PlayerPosition) : Quaternion.Euler(90, 0, 270);
        }
        bool isDiscardLike = SetType == "Discard" || SetType == "DiscardWithoutAnimation";
        if (isRiichi && isDiscardLike) {
            rotation = Quaternion.Euler(0, 90, 0) * rotation;
        }
        if (!isRecordSet) {
            Vector3 euler = rotation.eulerAngles;
            euler.z += Random.Range(-2f, 2f);
            rotation = Quaternion.Euler(euler);
        }

        int cardsPerRow;
        if (isDiscardLike) {
            cardsPerRow = 6;
        }
        else if (SetType == "Buhua" || SetType == "BuhuaWithoutAnimation") {
            cardsPerRow = 4;
        }
        else if (SetType == "Record") {
            cardsPerRow = 14;
        }
        else {
            Debug.LogError($"SetType {SetType} 错误");
            cardsPerRow = 10;
        }

        int index = SetPosition.childCount;
        int row = index / cardsPerRow;
        int col = index % cardsPerRow;

        bool useHorizontalLayout = isRiichi && isDiscardLike;
        float colOffset = ComputeRowCenterOffset(SetPosition, row, col, cardsPerRow, useHorizontalLayout);
        currentPosition += widthdirection.normalized * colOffset;
        currentPosition += heightdirection.normalized * heightSpacing * row;

        Debug.Log($"创建卡片 {SetPosition.childCount}, 牌ID: {tileId}");
        GameObject cardObj = MahjongObjectPool.Instance.Spawn(tileId, currentPosition, rotation);
        if (cardObj == null) {
            Debug.LogError($"无法从对象池获取牌: {tileId}");
            return;
        }
        if (SetType == "Discard") {
            lastCutJiagang3DObject = cardObj;
        }
        Tile3D tile3D = cardObj.GetComponent<Tile3D>();
        if (tile3D != null) tile3D.isRiichiHorizontal = useHorizontalLayout;
        cardObj.transform.SetParent(SetPosition, worldPositionStays: true);
        cardObj.name = $"Card_{SetPosition.childCount}";

        if (Card3DHoverManager.Instance != null) {
            Card3DHoverManager.Instance.RegisterCard(cardObj, tileId);
        }

        if (isMoqie && Card3DHoverManager.Instance != null) {
            Card3DHoverManager.Instance.SetCardGrayOverlay(cardObj, Card3DHoverManager.Instance.MoqieOverlayColor, Card3DHoverManager.Instance.MoqieOverlayIntensity);
        }

        if (isRecordSet) {
            cardObj.transform.position = currentPosition;
            return;
        }

        Vector3 startPosition = GetLastRemovePos(PlayerPosition);
        if (PlayerPosition == "self") {
            startPosition = selfPosPanel.outputPos != null ? selfPosPanel.outputPos.position : selfPosPanel.cardsPosition.position;
        }
        if (SetType == "DiscardWithoutAnimation" || SetType == "BuhuaWithoutAnimation") {
            return;
        }
        else {
            StartCoroutine(MoveCardFromRemovePosition(cardObj, currentPosition, startPosition));
        }
    }
}
