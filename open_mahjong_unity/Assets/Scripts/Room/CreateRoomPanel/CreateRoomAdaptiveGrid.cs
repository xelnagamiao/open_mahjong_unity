using UnityEngine;

public sealed class CreateRoomAdaptiveGrid : UnityEngine.UI.GridLayoutGroup {
    [Min(0)] public float minimumCellWidth;

    public override void CalculateLayoutInputHorizontal() {
        UpdateColumns();
        base.CalculateLayoutInputHorizontal();
        SetLayoutInputForAxis(0, 0, 1, 0);
    }
    public override void SetLayoutHorizontal() {
        UpdateColumns();
        base.SetLayoutHorizontal();
    }
    private void UpdateColumns() {
        float minimum = minimumCellWidth > 0 ? minimumCellWidth : 240;
        int columns = Mathf.Clamp(Mathf.FloorToInt((rectTransform.rect.width - padding.horizontal + spacing.x) / (minimum + spacing.x)), 1, 4);
        constraint = Constraint.FixedColumnCount; constraintCount = columns;
        cellSize = new Vector2(Mathf.Max(1, (rectTransform.rect.width - padding.horizontal - spacing.x * (columns - 1)) / columns), cellSize.y);
    }
}
