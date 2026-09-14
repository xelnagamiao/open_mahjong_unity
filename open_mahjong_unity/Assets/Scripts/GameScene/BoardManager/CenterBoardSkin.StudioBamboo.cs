using UnityEngine;

public sealed partial class CenterBoardSkin
{
    void PaintStudioBamboo()
    {
        // A dark flat tile with an inset pale ledger. The fine ruled details
        // belong to the readout and stick endpoints, leaving scores unframed.
        Shape(face[0], Vector2.zero, new Vector2(77, 77), 5.5f, "#143638", "#102A2C", .5f);
        Shape(face[1], Vector2.zero, new Vector2(75.2f, 75.2f), 5.0f, "#254C4D", "#638B80", .32f);
        Shape(face[2], Vector2.zero, new Vector2(35, 33.4f), 2.2f, "#D9E0CD", "#B4C1B2", .5f);
        Shape(face[3], new Vector2(0, -.2f), new Vector2(30.6f, .35f), .1f, "#A0AF9C", "#A0AF9C", 0);
        face[4].gameObject.SetActive(false);
        for (int seat = 0; seat < 4; seat++)
        {
            rails[seat].gameObject.SetActive(false);
            Shape(badges[seat], new Vector2(-29.5f, -29.5f), new Vector2(14, 15),
                1.2f, "#2B5858", "#709485", .25f);
            // Small edge endpoints frame the real point stick only when it is
            // present; there is no empty outlined channel around the table.
            Detail(seat, 0, new Vector2(-21.2f, -35.2f), new Vector2(.55f, 4.8f), .12f,
                "#8FAD99", "#8FAD99", 0);
            Detail(seat, 1, new Vector2(21.2f, -35.2f), new Vector2(.55f, 4.8f), .12f,
                "#8FAD99", "#8FAD99", 0);
            Detail(seat, 2, new Vector2(0, -37.1f), new Vector2(39.8f, .3f), .1f,
                "#416B64", "#416B64", 0);
            // A single slim underline ties a wind plaque into the sheet edge.
            // It stays below the entire wind glyph area.
            Detail(seat, 3, new Vector2(-29.5f, -37.0f), new Vector2(9.2f, .5f), .15f,
                "#A9BDA2", "#A9BDA2", 0);
        }
    }

    static Vector2[] RoundedRect(float width, float height, float radius)
    {
        const int segmentsPerCorner = 6;
        float x = width * .5f, y = height * .5f;
        radius = Mathf.Clamp(radius, .1f, Mathf.Min(x, y));
        var points = new Vector2[4 * (segmentsPerCorner + 1)];
        for (int corner = 0; corner < 4; corner++)
        {
            var center = new Vector2(corner < 2 ? x - radius : -x + radius,
                corner == 0 || corner == 3 ? -y + radius : y - radius);
            for (int step = 0; step <= segmentsPerCorner; step++)
            {
                float angle = (-90 + corner * 90 + step * 90f / segmentsPerCorner) * Mathf.Deg2Rad;
                points[corner * (segmentsPerCorner + 1) + step] = center
                    + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;
            }
        }
        return points;
    }
}
