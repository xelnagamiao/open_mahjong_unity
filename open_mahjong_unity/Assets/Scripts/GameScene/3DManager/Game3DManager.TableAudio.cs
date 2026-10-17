using UnityEngine;

public partial class Game3DManager {
    /// <summary>与实际牌桌共用接触面和座位尺寸；不使用俯视相机的高度作为听音距离。</summary>
    public bool TryGetTableAudioFrame(out Vector3 center, out Quaternion rotation,
        out float halfWidth, out float halfDepth) {
        center = Vector3.zero;
        rotation = Quaternion.identity;
        halfWidth = halfDepth = 0f;
        if (!isActiveAndEnabled || selfPosPanel == null || leftPosPanel == null
            || topPosPanel == null || rightPosPanel == null
            || selfPosPanel.discardsPosition == null || leftPosPanel.discardsPosition == null
            || topPosPanel.discardsPosition == null || rightPosPanel.discardsPosition == null) return false;

        GetTileSurface(out Vector3 surface, out Vector3 normal);
        normal.Normalize();
        Vector3 forward = Vector3.ProjectOnPlane(Vector3.forward, normal).normalized;
        if (forward.sqrMagnitude < 0.5f) return false;
        rotation = Quaternion.LookRotation(forward, normal);
        Vector3 right = rotation * Vector3.right;
        center = (selfPosPanel.discardsPosition.position + leftPosPanel.discardsPosition.position
            + topPosPanel.discardsPosition.position + rightPosPanel.discardsPosition.position) * 0.25f;
        center += normal * Vector3.Dot(surface - center, normal);

        Transform left = leftPosPanel.cardsPosition != null ? leftPosPanel.cardsPosition : leftPosPanel.discardsPosition;
        Transform rightHand = rightPosPanel.cardsPosition != null ? rightPosPanel.cardsPosition : rightPosPanel.discardsPosition;
        Transform self = selfPosPanel.cardsPosition != null ? selfPosPanel.cardsPosition : selfPosPanel.discardsPosition;
        Transform top = topPosPanel.cardsPosition != null ? topPosPanel.cardsPosition : topPosPanel.discardsPosition;
        halfWidth = Mathf.Max(Mathf.Abs(Vector3.Dot(left.position - center, right)),
            Mathf.Abs(Vector3.Dot(rightHand.position - center, right)));
        halfDepth = Mathf.Max(Mathf.Abs(Vector3.Dot(self.position - center, forward)),
            Mathf.Abs(Vector3.Dot(top.position - center, forward)));
        return halfWidth > 0.01f && halfDepth > 0.01f;
    }

    /// <summary>在原来的出牌音效触发点调用；提前计算落点，不等待飞牌或整理动画。</summary>
    public void PlayDiscardSound(string seat, bool isRiichi = false) {
        if (SoundManager.Instance == null) return;
        PosPanel3D panel = GetPosPanel(seat);
        if (panel == null || panel.discardsPosition == null) {
            SoundManager.Instance.PlayDiscardImpact(seat);
            return;
        }
        Vector3 row, nextRow;
        Quaternion rotation;
        switch (seat) {
            case "left":
                row = BackDirection; nextRow = LeftDirection; rotation = Quaternion.Euler(90, 0, 90); break;
            case "top":
                row = LeftDirection; nextRow = FrontDirection; rotation = Quaternion.Euler(90, 0, 0); break;
            case "right":
                row = FrontDirection; nextRow = RightDirection; rotation = Quaternion.Euler(90, 0, 270); break;
            default:
                row = RightDirection; nextRow = BackDirection; rotation = SelfTileWorldRotation(panel.discardsPosition); break;
        }
        if (isRiichi) rotation = Quaternion.Euler(0, -90, 0) * rotation;
        Vector3 slot = TableTileSlotPosition(panel.discardsPosition, row, nextRow, 6, true, isRiichi);
        // 与牌河共用换行、横牌占宽及桌面高度计算；不抽取随机旋转，以免影响视觉随机序列。
        SoundManager.Instance.PlayDiscardImpact(seat, PlaceTileOnTable(slot, rotation));
    }
}
