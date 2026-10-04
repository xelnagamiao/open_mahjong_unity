using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public sealed partial class CenterBoardSkin
{
    // Approved camera previews 01, 04, 05, 06, 07. Only artwork and appearance are
    // cached here; the original BoardCanvas continues to own all live readouts.
    int ApprovedFlatNumber => StyleId == CenterDisplayStyles.FlatFrame ? 1
        : StyleId == CenterDisplayStyles.FlatSeats ? 4
        : StyleId == CenterDisplayStyles.FlatTicket ? 5
        : StyleId == CenterDisplayStyles.FlatFold ? 6
        : StyleId == CenterDisplayStyles.FlatContrast ? 7 : 0;
    bool IsApprovedFlat => ApprovedFlatNumber != 0;
    readonly Dictionary<string, ApprovedFlatArt> approvedFlatArtwork = new Dictionary<string, ApprovedFlatArt>();

    sealed class ApprovedFlatArt
    {
        public RectTransform Face;
        public readonly RectTransform[] Seats = new RectTransform[4];
        public void SetActive(bool value)
        {
            Face.gameObject.SetActive(value);
            foreach (var seat in Seats) seat.gameObject.SetActive(value);
        }
    }

    void ApplyApprovedFlat()
    {
        int number = ApprovedFlatNumber;
        string[] palette = number == 1 ? new[] { "#142438", "#26496C", "#F4F4E9", "#91B7CF" }
            : number == 4 ? new[] { "#172132", "#3A4B62", "#F4F1E6", "#ABC1D1" }
            : number == 5 ? new[] { "#252C35", "#D8DDDA", "#FAF7EC", "#687E85" }
            : number == 6 ? new[] { "#211F26", "#753B42", "#F7EFE0", "#C7AAA0" }
            : new[] { "#20252C", "#E7E8DF", "#FAF8ED", "#79858A" };
        string ink = palette[0], panel = palette[1], paper = palette[2], edge = palette[3];
        bool band = number >= 4 && number <= 6;
        if (art) art.gameObject.SetActive(false);
        foreach (var element in seatArt) if (element) element.gameObject.SetActive(false);
        var hit = backgroundStates[0].image;
        if (hit) { hit.enabled = true; hit.color = Color.clear; }
        for (int i = 1; i < backgroundStates.Length; i++)
            if (backgroundStates[i].image) backgroundStates[i].image.enabled = false;

        if (!approvedFlatArtwork.TryGetValue(StyleId, out var artwork))
        {
            artwork = new ApprovedFlatArt();
            artwork.Face = NewRect(control, "FlatFace_" + StyleId, Vector2.zero, new Vector2(77, 77));
            artwork.Face.SetAsFirstSibling();
            FlatShape(artwork.Face, 0, 0, 77, 77, 1.5f, number == 5 || number == 7 ? panel : ink, number == 7 ? paper : edge, .65f);
            if (number == 1) FlatShape(artwork.Face, 0, 0, 35, 35, 1, ink, panel, .7f);
            if (number == 4) FlatShape(artwork.Face, 0, 0, 34, 34, 1, paper);
            if (number == 5)
            {
                FlatShape(artwork.Face, -16.5f, 0, .6f, 29, 0, edge);
                FlatShape(artwork.Face, 16.5f, 0, .6f, 29, 0, edge);
            }
            if (number == 6) FlatShape(artwork.Face, 0, 0, 35, 35, 2, ink, edge, .55f);
            if (number == 7) FlatShape(artwork.Face, 0, 0, 36, 36, 1, ink);
            for (int i = 0; i < 4; i++)
            {
                // Seat parents already provide rotation and occupancy visibility.
                var seat = artwork.Seats[i] = NewRect(scores[i].transform.parent, "FlatSeat_" + StyleId, Vector2.zero, new Vector2(77, 77));
                seat.SetAsFirstSibling();
                if (number == 1) FlatShape(seat, -28, -28, 14.5f, 14.5f, 1, panel);
                if (band)
                {
                    FlatShape(seat, -7, -28, 56, 14, number == 6 ? 2 : 0, number == 5 ? paper : panel);
                    if (number == 4) FlatShape(seat, -28, -28, 13, 13, 0, paper);
                    if (number == 5)
                    {
                        FlatShape(seat, -28, -28, 13, 14, 0, ink);
                        FlatShape(seat, -20, -28, .6f, 12, 0, edge);
                    }
                    if (number == 6) FlatShape(seat, -20.5f, -28, .6f, 10, 0, edge);
                }
                FlatShape(seat, 0, band ? -19.3f : number == 1 ? -34 : -34.5f,
                    band ? 33 : number == 1 ? 38 : 32, number == 1 ? 3.5f : 3,
                    band ? .4f : number == 1 ? .5f : .25f, number == 7 ? edge : panel);
            }
            approvedFlatArtwork.Add(StyleId, artwork);
        }
        artwork.SetActive(true);
        if (!graphiteFont) graphiteFont = Resources.Load<TMP_FontAsset>("font/CenterDisplay/GraphiteHeitiStatic");
        ConfigureRect(middle, Vector2.zero, new Vector2(36, 36));
        float centerSize = number == 7 ? 8.8f : 8.4f;
        string centerColor = number == 4 || number == 5 ? ink : paper;
        ApprovedFlatText(roundText, 0, 6, 32, centerSize, centerColor, 7);
        ApprovedFlatText(remainingText, 0, -6, 32, centerSize, centerColor, 7);
        ApplyCompactReadout();
        for (int i = 0; i < 4; i++)
        {
            ApprovedFlatText(winds[i], number == 7 ? -29 : -28, number == 7 ? -29 : -28,
                band ? 13 : 14, number == 1 ? 11 : number == 7 ? 10.7f : 10.5f,
                number == 4 || number == 7 ? ink : paper, 10.5f);
            ApprovedFlatText(scores[i], band ? -1 : 0, band ? -28 : number == 1 ? -24 : -26,
                band ? 35 : number == 1 ? 39 : 38, number == 7 ? 7.7f : 7.8f,
                number == 5 || number == 7 ? ink : paper, 6.2f);
            ConfigureRect(turns[i].rectTransform, new Vector2(0, band ? -19.3f : number == 1 ? -34 : -34.5f),
                new Vector2(band ? 33 : number == 1 ? 38 : 32, number == 1 ? 3.5f : 3));
            Color orange = C("#EE9C62"); orange.a = turns[i].color.a; turns[i].color = orange;
            turns[i].overrideSprite = null; turns[i].sprite = null;
            turns[i].type = Image.Type.Simple; turns[i].useSpriteMesh = false; turns[i].preserveAspect = false;
            if (!turnMeshes[i]) turnMeshes[i] = turns[i].GetComponent<CenterTurnMesh>();
            if (!turnMeshes[i]) turnMeshes[i] = turns[i].gameObject.AddComponent<CenterTurnMesh>();
            turnMeshes[i].Cut = band ? .4f : number == 1 ? .5f : .25f;
            turnMeshes[i].Taper = false; turnMeshes[i].RoundedTrapezoid = false;
            turnMeshes[i].Highlight = 0; turnMeshes[i].enabled = true;
        }
    }

    static void ApprovedFlatText(TMP_Text text, float x, float y, float width, float size, string color, float min)
    {
        GraphiteText(text, new Vector2(x, y), new Vector2(width, Mathf.Max(12, size * 1.5f)), size, color, min);
    }

    static void FlatShape(Transform parent, float x, float y, float width, float height, float cut, string fill, string stroke = null, float thickness = 0)
    {
        var rect = NewRect(parent, "FlatShape", new Vector2(x, y), new Vector2(width, height));
        var polygon = cut < .01f
            ? new[] { new Vector2(-width / 2, -height / 2), new Vector2(width / 2, -height / 2), new Vector2(width / 2, height / 2), new Vector2(-width / 2, height / 2) }
            : CenterSkinGraphic.CutRect(width, height, cut);
        rect.gameObject.AddComponent<CenterSkinGraphic>().SetShape(polygon, C(fill), stroke == null ? Color.clear : C(stroke), thickness);
    }
}
