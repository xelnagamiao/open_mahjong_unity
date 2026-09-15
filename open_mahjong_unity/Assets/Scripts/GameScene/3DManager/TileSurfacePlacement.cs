using UnityEngine;

/// <summary>
/// Bounds 的平面支撑计算。矩阵保留完整旋转/缩放（包括父级非均匀缩放），不需要 Collider 或射线。
/// 轴对齐落点精确；倒角模型斜放时使用外包盒，短暂旋转过程中会略保守。
/// </summary>
public static class TileSurfacePlacement {
    public static float MinimumProjection(Bounds bounds, Matrix4x4 localToWorld, Vector3 normal) {
        Vector3 e = bounds.extents;
        float radius = Mathf.Abs(Vector3.Dot(normal, localToWorld.MultiplyVector(new Vector3(e.x, 0f, 0f))))
            + Mathf.Abs(Vector3.Dot(normal, localToWorld.MultiplyVector(new Vector3(0f, e.y, 0f))))
            + Mathf.Abs(Vector3.Dot(normal, localToWorld.MultiplyVector(new Vector3(0f, 0f, e.z))));
        return Vector3.Dot(normal, localToWorld.MultiplyPoint3x4(bounds.center)) - radius;
    }

    public static float MaximumProjection(Bounds bounds, Matrix4x4 localToWorld, Vector3 normal) {
        return -MinimumProjection(bounds, localToWorld, -normal);
    }

    /// <summary>只沿平面法线移动根节点；保留牌桌内的排布位置和可选堆叠表面。</summary>
    public static Vector3 PlaceOnPlane(Vector3 rootPosition, Bounds bounds, Matrix4x4 bodyToWorld,
        Vector3 planePoint, Vector3 planeNormal, float clearance = 0f) {
        Vector3 normal = planeNormal.normalized;
        float delta = Vector3.Dot(normal, planePoint) + clearance - MinimumProjection(bounds, bodyToWorld, normal);
        return rootPosition + normal * delta;
    }

    /// <summary>旅行中的牌只在接触平面下方时抬起，保留原有腾空行程。</summary>
    public static Vector3 KeepAbovePlane(Vector3 rootPosition, Bounds bounds, Matrix4x4 bodyToWorld,
        Vector3 planePoint, Vector3 planeNormal, float clearance = 0f) {
        Vector3 normal = planeNormal.normalized;
        float delta = Vector3.Dot(normal, planePoint) + clearance - MinimumProjection(bounds, bodyToWorld, normal);
        return rootPosition + normal * Mathf.Max(0f, delta);
    }

    /// <summary>桌布子网格的最薄轴作为表面法线；用逆转置兼容缩放后的桌面。</summary>
    public static void PlaneFromSurfaceBounds(Bounds bounds, Matrix4x4 localToWorld,
        out Vector3 point, out Vector3 normal) {
        Vector3 size = bounds.size;
        int axis = size.x <= size.y && size.x <= size.z ? 0 : size.y <= size.z ? 1 : 2;
        Vector3 localNormal = Vector3.zero;
        localNormal[axis] = 1f;
        normal = localToWorld.inverse.transpose.MultiplyVector(localNormal).normalized;
        if (Vector3.Dot(normal, Vector3.up) < 0f) {
            localNormal = -localNormal;
            normal = -normal;
        }
        point = localToWorld.MultiplyPoint3x4(bounds.center + localNormal * bounds.extents[axis]);
    }
}
