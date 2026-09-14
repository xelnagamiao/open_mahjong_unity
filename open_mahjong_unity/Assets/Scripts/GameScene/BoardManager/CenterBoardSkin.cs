using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Applies a selected center appearance to the existing game UI. Never owns game
/// text, scores, occupancy, riichi state, or the current-player coroutine.
/// </summary>
public sealed partial class CenterBoardSkin : MonoBehaviour
{
    public string StyleId { get; private set; } = "classic";
    static readonly Color Orange = new Color(.93207544f, .61297697f, .3851406f, 1);
    static readonly string[] ScoreColors = { "#F2D376", "#E9DBA4", "#35455A", "#E5D5B5" };
    static readonly string[][] Palettes = {
        new[] { "#50586A", "#252D3D", "#42495C", "#6A7486", "#2B3142", "#566174", "#243040", "#567180", "#475165", "#35415A", "#C44A61" },
        new[] { "#3D5457", "#203539", "#334B50", "#627C7A", "#243B41", "#48676B", "#1F343C", "#537B7C", "#3C565A", "#3D575D", "#BA5866" },
        new[] { "#AFB8C4", "#6D7B8E", "#CBD1D9", "#DCE0E5", "#BCC5D0", "#94A2B3", "#2D4050", "#708B99", "#A6B2C1", "#A2AEBD", "#B96370" },
        new[] { "#534D64", "#302A40", "#484255", "#766B83", "#353143", "#61566F", "#2A2C3E", "#706780", "#514A62", "#4C465F", "#BD576B" }
    };
    // Restored from the V2 source snapshot, independently selectable from the new designs.
    static readonly string[][] TrialPalettes = {
        new[] { "#417BEB", "#0B1428", "#152238", "#28456B", "#152238", "#28456B", "#0D192C", "#28456B", "#152238", "#215AC4", "#DD4856" },
        new[] { "#20BBA7", "#062F36", "#08424B", "#16616B", "#08424B", "#16616B", "#07343D", "#16616B", "#08424B", "#087C79", "#E44857" },
        new[] { "#223F72", "#132B50", "#F6EFDD", "#FFFAED", "#F6EFDD", "#D2BFA3", "#F6EFDD", "#F6EFDD", "#F6EFDD", "#245DB1", "#D9424D" }
    };
    static readonly string[] TrialScoreColors = { "#FFE6A2", "#FFF0B5", "#153660" };
    static readonly string[] TrialRoundColors = { "#6FE5FF", "#61F2D8", "#17498E" };
    static readonly string[] TrialRemainingColors = { "#D8F6FF", "#E9FFF4", "#B93243" };
    // Score, round, remaining, wind ink, wind fill, east fill, east ink.
    static readonly string[][] StudioColors = {
        new[] { "#E8EEFF", "#A8BAFF", "#D0DAFF", "#DDE6FF", "#26345A", "#B54F67", "#FFF3F3" },
        new[] { "#552D36", "#703242", "#4B3438", "#6C3E49", "#EBDCCF", "#D7A9AE", "#622333" },
        new[] { "#ECDEC0", "#3E5750", "#264F4D", "#DBE9D8", "#2B5858", "#AB5753", "#FFF3DE" }
    };
    static TMP_FontAsset centerFont;

    BoardCanvas board;
    RectTransform control, middle, art;
    TMP_Text roundText, remainingText;
    TMP_Text[] scores, winds;
    Image[] turns;
    TextState[] textStates;
    ImageState[] turnStates, backgroundStates;
    RectState middleState;
    readonly CenterSkinGraphic[] face = new CenterSkinGraphic[5], rails = new CenterSkinGraphic[4], badges = new CenterSkinGraphic[4];
    readonly CenterSkinGraphic[][] details = new CenterSkinGraphic[4][];
    CenterSkinGraphic readoutRule;
    readonly RectTransform[] seatArt = new RectTransform[4];
    readonly string[] previousWind = new string[4];
    readonly List<AnchorState> pointAnchors = new List<AnchorState>();
    readonly CenterTurnMesh[] turnMeshes = new CenterTurnMesh[4];
    ConfigManager subscribedConfig;
    bool initialized, watchConfiguration;
    int paletteIndex = -1;
    bool IsOriginalFlat => StyleId == CenterDisplayStyles.OriginalFlat;
    bool IsTrial => StyleId == CenterDisplayStyles.TrialCobalt || StyleId == CenterDisplayStyles.TrialJade || StyleId == CenterDisplayStyles.TrialIvory;
    bool IsStudio => StyleId == CenterDisplayStyles.StudioIndigo || StyleId == CenterDisplayStyles.StudioPaper || StyleId == CenterDisplayStyles.StudioBamboo;
    string[] CurrentPalette => IsTrial ? TrialPalettes[paletteIndex] : Palettes[paletteIndex];
    float? PointAnchorY => paletteIndex < 0 || IsOriginalFlat ? (float?)null
        : IsStudio ? -35.2f : IsTrial ? -31.6f : -31.3f;

