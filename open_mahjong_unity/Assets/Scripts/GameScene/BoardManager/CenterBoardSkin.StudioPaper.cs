using UnityEngine;

public sealed partial class CenterBoardSkin
{
    // Original stationery composition: a warm paper sheet, a bound central
    // note and four restrained bookmarks. Live labels and indicators retain
    // their existing BoardCanvas objects and game-state ownership.
    void PaintStudioPaper()
    {
        const string paper = "#F3E8D9";
        const string ink = "#713546";

        // One straight-edged sheet, without a concentric rounded enclosure.
        Shape(face[0], Vector2.zero, new Vector2(77, 77), .70f,
            paper, "#693442", .55f);

        // The slight paper-tone change and a single binding line establish the
        // center note. The line is outside the 31-unit live readout rectangle.
        Shape(face[1], Vector2.zero, new Vector2(31.8f, 31.8f), .35f,
            "#FFF7EB", "#D6BDAF", .35f);
        Shape(face[2], new Vector2(-16.25f, 0), new Vector2(.70f, 31.8f), .10f,
            ink, ink, 0);
        Shape(face[3], new Vector2(0, -.1f), new Vector2(25.2f, .30f), .05f,
            "#D3AE9F", "#D3AE9F", 0);
        face[4].gameObject.SetActive(false);

        for (int seat = 0; seat < 4; seat++)
        {
            // Real riichi sticks occupy y=-35.2. Leaving this paper margin
            // plain avoids a second decorative track alongside the turn light.
            rails[seat].gameObject.SetActive(false);

            // A pale bookmark carries the score, with a red binding edge just
            // outside its 40-unit text rectangle. Its lower rule ends before
            // the orange Image at y=-30.6, leaving both indicators unobstructed.
            Detail(seat, 0, new Vector2(0, -23.2f), new Vector2(41.2f, 12), .40f,
                "#EFE0D2", "#D8BBAA", .25f);
            Detail(seat, 1, new Vector2(-20.8f, -23.2f), new Vector2(.60f, 12), .10f,
                ink, ink, 0);
            Detail(seat, 2, new Vector2(0, -28.85f), new Vector2(34, .30f), .05f,
                "#BD8D85", "#BD8D85", 0);

            // A quiet square paper label sits entirely inside the silhouette.
            // Its fill is updated by the existing wind-state colour handling.
            Shape(badges[seat], new Vector2(-29.5f, -29.5f), new Vector2(14.2f, 14.2f), .45f,
                "#EBDCCF", "#C7A397", .30f);
        }
    }
}
