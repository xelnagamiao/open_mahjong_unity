using System;
using UnityEngine;
using UnityEngine.UI;

[RequireComponent(typeof(CanvasRenderer))]
public sealed class PlayerInfoTrendChart : MaskableGraphic {
    [SerializeField] private Color lineColor = new Color(1, .4200189f, 0);
    [SerializeField] private Color pointColor = new Color(1, .84f, .16f);
    [SerializeField, Min(1)] private float pointRadius = 5.5f;
    [SerializeField] private Color gridColor = new Color(1, 1, 1, .2f);
    [SerializeField] private int[] placements = Array.Empty<int>();
    public int PointCount => placements.Length;
    private const int Capacity = 10;

    /// <param name="values">从左到右按时间顺序排列，1 位在上，4 位在下。</param>
    public void SetPlacements(int[] values) {
        int count = Math.Min(values?.Length ?? 0, Capacity);
        placements = new int[count];
        if (count > 0) Array.Copy(values, values.Length - count, placements, 0, count);
        SetVerticesDirty();
    }

    private Vector2 Point(int index) {
        var rect = rectTransform.rect;
        // 固定十场横坐标；不足十场从最左侧开始，右侧保留空白。
        float x = index / (float)(Capacity - 1);
        float y = (4 - Mathf.Clamp(placements[index], 1, 4)) / 3f;
        return new Vector2(rect.xMin + x * rect.width, rect.yMin + y * rect.height);
    }

    protected override void OnPopulateMesh(VertexHelper vh) {
        vh.Clear();
        var rect = rectTransform.rect;
        for (int i = 0; i < 4; i++) {
            float y = rect.yMin + rect.height * i / 3;
            Line(vh, new Vector2(rect.xMin, y), new Vector2(rect.xMax, y), 1, gridColor);
        }
        for (int i = 1; i < placements.Length; i++) Line(vh, Point(i - 1), Point(i), 2, lineColor);
        for (int i = 0; i < placements.Length; i++) Dot(vh, Point(i), pointRadius, pointColor);
    }

    private static void Line(VertexHelper vh, Vector2 a, Vector2 b, float width, Color color) {
        Vector2 offset = new Vector2(-(b - a).y, (b - a).x).normalized * width / 2;
        int start = vh.currentVertCount;
        vh.AddVert(a - offset, color, Vector2.zero);
        vh.AddVert(a + offset, color, Vector2.zero);
        vh.AddVert(b + offset, color, Vector2.zero);
        vh.AddVert(b - offset, color, Vector2.zero);
        vh.AddTriangle(start, start + 1, start + 2);
        vh.AddTriangle(start, start + 2, start + 3);
    }

    private static void Dot(VertexHelper vh, Vector2 point, float radius, Color color) {
        int start = vh.currentVertCount;
        vh.AddVert(point, color, Vector2.zero);
        for (int i = 0; i <= 16; i++) {
            float angle = i * Mathf.PI / 8;
            vh.AddVert(point + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius, color, Vector2.zero);
        }
        for (int i = 1; i <= 16; i++) vh.AddTriangle(start, start + i, start + i + 1);
    }
}
