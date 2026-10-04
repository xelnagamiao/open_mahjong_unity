using System.Text.RegularExpressions;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public sealed partial class CenterBoardSkin
{
    bool IsGraphiteImage => StyleId == CenterDisplayStyles.GraphiteImage || StyleId == CenterDisplayStyles.GraphiteImage2;
    RawImage graphiteBackground;
    Material graphiteEastMaterial;
    static readonly int EastCorners = Shader.PropertyToID("_EastCorners");
    static TMP_FontAsset graphiteFont;
    bool graphiteTextApplied;
    ITextPreprocessor savedRoundPreprocessor, savedRemainingPreprocessor;

    void ApplyGraphiteImage()
    {
        if (art) art.gameObject.SetActive(false);
        foreach (var element in seatArt) if (element) element.gameObject.SetActive(false);
        var hit = backgroundStates[0].image;
        if (hit) { hit.enabled = true; hit.color = Color.clear; }
        for (int i = 1; i < backgroundStates.Length; i++)
            if (backgroundStates[i].image) backgroundStates[i].image.enabled = false;
        if (!graphiteBackground)
        {
            var rect = NewRect(control, "GraphiteImageBackground", Vector2.zero, new Vector2(77, 77));
            rect.SetAsFirstSibling();
            graphiteBackground = rect.gameObject.AddComponent<RawImage>();
            graphiteBackground.color = Color.white;
            graphiteBackground.raycastTarget = false;
        }
        graphiteBackground.texture = Resources.Load<Texture2D>("image/Board/CenterDisplay/" + StyleId + "_background");
        graphiteBackground.gameObject.SetActive(true);
        if (StyleId == CenterDisplayStyles.GraphiteImage2 && !graphiteEastMaterial)
        {
            var shader = Resources.Load<Shader>("image/Board/CenterDisplay/CenterEastTint");
            if (shader) graphiteEastMaterial = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
        }
        graphiteBackground.material = StyleId == CenterDisplayStyles.GraphiteImage2 ? graphiteEastMaterial : null;
        UpdateGraphiteEast();
        if (!graphiteFont) graphiteFont = Resources.Load<TMP_FontAsset>("font/CenterDisplay/GraphiteHeitiStatic");
        ConfigureRect(middle, Vector2.zero, new Vector2(25, 25));
        GraphiteText(roundText, new Vector2(0, 4.9f), new Vector2(22, 12), 7.2f, "#E7E7E7", 5.5f);
        GraphiteText(remainingText, new Vector2(0, -5.2f), new Vector2(22, 12), 7.2f, "#E7E7E7", 6);
        ApplyCompactReadout();
        for (int i = 0; i < 4; i++)
        {
            GraphiteText(scores[i], new Vector2(0, -20.8f), new Vector2(29, 12), 6.6f, "#D7D7D7", 5);
            GraphiteText(winds[i], new Vector2(-28, -28), new Vector2(15, 18), 11.5f, "#D7D7D7", 10);
            ConfigureRect(turns[i].rectTransform, new Vector2(0, -28.05f), new Vector2(31.45f, 4.05f));
            Color orange = Orange; orange.a = turns[i].color.a; turns[i].color = orange;
            turns[i].overrideSprite = null; turns[i].sprite = null;
            turns[i].type = Image.Type.Simple; turns[i].useSpriteMesh = false; turns[i].preserveAspect = false;
            if (!turnMeshes[i]) turnMeshes[i] = turns[i].GetComponent<CenterTurnMesh>();
            if (!turnMeshes[i]) turnMeshes[i] = turns[i].gameObject.AddComponent<CenterTurnMesh>();
            turnMeshes[i].Taper = false; turnMeshes[i].RoundedTrapezoid = true;
            turnMeshes[i].Highlight = 0; turnMeshes[i].enabled = true;
        }
    }

    void UpdateGraphiteEast()
    {
        if (!graphiteEastMaterial || StyleId != CenterDisplayStyles.GraphiteImage2) return;
        var corners = Vector4.zero;
        for (int i = 0; i < winds.Length; i++)
        {
            string wind = winds[i].text;
            corners[i] = wind.Contains("东") || wind.Contains("東") ? 1 : 0;
        }
        graphiteEastMaterial.SetVector(EastCorners, corners);
    }

    static void GraphiteText(TMP_Text text, Vector2 position, Vector2 size, float fontSize, string color, float minimum)
    {
        ConfigureText(text, position, size, fontSize, C(color));
        if (graphiteFont) { text.font = graphiteFont; text.fontSharedMaterial = graphiteFont.material; }
        text.enableAutoSizing = true; text.fontSizeMin = minimum; text.fontSizeMax = fontSize;
    }

    void ApplyCompactReadout()
    {
        savedRoundPreprocessor = roundText.textPreprocessor;
        savedRemainingPreprocessor = remainingText.textPreprocessor;
        roundText.textPreprocessor = GraphiteReadout.Round;
        remainingText.textPreprocessor = GraphiteReadout.Remaining;
        graphiteTextApplied = true;
        roundText.ForceMeshUpdate(true, true);
        remainingText.ForceMeshUpdate(true, true);
    }

    void RestoreGraphiteText()
    {
        if (!graphiteTextApplied) return;
        roundText.textPreprocessor = savedRoundPreprocessor;
        remainingText.textPreprocessor = savedRemainingPreprocessor;
        roundText.ForceMeshUpdate(true, true);
        remainingText.ForceMeshUpdate(true, true);
        graphiteTextApplied = false;
    }

    // Format only the rendered readout; the game-owned TMP text remains intact.
    sealed class GraphiteReadout : ITextPreprocessor
    {
        public static readonly GraphiteReadout Round = new GraphiteReadout(false);
        public static readonly GraphiteReadout Remaining = new GraphiteReadout(true);
        readonly bool remaining;
        GraphiteReadout(bool value) => remaining = value;
        public string PreprocessText(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;
            if (remaining) return Regex.Replace(text, @"^余[:：]?\s*(\d+)$", m => "余" + m.Groups[1].Value.PadLeft(2, '0'));
            return Regex.Replace(text, @"^([东南西北])([一二三四五六七八九])局$",
                m => m.Groups[1].Value + ("一二三四五六七八九".IndexOf(m.Groups[2].Value) + 1) + "局");
        }
    }
}
