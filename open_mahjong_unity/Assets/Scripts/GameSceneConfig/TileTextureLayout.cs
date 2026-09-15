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
    // 雪枫透明画布已逐牌留出倒角余量；其他图片仍使用通用完整图片区。
    public const float SnowTableImageScale = .93f;
    public const string TableImageScalePngKey = "om-table-image-scale-v1";

    /// <summary>只在导入时读取 PNG 声明的排版；原始 PNG 字节随 ZIP/存档保留。</summary>
    public static float ReadTableImageScale(byte[] png) {
        if (png == null || png.Length < 33
            || png[0] != 137 || png[1] != 80 || png[2] != 78 || png[3] != 71
            || png[4] != 13 || png[5] != 10 || png[6] != 26 || png[7] != 10) return TableImageScale;
        for (int offset = 8; offset <= png.Length - 12;) {
            uint length = ReadPngUInt32(png, offset);
            if (length > (uint)(png.Length - offset - 12)) return TableImageScale;
            int size = (int)length;
            int data = offset + 8;
            bool text = png[offset + 4] == 't' && png[offset + 5] == 'E'
                && png[offset + 6] == 'X' && png[offset + 7] == 't';
            if (text && size > TableImageScalePngKey.Length
                && size <= TableImageScalePngKey.Length + 33) {
                bool matches = png[data + TableImageScalePngKey.Length] == 0;
                for (int i = 0; matches && i < TableImageScalePngKey.Length; i++) {
                    matches = png[data + i] == TableImageScalePngKey[i];
                }
                if (matches) {
                    if (PngChunkCrc(png, offset + 4, size + 4) != ReadPngUInt32(png, data + size))
                        return TableImageScale;
                    int valueOffset = data + TableImageScalePngKey.Length + 1;
                    string value = System.Text.Encoding.ASCII.GetString(png, valueOffset,
                        size - TableImageScalePngKey.Length - 1);
                    return float.TryParse(value, System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out float scale)
                        && !float.IsNaN(scale) && !float.IsInfinity(scale)
                        && scale >= TableImageScale && scale <= SnowTableImageScale
                        ? scale : TableImageScale;
                }
            }
            if (png[offset + 4] == 'I' && png[offset + 5] == 'E'
                && png[offset + 6] == 'N' && png[offset + 7] == 'D') break;
            offset += size + 12;
        }
        return TableImageScale;
    }

    private static uint ReadPngUInt32(byte[] data, int offset) {
        return ((uint)data[offset] << 24) | ((uint)data[offset + 1] << 16)
            | ((uint)data[offset + 2] << 8) | data[offset + 3];
    }

    private static uint PngChunkCrc(byte[] data, int offset, int count) {
        uint crc = 0xffffffffu;
        for (int end = offset + count; offset < end; offset++) {
            crc ^= data[offset];
            for (int bit = 0; bit < 8; bit++) {
                crc = (crc >> 1) ^ ((crc & 1u) != 0 ? 0xedb88320u : 0u);
            }
        }
        return crc ^ 0xffffffffu;
    }

    // Thumbnail outlines follow the actual authored mesh, not the texture's UV canvas.
    public static float GetRenderedCardAspect(GameObject model) {
        MeshFilter source = model != null ? model.GetComponentInChildren<MeshFilter>(true) : null;
        if (source == null || source.sharedMesh == null) return TableAspect;
        Vector3 size = source.sharedMesh.bounds.size;
        Matrix4x4 matrix = source.transform.localToWorldMatrix;
        Vector3 x = matrix.MultiplyVector(new Vector3(size.x, 0f, 0f));
        Vector3 y = matrix.MultiplyVector(new Vector3(0f, size.y, 0f));
        Vector3 z = matrix.MultiplyVector(new Vector3(0f, 0f, size.z));
        float width = Mathf.Abs(x.x) + Mathf.Abs(y.x) + Mathf.Abs(z.x);
        float height = Mathf.Abs(x.y) + Mathf.Abs(y.y) + Mathf.Abs(z.y);
        return width > 0f && height > 0f ? width / height : TableAspect;
    }

    public static void FitRenderedCardPreview(RectTransform frame, GameObject model) {
        Vector2 available = frame.rect.size;
        float aspect = GetRenderedCardAspect(model);
        float width = Mathf.Min(available.x, available.y * aspect);
        Vector2 fitted = new Vector2(width, width / aspect);
        frame.anchoredPosition += Vector2.Scale(fitted - available, frame.pivot - Vector2.one * .5f);
        frame.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, fitted.x);
        frame.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, fitted.y);
    }
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