    public void Initialize(BoardCanvas owner, TMP_Text round, TMP_Text remaining, TMP_Text[] scoreLabels, TMP_Text[] windLabels, Image[] turnImages)
    {
        if (initialized) return;
        board = owner; roundText = round; remainingText = remaining;
        scores = scoreLabels; winds = windLabels; turns = turnImages;
        control = board.transform.Find("ControlPanel -1") as RectTransform;
        middle = control ? control.Find("ControlPanelMid") as RectTransform : null;
        if (!control || !middle || !roundText || !remainingText || scores.Length != 4 || winds.Length != 4 || turns.Length != 4)
            throw new InvalidOperationException("Original BoardCanvas center references are incomplete.");
        for (int i = 0; i < 4; i++)
            if (!scores[i] || !winds[i] || !turns[i])
                throw new InvalidOperationException("Original BoardCanvas seat references are incomplete.");
        middleState = new RectState(middle);
        textStates = new TextState[10];
        textStates[0] = new TextState(roundText); textStates[1] = new TextState(remainingText);
        turnStates = new ImageState[4];
        for (int i = 0; i < 4; i++)
        {
            textStates[2 + i] = new TextState(scores[i]);
            textStates[6 + i] = new TextState(winds[i]);
            turnStates[i] = new ImageState(turns[i]);
        }
        backgroundStates = new[] {
            new ImageState(control.GetComponent<Image>()),
            new ImageState(control.Find("Panel")?.GetComponent<Image>()),
            new ImageState(middle.GetComponent<Image>())
        };
        initialized = true;
        TryInitializePointAnchors();
    }

    public void BeginConfiguredAppearance()
    {
        watchConfiguration = true;
        TryConnectConfiguration();
    }

    public void DetachConfiguration()
    {
        if (subscribedConfig) subscribedConfig.CenterDisplayChanged -= Apply;
        subscribedConfig = null;
        watchConfiguration = false;
    }

    void TryConnectConfiguration()
    {
        var config = ConfigManager.Instance;
        if (!config || config == subscribedConfig) return;
        if (subscribedConfig) subscribedConfig.CenterDisplayChanged -= Apply;
        subscribedConfig = config;
        subscribedConfig.CenterDisplayChanged += Apply;
        Apply(subscribedConfig.SelectedCenterDisplayId);
    }

    void LateUpdate()
    {
        if (!initialized) return;
        if (watchConfiguration) TryConnectConfiguration();
        if (pointAnchors.Count < 4) TryInitializePointAnchors();
        if (paletteIndex >= 0) UpdateWindColors(false);
    }

