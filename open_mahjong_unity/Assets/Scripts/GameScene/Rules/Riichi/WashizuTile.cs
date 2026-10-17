using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;

/// <summary>Glass is a physical copy attribute; only calculations strip it.</summary>
public static class WashizuTile {
    public const string SubRule = "riichi/washizu";
    public const string RealtimeSubRule = "riichi/washizu_realtime";
    public const string DoublesSubRule = "riichi/washizu_doubles";
    public static bool IsWashizu(string subRule) => subRule == SubRule || subRule == RealtimeSubRule || subRule == DoublesSubRule;
    public static bool IsRealtimeSubRule(string subRule) => subRule == RealtimeSubRule || subRule == GuobiaoGameState.TransparentRealtimeSubRule;
    public static bool IsRealtimeHandOrder {
        get {
            var record = GameRecordManager.Instance;
            if (record != null && record.gameObject.activeSelf && record.gameRecord?.gameTitle != null)
                return record.gameRecord.gameTitle.TryGetValue("sub_rule", out object value) && IsRealtimeSubRule(value?.ToString());
            return IsRealtimeSubRule(GameSession.Current.SubRule);
        }
    }
    public const int Offset = 2000;
    public static bool UsesTransparentHands(string subRule) => IsWashizu(subRule) || GuobiaoGameState.IsTransparentSubRule(subRule);
    public static bool IsTransparent(int tile) => Riichi.WashizuTileEncoding.IsTransparent(tile);
    public static int Face(int tile) => Riichi.WashizuTileEncoding.Face(tile);
    public static bool SameCopyKind(int a, int b) => IsTransparent(a) == IsTransparent(b)
        && TileIdOrder.Normalize(a) == TileIdOrder.Normalize(b);

    /// <summary>
    /// Reveal the hidden faces without mixing the public glass row into the opaque
    /// group. The winning tile is excluded by the caller and keeps its own slot.
    /// A null projection is an ordinary or fully visible hand, using normal sorting.
    /// </summary>
    public static int[] OrderRevealedHand(IList<int> closedTiles, IList<int> publicHand) {
        var remaining = new List<int>(closedTiles);
        TileIdOrder.SortHand(remaining);
        if (publicHand == null) return remaining.ToArray();

        var ordered = new List<int>(remaining.Count);
        foreach (int tile in publicHand) {
            if (!IsTransparent(tile)) continue;
            int index = remaining.IndexOf(tile);
            if (index < 0) continue; // The snapshot may also contain the winning tile.
            ordered.Add(tile);
            remaining.RemoveAt(index);
        }
        // A just-drawn copy may not have reached the public snapshot yet. Keep it
        // with the glass group; opaque faces are first known when the hand opens.
        foreach (int tile in remaining) if (IsTransparent(tile)) ordered.Add(tile);
        foreach (int tile in remaining) if (!IsTransparent(tile)) ordered.Add(tile);
        return ordered.ToArray();
    }

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
