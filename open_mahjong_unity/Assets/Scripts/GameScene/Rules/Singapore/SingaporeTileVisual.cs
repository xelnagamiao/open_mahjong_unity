using UnityEngine;

/// <summary>原创文字动物牌面，手牌、桌面与牌谱使用同一组实体牌 ID。</summary>
public static class SingaporeTileVisual {
    public static bool IsAnimal(int tile) => tile>=61 && tile<=64;
    public static Sprite Sprite(int tile) => IsAnimal(tile) ? Resources.Load<Sprite>($"image/Cards/Faces/singapore/{tile}") : null;
    public static Texture2D Texture(int tile) => IsAnimal(tile) ? Resources.Load<Texture2D>($"image/Cards/Faces/singapore/{tile}") : null;
}