    public void Apply(string id)
    {
        if (!initialized) return;
        id = CenterDisplayStyles.Normalize(id);
        int index = CenterDisplayStyles.IndexOf(id);
        StyleId = id;
        // Palette selection is independent of the catalog order.
        paletteIndex = id == CenterDisplayStyles.Classic ? -1
            : id == "ink" || id == CenterDisplayStyles.TrialJade || id == CenterDisplayStyles.StudioPaper ? 1
            : id == "ivory" || id == CenterDisplayStyles.TrialIvory || id == CenterDisplayStyles.StudioBamboo ? 2
            : id == "violet" ? 3 : 0;
        if (index == 0)
        {
            if (art) art.gameObject.SetActive(false);
            foreach (var element in seatArt) if (element) element.gameObject.SetActive(false);
            middleState.Restore();
            foreach (var state in textStates) state.Restore();
            foreach (var state in backgroundStates) state.Restore(false);
            // Preserve the live flash alpha; only restore the saved appearance.
            foreach (var state in turnStates) state.Restore(true);
            foreach (var effect in turnMeshes) if (effect) effect.enabled = false;
        }
        else
        {
            EnsureArtwork();
            art.gameObject.SetActive(true);
            foreach (var element in seatArt) element.gameObject.SetActive(true);
            // Retain the original parent Image as a transparent click target.
            // Disabling this GameObject would also hide its dynamic children.
            var hit = backgroundStates[0].image;
            if (hit) { hit.enabled = true; hit.color = Color.clear; }
            for (int i = 1; i < backgroundStates.Length; i++)
                if (backgroundStates[i].image) backgroundStates[i].image.enabled = false;
            bool original = IsOriginalFlat;
            bool trial = IsTrial;
            bool studio = IsStudio;
            ConfigureRect(middle, Vector2.zero, original ? new Vector2(29.8f, 29.4f) : studio ? new Vector2(31, 31) : trial ? new Vector2(36, 34) : new Vector2(36, 36));
            ConfigureText(roundText, new Vector2(0, original ? 4 : studio ? 5.2f : trial ? 5.7f : 6.8f), original ? new Vector2(26, 11) : new Vector2(studio ? 29 : 33, 14), original ? 7.4f : studio ? 8.7f : trial ? 9.6f : 8.4f,
                C(studio ? StudioColors[paletteIndex][1] : trial ? TrialRoundColors[paletteIndex] : "#74DECE"));
            // Four-character rule names retain their actual text and fit the same readout.
            roundText.enableAutoSizing = true; roundText.fontSizeMin = original ? 6.4f : studio ? 7.1f : 7.8f;
            roundText.fontSizeMax = roundText.fontSize;
            ConfigureText(remainingText, new Vector2(0, original ? -4.1f : studio ? -5.8f : trial ? -6.1f : -6.5f), new Vector2(original ? 26 : studio ? 29 : 33, 14), original ? 7.4f : studio ? 9.7f : trial ? 10.2f : 8.6f,
                C(studio ? StudioColors[paletteIndex][2] : trial ? TrialRemainingColors[paletteIndex] : "#A3EEE2"));
            if (studio) { remainingText.enableAutoSizing = true; remainingText.fontSizeMin = 8; remainingText.fontSizeMax = 9.7f; }
            for (int i = 0; i < 4; i++)
            {
                float scoreSize = original ? 7.8f : studio ? 8.6f : trial ? 9.4f : 8;
                ConfigureText(scores[i], new Vector2(0, original ? -20.1f : studio ? -23.6f : -23.8f), original ? new Vector2(35, 11.5f) : new Vector2(40, 13), scoreSize,
                    C(studio ? StudioColors[paletteIndex][0] : trial ? TrialScoreColors[paletteIndex] : ScoreColors[paletteIndex]));
                scores[i].margin = new Vector4(.7f, 0, .7f, 0);
                scores[i].enableAutoSizing = true; scores[i].fontSizeMin = 6.2f; scores[i].fontSizeMax = scoreSize;
                ConfigureText(winds[i], new Vector2(studio ? -29.5f : -29.4f, studio ? -29.5f : -29.4f), new Vector2(14, 16), studio ? 10.5f : 11, Color.white);
                ConfigureRect(turns[i].rectTransform, new Vector2(0, studio ? -30.6f : trial ? -36.3f : original ? -34.1f : -36),
                    new Vector2(studio ? 30 : original ? 40 : 42, studio ? 2.4f : trial ? 2.8f : 3.2f));
                Color orange = studio ? C("#EFAD59") : trial ? C("#FF8D32") : Orange; orange.a = turns[i].color.a; turns[i].color = orange;
                turns[i].overrideSprite = null; turns[i].sprite = null;
                turns[i].type = Image.Type.Simple; turns[i].useSpriteMesh = false; turns[i].preserveAspect = false;
                if (!turnMeshes[i])
                {
                    turnMeshes[i] = turns[i].GetComponent<CenterTurnMesh>();
                    if (!turnMeshes[i]) turnMeshes[i] = turns[i].gameObject.AddComponent<CenterTurnMesh>();
                }
                turnMeshes[i].Cut = studio ? .5f : trial ? .3f : .8f;
                turnMeshes[i].Taper = false;
                turnMeshes[i].Highlight = 0;
                turnMeshes[i].enabled = true;
            }
            PaintArtwork();
            UpdateWindColors(true);
        }
        TryInitializePointAnchors();
        foreach (var anchor in pointAnchors) anchor.Apply(PointAnchorY);
    }

