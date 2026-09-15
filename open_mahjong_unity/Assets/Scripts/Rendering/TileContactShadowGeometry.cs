using UnityEngine;

/// <summary>把牌体包围盒垂直投到桌面；保留完整父级缩放，不需要 Collider 或射线。</summary>
public static class TileContactShadowGeometry {
    public readonly struct Projection {
        public readonly Vector3 Center;
        public readonly Vector3 Normal;
        public readonly Vector3 Tangent;
        public readonly Vector3 Bitangent;
        public readonly Vector2 HalfSize;
        // 最低点到桌面的有符号距离。牌穿入桌面时为负，渲染方按需限制到零。
        public readonly float Height;
        // 模型 X 轴完整边长，用于按牌宽设置扩散和淡出，不随翻牌姿态改变。
        public readonly float Width;

        public Projection(Vector3 center, Vector3 normal, Vector3 tangent, Vector3 bitangent,
            Vector2 halfSize, float height, float width) {
            Center = center;
            Normal = normal;
            Tangent = tangent;
            Bitangent = bitangent;
            HalfSize = halfSize;
            Height = height;
            Width = width;
        }
    }

    public static bool TryProject(Bounds bounds, Matrix4x4 bodyToWorld,
        Vector3 planePoint, Vector3 planeNormal, out Projection result) {
        result = default;
        Vector3 extent = bounds.extents;
        if (!Finite(extent) || !Finite(bounds.center) || !Finite(planePoint) || !Finite(planeNormal)
            || extent.x <= 0f || extent.y < 0f || extent.z < 0f) return false;
        float normalLength = planeNormal.magnitude;
        if (!Finite(normalLength) || normalLength <= 0.000001f) return false;
        Vector3 normal = planeNormal / normalLength;
        Vector3 xAxis = bodyToWorld.MultiplyVector(new Vector3(extent.x * 2f, 0f, 0f));
        float width = xAxis.magnitude;
        if (!Finite(width) || width <= 0.000001f) return false;

        Vector3 tangent = OnPlane(xAxis, normal);
        if (tangent.sqrMagnitude <= width * width * 0.00000001f) {
            tangent = OnPlane(bodyToWorld.MultiplyVector(Vector3.up), normal);
            if (tangent.sqrMagnitude <= 0.000000000001f)
                tangent = OnPlane(bodyToWorld.MultiplyVector(Vector3.forward), normal);
            if (tangent.sqrMagnitude <= 0.000000000001f)
                tangent = Vector3.Cross(Mathf.Abs(normal.y) < 0.9f ? Vector3.up : Vector3.right, normal);
        }
        if (!Finite(tangent)) return false;
        tangent.Normalize();
        Vector3 bitangent = Vector3.Cross(tangent, normal);
        Vector3 bodyCenter = bodyToWorld.MultiplyPoint3x4(bounds.center);
        if (!Finite(bodyCenter)) return false;
        Vector3 origin = bodyCenter - normal * Vector3.Dot(bodyCenter - planePoint, normal);
        Vector2 min = new Vector2(float.PositiveInfinity, float.PositiveInfinity);
        Vector2 max = new Vector2(float.NegativeInfinity, float.NegativeInfinity);
        float height = float.PositiveInfinity;
        for (int i = 0; i < 8; i++) {
            Vector3 corner = bounds.center + new Vector3(
                (i & 1) == 0 ? -extent.x : extent.x,
                (i & 2) == 0 ? -extent.y : extent.y,
                (i & 4) == 0 ? -extent.z : extent.z);
            Vector3 relative = bodyToWorld.MultiplyPoint3x4(corner) - origin;
            if (!Finite(relative)) return false;
            float x = Vector3.Dot(relative, tangent);
            float y = Vector3.Dot(relative, bitangent);
            min.x = Mathf.Min(min.x, x);
            min.y = Mathf.Min(min.y, y);
            max.x = Mathf.Max(max.x, x);
            max.y = Mathf.Max(max.y, y);
            height = Mathf.Min(height, Vector3.Dot(relative, normal));
        }
        Vector2 halfSize = (max - min) * 0.5f;
        if (!Finite(halfSize.x) || !Finite(halfSize.y) || !Finite(height)
            || halfSize.x <= 0.000001f || halfSize.y <= 0.000001f) return false;
        Vector2 midpoint = (min + max) * 0.5f;
        Vector3 center = origin + tangent * midpoint.x + bitangent * midpoint.y;
        result = new Projection(center, normal, tangent, bitangent, halfSize, height, width);
        return true;
    }

    private static Vector3 OnPlane(Vector3 vector, Vector3 normal) => vector - normal * Vector3.Dot(vector, normal);
    private static bool Finite(Vector3 value) => Finite(value.x) && Finite(value.y) && Finite(value.z);
    private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
}
