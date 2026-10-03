using TMPro;
using UnityEngine;
using UnityEngine.UI;

[AddComponentMenu("Layout/Create Room Details Layout")]
public sealed class CreateRoomDetailsLayout : LayoutGroup {
    [SerializeField, Min(1)] private int columns = 2;
    public int Columns { get => Mathf.Max(1, columns); set => columns = Mathf.Max(1, value); }
    private const float ColumnGap = 24, RowGap = 8, OptionHeight = 104;

    public override void CalculateLayoutInputHorizontal() {
        base.CalculateLayoutInputHorizontal();
        SetLayoutInputForAxis(0, 0, 1, 0);
    }

    public override void CalculateLayoutInputVertical() {
        float height = Arrange(-1);
        SetLayoutInputForAxis(height, height, -1, 1);
    }

    public override void SetLayoutHorizontal() { Arrange(0); }
    public override void SetLayoutVertical() { Arrange(1); }

    private float Arrange(int axis) {
        float width = Mathf.Max(0, rectTransform.rect.width - padding.horizontal);
        float columnWidth = Mathf.Max(0, (width - ColumnGap * (Columns - 1)) / Columns);
        float y = padding.top;
        int column = 0;
        foreach (var child in rectChildren) {
            bool option = child.name.StartsWith("DetailedConfig_") || child.name.StartsWith("FanTai_");
            if (!option && column != 0) { column = 0; y += OptionHeight + RowGap; }
            float height = option ? OptionHeight : 72;
            if (child.name.StartsWith("Section_")) height = 48;
            else if (child.TryGetComponent<TMP_Text>(out var text))
                height = Mathf.Max(40, text.GetPreferredValues(width, 0).y + 8);
            float x = padding.left + (option ? column * (columnWidth + ColumnGap) : 0);
            if (axis == 0) SetChildAlongAxis(child, 0, x, option ? columnWidth : width);
            if (axis == 1) SetChildAlongAxis(child, 1, y, height);
            if (!option || column == Columns - 1) { y += height + RowGap; column = 0; }
            else column++;
        }
        if (column != 0) y += OptionHeight + RowGap;
        return Mathf.Max(0, y - RowGap + padding.bottom);
    }
}