    void EnsureArtwork()
    {
        if (art) return;
        art = NewRect(control, "CenterSkinArt", Vector2.zero, new Vector2(77, 77));
        art.SetAsFirstSibling();
        string[] names = { "OuterContour", "FlatFace", "IntegratedOctagonalFace", "FaceKeyline", "CentralReadout" };
        for (int i = 0; i < face.Length; i++) face[i] = NewGraphic(art, names[i]);
        readoutRule = NewGraphic(art, "ReadoutDivider");
        for (int i = 0; i < 4; i++)
        {
            // Occupied-seat logic hides Score.parent. Keep each decoration there.
            seatArt[i] = NewRect(scores[i].transform.parent, "CenterSkinSeat", Vector2.zero, new Vector2(77, 77));
            seatArt[i].SetAsFirstSibling();
            rails[i] = NewGraphic(seatArt[i], "EdgeRail");
            details[i] = new CenterSkinGraphic[8];
            for (int j = 0; j < details[i].Length; j++) details[i][j] = NewGraphic(seatArt[i], "ReferenceDetail_" + j);
            badges[i] = NewGraphic(seatArt[i], "WindBadge");
        }
    }

    void PaintArtwork()
    {
        foreach (var graphic in face) graphic.gameObject.SetActive(true);
        foreach (var graphic in rails) graphic.gameObject.SetActive(true);
        foreach (var graphic in badges) graphic.gameObject.SetActive(true);
        foreach (var seat in details) foreach (var graphic in seat) graphic.gameObject.SetActive(false);
        readoutRule.gameObject.SetActive(false);
        if (IsStudio)
        {
            if (StyleId == CenterDisplayStyles.StudioIndigo) PaintStudioIndigo();
            else if (StyleId == CenterDisplayStyles.StudioPaper) PaintStudioPaper();
            else PaintStudioBamboo();
            return;
        }
        if (IsOriginalFlat)
        {
            PaintOriginalArtwork();
            return;
        }
        if (IsTrial)
        {
            PaintTrialArtwork();
            return;
        }
        var p = Palettes[paletteIndex];
        Shape(face[0], Vector2.zero, new Vector2(77, 77), 3.2f, p[0], p[1], .45f);
        Shape(face[1], Vector2.zero, new Vector2(75, 75), 2.8f, p[2], p[3], .22f);
        Shape(face[2], Vector2.zero, new Vector2(61.4f, 61.4f), 9.4f, p[4], p[1], .38f);
        Shape(face[3], Vector2.zero, new Vector2(60.1f, 60.1f), 9, p[4], p[5], .22f);
        Shape(face[4], Vector2.zero, new Vector2(36, 36), 1.5f, p[6], p[7], .27f);
        for (int i = 0; i < 4; i++)
        {
            Shape(rails[i], new Vector2(0, -36), new Vector2(44, 3.8f), 1, p[8], p[1], .22f);
            Shape(badges[i], new Vector2(-29.4f, -29.4f), new Vector2(14, 14), 1, p[9], p[1], .3f);
        }
    }

    void PaintTrialArtwork()
    {
        var p = CurrentPalette;
        Shape(face[0], Vector2.zero, new Vector2(77, 77), 1.8f, p[0], p[1], .3f);
        Shape(face[1], Vector2.zero, new Vector2(75.2f, 75.2f), 1.3f, p[2], p[3], .15f);
        face[2].gameObject.SetActive(false);
        face[3].gameObject.SetActive(false);
        Shape(face[4], Vector2.zero, new Vector2(36, 34), .5f, p[6], p[7], paletteIndex == 2 ? 0 : .18f);
        for (int i = 0; i < 4; i++)
        {
            rails[i].gameObject.SetActive(false);
            Shape(badges[i], new Vector2(-29.4f, -29.4f), new Vector2(14, 14), .4f, p[9], p[9], 0);
        }
    }

    void PaintOriginalArtwork()
    {
        Shape(face[0], Vector2.zero, new Vector2(77, 77), 3.2f, "#555D70", "#171D2A", .85f);
        Shape(face[1], Vector2.zero, new Vector2(74.4f, 74.4f), 2.8f, "#42495C", "#798397", .38f);
        Shape(face[2], Vector2.zero, new Vector2(61.4f, 61.4f), 9.4f, "#292F40", "#202635", .75f);
        Shape(face[3], Vector2.zero, new Vector2(59.5f, 59.5f), 8.8f, "#292F40", "#677184", .38f);
        Shape(face[4], Vector2.zero, new Vector2(29.8f, 29.4f), 2, "#222B3B", "#536777", .35f);
        for (int i = 0; i < 4; i++)
        {
            Shape(rails[i], new Vector2(0, -34.1f), new Vector2(42, 3.8f), 1, "#535D73", "#2A3244", .3f);
            Shape(badges[i], new Vector2(-29.4f, -29.4f), new Vector2(14, 14), .8f, "#35415A", "#1E2738", .4f);
        }
    }

