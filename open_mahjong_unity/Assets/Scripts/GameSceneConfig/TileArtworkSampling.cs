using UnityEngine;

/// <summary>UI 使用原始纹理采样；同一张源纹理的 mipmap 留给倾斜的 3D 牌面。</summary>
public static class TileArtworkSampling
{
    static Material material;
    public static void Apply(UnityEngine.UI.Image image) {
        if (image == null) return;
        if (material == null) material = Resources.Load<Material>("Materials/Tiles/HandArtwork");
        if (material != null && image.material != material) image.material = material;
    }
}
