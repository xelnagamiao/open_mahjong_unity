using UnityEngine;
using UnityEngine.UI;

/// <summary>计分表的完整边线；正文随 Content 一起滚动，不参与布局或拦截鼠标。</summary>
[RequireComponent(typeof(CanvasRenderer))]
public sealed class ScoreHistoryGridGraphic : MaskableGraphic
{
    public enum Section { Header, Body, Frame }

    [SerializeField] private Section section;
    private const float RowHeight = 40f;
    private const float LineWidth = 1f;
    private static readonly float[] Columns = { 0, 120, 230, 340, 450, 560, 670, 780, 890, 1000, 1120 };

    protected override void OnPopulateMesh(VertexHelper mesh)
    {
        mesh.Clear();
        Rect rect = rectTransform.rect;
        float width = rect.width;
        float height = rect.height;
        if (width <= 0 || height <= 0) return;

        if (section == Section.Frame) {
            Line(mesh, rect, 0, 0, width, LineWidth);
            Line(mesh, rect, 0, height - LineWidth, width, LineWidth);
            Line(mesh, rect, 0, 0, LineWidth, height);
            Line(mesh, rect, width - LineWidth, 0, LineWidth, height);
            return;
        }

        for (int i = 0; i < Columns.Length; i++) {
            bool playerSubcolumn = i == 2 || i == 4 || i == 6 || i == 8;
            float top = section == Section.Header && playerSubcolumn ? RowHeight : 0f;
            Line(mesh, rect, Mathf.Min(Columns[i], width - LineWidth), top, LineWidth, height - top);
        }
        if (section == Section.Header) {
            Line(mesh, rect, 0, 0, width, LineWidth);
            Line(mesh, rect, 120, RowHeight, 880, LineWidth);
            Line(mesh, rect, 0, height - LineWidth, width, LineWidth);
        } else {
            for (float y = 0; y < height; y += RowHeight) {
                Line(mesh, rect, 0, y, width, LineWidth);
            }
            Line(mesh, rect, 0, height - LineWidth, width, LineWidth);
        }
    }

    private void Line(VertexHelper mesh, Rect rect, float x, float y, float width, float height)
    {
        if (width <= 0 || height <= 0) return;
        int start = mesh.currentVertCount;
        float left = rect.xMin + x;
        float top = rect.yMax - y;
        mesh.AddVert(new Vector3(left, top), color, Vector2.zero);
        mesh.AddVert(new Vector3(left + width, top), color, Vector2.zero);
        mesh.AddVert(new Vector3(left + width, top - height), color, Vector2.zero);
        mesh.AddVert(new Vector3(left, top - height), color, Vector2.zero);
        mesh.AddTriangle(start, start + 1, start + 2);
        mesh.AddTriangle(start, start + 2, start + 3);
    }
}