    void UpdateWindColors(bool force)
    {
        var p = IsStudio ? null : CurrentPalette;
        for (int i = 0; i < 4; i++)
        {
            string value = winds[i].text;
            if (!force && previousWind[i] == value) continue;
            previousWind[i] = value;
            bool east = value == "东" || value == "東";
            badges[i].color = C(IsStudio ? StudioColors[paletteIndex][east ? 5 : 4] : east ? p[10] : p[9]);
            winds[i].color = C(IsStudio ? StudioColors[paletteIndex][east ? 6 : 3] : !IsTrial && paletteIndex == 2 && !east ? "#35465C" : "#FFFCF1");
        }
    }

    void TryInitializePointAnchors()
    {
        if (!board) return;
        foreach (var panel in board.GetComponentsInChildren<PosPanel3D>(true))
        {
            Transform target = panel.tenbouPos;
            if (!target || pointAnchors.Exists(s => s.target == target)) continue;
            var state = new AnchorState(target);
            pointAnchors.Add(state);
            state.Apply(PointAnchorY);
        }
    }

    void OnDestroy()
    {
        DetachConfiguration();
    }

    static Color C(string value) { ColorUtility.TryParseHtmlString(value, out var result); return result; }
    static RectTransform NewRect(Transform parent, string name, Vector2 position, Vector2 size)
    {
        var go = new GameObject(name, typeof(RectTransform)); go.layer = parent.gameObject.layer;
        var rect = go.GetComponent<RectTransform>(); rect.SetParent(parent, false);
        ConfigureRect(rect, position, size); return rect;
    }
    static CenterSkinGraphic NewGraphic(Transform parent, string name) =>
        NewRect(parent, name, Vector2.zero, new Vector2(77, 77)).gameObject.AddComponent<CenterSkinGraphic>();
    static void ConfigureRect(RectTransform rect, Vector2 position, Vector2 size)
    {
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(.5f, .5f);
        rect.localScale = Vector3.one; rect.localRotation = Quaternion.identity;
        rect.sizeDelta = size; rect.anchoredPosition = position;
    }
    static void Shape(CenterSkinGraphic graphic, Vector2 position, Vector2 size, float cut, string fill, string edge, float width)
    {
        ConfigureRect(graphic.rectTransform, position, size);
        graphic.SetShape(CenterSkinGraphic.CutRect(size.x, size.y, cut), C(fill), C(edge), width);
    }
    static void FlatRectangle(CenterSkinGraphic graphic, Vector2 size, Color fill)
    {
        ConfigureRect(graphic.rectTransform, Vector2.zero, size);
        graphic.SetShape(CenterSkinGraphic.CutRect(size.x, size.y, .12f), fill, Color.clear, 0);
    }
    CenterSkinGraphic Detail(int seat, int index, Vector2 position, Vector2 size, float cut, string fill, string edge, float width)
    {
        var graphic = details[seat][index]; graphic.gameObject.SetActive(true);
        Shape(graphic, position, size, cut, fill, edge, width); return graphic;
    }
    CenterSkinGraphic DetailPolygon(int seat, int index, Vector2[] points, string fill, string edge, float width)
    {
        var graphic = details[seat][index]; graphic.gameObject.SetActive(true);
        Polygon(graphic, points, fill, edge, width); return graphic;
    }
    static void Polygon(CenterSkinGraphic graphic, Vector2[] points, string fill, string edge, float width)
    {
        ConfigureRect(graphic.rectTransform, Vector2.zero, new Vector2(77, 77));
        graphic.SetShape(points, C(fill), C(edge), width);
    }
    static void ConfigureText(TMP_Text text, Vector2 position, Vector2 size, float fontSize, Color color)
    {
        ConfigureRect(text.rectTransform, position, size);
        if (!centerFont) centerFont = Resources.Load<TMP_FontAsset>("font/CenterDisplay/CenterReadoutStatic");
        if (centerFont) { text.font = centerFont; text.fontSharedMaterial = centerFont.material; }
        text.color = color; text.enableAutoSizing = false; text.fontSize = fontSize;
        text.characterSpacing = 0;
        text.fontStyle = FontStyles.Normal; text.alignment = TextAlignmentOptions.Center;
        text.textWrappingMode = TextWrappingModes.NoWrap; text.overflowMode = TextOverflowModes.Overflow;
        text.margin = Vector4.zero;
    }

