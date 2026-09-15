using UnityEngine;

public sealed partial class CenterBoardSkin
{
    // Original folded-paper construction. The four score fields share one
    // uninterrupted face; only the readout has two slightly offset paper edges.
    void PaintStudioIndigo()
    {
        Polygon(face[0], RoundedRect(77, 77, 2.6f),
            "#10172E", "#51649C", .45f);

        // Opposite corners have a longer paper cut. The full underlying sheet
        // retains the 77-unit footprint, without separate projecting wind ears.
        Polygon(face[1], new[] {
            new Vector2(-35.9f, -37.2f),
            new Vector2(34.0f, -37.2f),
            new Vector2(37.2f, -34.0f),
            new Vector2(37.2f, 35.9f),
            new Vector2(35.9f, 37.2f),
            new Vector2(-34.0f, 37.2f),
            new Vector2(-37.2f, 34.0f),
            new Vector2(-37.2f, -35.9f)
        }, "#202C55", "#2A3A65", .3f);

        // A lit upper-left edge and a dark lower-right edge describe a folded
        // leaf rather than a concentric frame. All three stay within +/-16.6,
        // clear of the original four score RectTransforms starting at 17.1.
        Shape(face[2], new Vector2(.5f, -.7f), new Vector2(31.8f, 31.8f),
            1.8f, "#0D142B", "#00000000", 0);
        Shape(face[3], new Vector2(-.45f, .6f), new Vector2(32, 32),
            1.8f, "#6179C9", "#00000000", 0);
        Polygon(face[4], RoundedRect(31, 31, 1.8f),
            "#141D3C", "#3C5089", .3f);

        for (int seat = 0; seat < 4; seat++)
        {
            // The real stick at y=-35.2 and the live orange rectangle at
            // y=-30.6 sit directly on the paper; neither gets a drawn trough.
            rails[seat].gameObject.SetActive(false);

            // A narrow return fold occupies the gap between the wind plaque,
            // score field and real stick. Its inner tip stops before either
            // neighbouring score box; no crease crosses a text field.
            DetailPolygon(seat, 0, new[] {
                new Vector2(-22.1f, -37.4f),
                new Vector2(-20.9f, -35.9f),
                new Vector2(-20.9f, -23.5f),
                new Vector2(-21.6f, -21.6f),
                new Vector2(-22.1f, -23.5f)
            }, "#344780", "#00000000", 0);
            DetailPolygon(seat, 1, new[] {
                new Vector2(-21.1f, -35.7f),
                new Vector2(-20.7f, -35.3f),
                new Vector2(-20.7f, -23.5f),
                new Vector2(-21.1f, -22.4f)
            }, "#6178BD", "#00000000", 0);

            // Small inset wind labels stay inside the sheet. Their fill and
            // live text colours remain under UpdateWindColors ownership.
            Shape(badges[seat], new Vector2(-29.5f, -29.5f),
                new Vector2(14, 15.2f), 1.7f,
                "#26345A", "#5168A4", .3f);
        }
    }
}
