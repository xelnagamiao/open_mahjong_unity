using UnityEngine;

/// <summary>Native, maskable uGUI shape for the card layout controls.</summary>
[RequireComponent(typeof(CanvasRenderer))]
public sealed class CardLayoutRoundedGraphic : UnityEngine.UI.MaskableGraphic
{
    [SerializeField] float radius = 12;
    [SerializeField] float borderWidth = 1.5f;
    [SerializeField] Color borderColor = new Color(.58f, .67f, .75f, .65f);

    public void SetAppearance(Color fill, Color border, float round, float width)
    {
        color = fill; borderColor = border; radius = round; borderWidth = width;
        SetVerticesDirty();
    }

    protected override void OnPopulateMesh(UnityEngine.UI.VertexHelper vh)
    {
        vh.Clear();
        var rect = GetPixelAdjustedRect();
        if (rect.width <= 0 || rect.height <= 0) return;
        const int segments = 8;
        const int count = (segments + 1) * 4;
        float round = Mathf.Clamp(radius, 0, Mathf.Min(rect.width, rect.height) * .5f);
        float inset = Mathf.Clamp(borderWidth, 0, Mathf.Min(rect.width, rect.height) * .5f);
        var inner = new Rect(rect.x + inset, rect.y + inset, rect.width - 2 * inset, rect.height - 2 * inset);
        vh.AddVert(rect.center, color, Vector2.zero);
        for (int i = 0; i < count; i++) vh.AddVert(Point(inner, Mathf.Max(0, round - inset), i, segments), color, Vector2.zero);
        for (int i = 0; i < count; i++) vh.AddTriangle(0, i + 1, (i + 1) % count + 1);
        if (inset <= 0) return;
        int start = vh.currentVertCount;
        for (int i = 0; i < count; i++) {
            vh.AddVert(Point(rect, round, i, segments), borderColor, Vector2.zero);
            vh.AddVert(Point(inner, Mathf.Max(0, round - inset), i, segments), borderColor, Vector2.zero);
        }
        for (int i = 0; i < count; i++) {
            int next = (i + 1) % count;
            vh.AddTriangle(start + i * 2, start + next * 2, start + i * 2 + 1);
            vh.AddTriangle(start + next * 2, start + next * 2 + 1, start + i * 2 + 1);
        }
    }

    static Vector2 Point(Rect rect, float round, int index, int segments)
    {
        int corner = index / (segments + 1);
        float angle = (corner * 90f + index % (segments + 1) * 90f / segments) * Mathf.Deg2Rad;
        var center = new Vector2(corner == 0 || corner == 3 ? rect.xMax - round : rect.xMin + round,
            corner < 2 ? rect.yMax - round : rect.yMin + round);
        return center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * round;
    }
}
