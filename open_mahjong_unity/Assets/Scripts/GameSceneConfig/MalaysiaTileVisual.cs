using UnityEngine;

/// <summary>Physical animal, face and fly IDs; independent of user standard tile packs.</summary>
public static class MalaysiaTileVisual {
    public static readonly int[] Ids={61,62,63,64,71,72,73,74,81};
    public static bool Contains(int tile) => (tile>=61 && tile<=64) || (tile>=71 && tile<=74) || tile==81;
    public static Sprite Sprite(int tile) => Resources.Load<Sprite>("Malaysia/"+tile);
    public static Texture2D Texture(int tile) => Resources.Load<Texture2D>("Malaysia/"+tile);
}
