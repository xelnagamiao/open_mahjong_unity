using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;

/// <summary>Glass is a physical copy attribute; only calculations strip it.</summary>
public static class WashizuTile {
    public const string SubRule = "riichi/washizu";
    public const int Offset = 2000;
    public static bool UsesTransparentHands(string subRule) => subRule == SubRule || subRule == GuobiaoGameState.TransparentSubRule;
    public static bool IsTransparent(int tile) => Riichi.WashizuTileEncoding.IsTransparent(tile);
    public static int Face(int tile) => Riichi.WashizuTileEncoding.Face(tile);
    public static bool SameCopyKind(int a, int b) => IsTransparent(a) == IsTransparent(b)
        && TileIdOrder.Normalize(a) == TileIdOrder.Normalize(b);

    // A concealed kong's glass copies identify all four faces, including its
    // hidden opaque copy. Counting that inference does not expose its artwork.
    public static IEnumerable<int> VisibleMeldTiles(int[] mask) {
        if (mask == null) yield break;
        bool concealed = mask.Length == 8;
        int knownFace = 0;
        for (int i = 0; i + 1 < mask.Length; i += 2) {
            concealed &= mask[i] == 2;
            if (IsTransparent(mask[i + 1])) knownFace = Face(mask[i + 1]);
        }
        for (int i = 1; i < mask.Length; i += 2) {
            int tile = mask[i] == 0 && concealed ? knownFace : mask[i];
            if (tile != 0) yield return tile;
        }
    }

    public static void ApplyHandVisual(Transform root, int tile, Image body, Image face, Image additionalBackground = null) {
        bool glass = IsTransparent(tile);
        var visual = root.GetComponent<WashizuHandVisual>();
        if (visual == null && glass) visual = root.gameObject.AddComponent<WashizuHandVisual>();
        if (visual != null) visual.Apply(glass, body, face);
        if (glass && additionalBackground != null && additionalBackground.enabled) {
            Color tint=additionalBackground.color; tint.a=0; additionalBackground.color=tint;
        }
    }
}