    sealed class RectState
    {
        readonly RectTransform rect;
        readonly Vector2 min, max, pivot, size;
        readonly Vector3 position, scale;
        readonly Quaternion rotation;
        public RectState(RectTransform value)
        {
            rect = value; min = rect.anchorMin; max = rect.anchorMax; pivot = rect.pivot;
            size = rect.sizeDelta; position = rect.anchoredPosition3D; scale = rect.localScale; rotation = rect.localRotation;
        }
        public void Restore()
        {
            if (!rect) return;
            rect.anchorMin = min; rect.anchorMax = max; rect.pivot = pivot; rect.sizeDelta = size;
            rect.anchoredPosition3D = position; rect.localScale = scale; rect.localRotation = rotation;
        }
    }
    sealed class TextState
    {
        readonly TMP_Text text;
        readonly RectState rect;
        readonly Color color;
        readonly TMP_FontAsset font;
        readonly Material fontMaterial;
        readonly float characterSpacing;
        readonly float fontSize, min, max;
        readonly bool auto;
        readonly FontStyles style;
        readonly TextAlignmentOptions alignment;
        readonly TextWrappingModes wrapping;
        readonly TextOverflowModes overflow;
        readonly Vector4 margin;
        public TextState(TMP_Text value)
        {
            text = value; rect = new RectState(text.rectTransform); color = text.color;
            font = text.font; fontMaterial = text.fontSharedMaterial; characterSpacing = text.characterSpacing;
            fontSize = text.fontSize; min = text.fontSizeMin; max = text.fontSizeMax; auto = text.enableAutoSizing;
            style = text.fontStyle; alignment = text.alignment; wrapping = text.textWrappingMode;
            overflow = text.overflowMode; margin = text.margin;
        }
        public void Restore()
        {
            if (!text) return;
            text.font = font; text.fontSharedMaterial = fontMaterial; text.characterSpacing = characterSpacing;
            rect.Restore(); text.color = color; text.enableAutoSizing = auto; text.fontSize = fontSize;
            text.fontSizeMin = min; text.fontSizeMax = max; text.fontStyle = style;
            text.alignment = alignment; text.textWrappingMode = wrapping; text.overflowMode = overflow; text.margin = margin;
        }
    }
    sealed class ImageState
    {
        public readonly Image image;
        readonly RectState rect;
        readonly Color color;
        readonly Sprite sprite, over;
        readonly Image.Type type;
        readonly bool enabled, mesh, aspect;
        public ImageState(Image value)
        {
            image = value; if (!image) return;
            rect = new RectState(image.rectTransform); color = image.color; enabled = image.enabled;
            sprite = image.sprite; over = image.overrideSprite; type = image.type; mesh = image.useSpriteMesh; aspect = image.preserveAspect;
        }
        public void Restore(bool keepFlashAlpha)
        {
            if (!image) return;
            float alpha = image.color.a;
            rect.Restore(); image.sprite = sprite; image.overrideSprite = over; image.type = type;
            image.useSpriteMesh = mesh; image.preserveAspect = aspect; image.enabled = enabled;
            Color restored = color; if (keepFlashAlpha) restored.a = alpha; image.color = restored;
        }
    }
    sealed class AnchorState
    {
        public readonly Transform target;
        readonly Vector3 position;
        readonly RectTransform rect;
        public AnchorState(Transform value)
        {
            target = value; rect = target as RectTransform;
            position = rect ? rect.anchoredPosition3D : target.localPosition;
        }
        public void Apply(float? seatY)
        {
            if (!target) return;
            Vector3 before = target.position;
            var settled = new List<Transform>();
            if (target.parent)
                foreach (Transform child in target.parent)
                    if ((child.name.StartsWith("RiichiTenbou_", StringComparison.Ordinal) || child.name.StartsWith("FieldRiichiTenbou_", StringComparison.Ordinal))
                        && Vector3.Distance(child.position, before) < .05f) settled.Add(child);
            Vector3 next = position; if (seatY.HasValue) next.y = seatY.Value;
            if (rect) rect.anchoredPosition3D = next; else target.localPosition = next;
            Vector3 delta = target.position - before;
            foreach (var stick in settled) if (stick) stick.position += delta;
            // In-flight instances are deliberately not moved here.
            // Game3DManager's flight uses the live target Transform.
        }
    }
}
