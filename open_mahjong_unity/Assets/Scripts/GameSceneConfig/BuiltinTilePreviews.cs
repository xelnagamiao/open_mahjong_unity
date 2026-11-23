using System.Collections.Generic;
using UnityEngine;

/// <summary>Small illustrations for menus and examples. Each gameplay pack has its own full-size atlas.</summary>
public static class BuiltinTilePreviews {
    private static readonly Dictionary<string, Sprite> Previews = new Dictionary<string, Sprite>();

    public static Sprite Load(string packId, int faceId) {
        if (Previews.Count == 0) {
            foreach (Sprite sprite in IosResourceCache.LoadAll<Sprite>("image/Cards/FacePreviews"))
                Previews[sprite.name] = sprite;
        }
        string key = packId == TilePackIds.PackOfficial ? faceId.ToString() : packId + "-" + faceId;
        return Previews.TryGetValue(key, out Sprite preview) ? preview : null;
    }
}
