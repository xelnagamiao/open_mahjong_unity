using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

[RequireComponent(typeof(CanvasRenderer)), DefaultExecutionOrder(400)]
public sealed class PlayerInfoTrendChart : MaskableGraphic {
    [SerializeField] private Color lineColor = new Color(1, .4200189f, 0);
    [SerializeField] private Color pointColor = new Color(1, .84f, .16f);
    [SerializeField, Min(1)] private float pointRadius = 5.5f;
    [SerializeField] private Color gridColor = new Color(1, 1, 1, .2f);
    [SerializeField] private int[] placements = Array.Empty<int>();
    [SerializeField] private TMP_Text[] rankLabels = Array.Empty<TMP_Text>();
    private int playerCount = 4;
    private float previousPixelScale;
    private Vector2 previousPixelOrigin, previousSize;
    public int PointCount => placements.Length;
    private const int Capacity = 10;

    public void SetPalette(Color line, Color point, Color grid) {
        lineColor = line;
        pointColor = point;
        gridColor = grid;
        SetVerticesDirty();
    }

    /// <param name="values">从左到右按时间顺序排列，1 位在上，末位在下。</param>
    public void SetPlacements(int[] values, int participants = 4) {
        playerCount = participants == 3 ? 3 : 4;
        UpdateRankLabels();
        int count = Math.Min(values?.Length ?? 0, Capacity);
        placements = new int[count];
        if (count > 0) Array.Copy(values, values.Length - count, placements, 0, count);
        SetVerticesDirty();
    }

    private float PixelScale {
        get {
            float scale = CanvasReferenceLayout.PixelsPerUnit(rectTransform);
            return scale > 0 && !float.IsInfinity(scale) ? scale : 1;
        }
    }

    private float GridWidth => 1 / PixelScale;

    private Vector2 PixelOrigin {
        get {
            if (!canvas) return Vector2.zero;
            var root = canvas.rootCanvas;
            var camera = root.renderMode == RenderMode.ScreenSpaceOverlay ? null : root.worldCamera;
            return RectTransformUtility.WorldToScreenPoint(camera, rectTransform.TransformPoint(Vector3.zero));
        }
    }

    private Rect PlotRect() {
        var rect = rectTransform.rect;
        float scale = PixelScale, origin = PixelOrigin.y;
        // Keep both boundary lines on pixel centers and wholly inside the plot.
        rect.yMin = (Mathf.Ceil(origin + rect.yMin * scale) + .5f - origin) / scale;
        rect.yMax = Mathf.Max(rect.yMin, (Mathf.Floor(origin + rect.yMax * scale) - .5f - origin) / scale);
        return rect;
    }

    private float RankY(int rank, Rect rect) {
        float y = rect.yMax - (rank - 1) * rect.height / (playerCount - 1);
        float scale = PixelScale, origin = PixelOrigin.y;
        return Mathf.Clamp((Mathf.Floor(origin + y * scale) + .5f - origin) / scale, rect.yMin, rect.yMax);
    }

    private void LateUpdate() {
        float scale = PixelScale;
        var origin = PixelOrigin;
        var size = rectTransform.rect.size;
        if (Mathf.Approximately(previousPixelScale, scale) && previousPixelOrigin == origin && previousSize == size) return;
        previousPixelScale = scale;
        previousPixelOrigin = origin; previousSize = size;
        UpdateRankLabels();
        SetVerticesDirty();
    }

    private void UpdateRankLabels() {
        var rect = PlotRect();
        for (int i = 0; i < rankLabels.Length; i++) {
            if (rankLabels[i] == null) continue;
            var label = rankLabels[i].rectTransform;
            label.gameObject.SetActive(i < playerCount);
            if (i < playerCount) {
                var position = label.anchoredPosition;
                position.y = rectTransform.anchoredPosition.y + RankY(i + 1, rect) - label.rect.center.y;
                label.anchoredPosition = position;
            }
        }
    }

    private Vector2 Point(int index, Rect rect) {
        // 固定十场横坐标；不足十场从最左侧开始，右侧保留空白。
        float x = index / (float)(Capacity - 1);
        return new Vector2(rect.xMin + x * rect.width, RankY(Mathf.Clamp(placements[index], 1, playerCount), rect));
    }

    protected override void OnPopulateMesh(VertexHelper vh) {
        vh.Clear();
        // One physical pixel per gridline, including during popup scaling and viewport changes.
        float width = GridWidth;
        var rect = PlotRect();
        for (int i = 0; i < playerCount; i++) {
            float y = RankY(playerCount - i, rect);
            Line(vh, new Vector2(rect.xMin, y), new Vector2(rect.xMax, y), width, gridColor);
        }
        for (int i = 1; i < placements.Length; i++) Line(vh, Point(i - 1, rect), Point(i, rect), 2, lineColor);
        for (int i = 0; i < placements.Length; i++) Dot(vh, Point(i, rect), pointRadius, pointColor);
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
