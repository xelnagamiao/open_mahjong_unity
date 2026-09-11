using UnityEngine;

/// <summary>牌面上传建议与等比排版。尺寸是建议，不参与上传合法性判断。</summary>
public static class TileTextureLayout {
    public const int HandRecommendedWidth = 272;
    public const int HandRecommendedHeight = 389;
    public const int TableRecommendedWidth = 400;
    public const int TableRecommendedHeight = 532;
    public const float HandAspect = (float)HandRecommendedWidth / HandRecommendedHeight;
    // 当前模型 UV0 的轴在 _FrontRotation=270 后对应牌宽/高；一单位 UV 对应
    // 20.004 × 26.605 的物理范围。这里是完整纹理画布比例，不是中央平面的比例。
    public const float TableAspect = (float)TableRecommendedWidth / TableRecommendedHeight;
    // 完整图片放在平面内，避开模型 UV 的倒角裁边；Shader 与图库共用这一区域。
    public const float TableImageScale = .86f;
    public const string HandRecommendation = "宽高比 272:389，推荐 272×389 像素";
    public const string TableRecommendation = "宽高比 1:1.33，推荐 400×532 像素（或 600×798）";

    /// <summary>完整等比居中对应的采样缩放；大于 1 的轴由 Shader 以底色填补越界部分。</summary>
    public static Vector2 ContainUvScale(float sourceAspect, float targetAspect = TableAspect) {
        if (sourceAspect <= 0f || targetAspect <= 0f) return Vector2.one;
        return sourceAspect > targetAspect
            ? new Vector2(1f, sourceAspect / targetAspect)
            : new Vector2(targetAspect / sourceAspect, 1f);
    }

    /// <summary>FrontRotation=270 发生在 Tiling 之后，因此要交换纹理宽、高轴。</summary>
    public static Vector4 FrontContainTiling(Texture texture) {
        if (texture == null || texture.height <= 0) return new Vector4(1f, 1f, 0f, 0f);
        Vector2 scale = ContainUvScale((float)texture.width / texture.height);
        return new Vector4(scale.y, scale.x, (1f - scale.y) * .5f, (1f - scale.x) * .5f);
    }

    public static Vector2 FitTableCanvas(Vector2 available) {
        float width = Mathf.Min(available.x, available.y * TableAspect);
        return new Vector2(width, width / TableAspect);
    }
}
