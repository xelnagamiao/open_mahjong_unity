using UnityEngine;

/// <summary>The imported tile's front canvas: right = -X, up = -Y, surface = -Z.
/// Attach every physical overlay here; seat, meld and river rotations belong to the tile.</summary>
public static class TileFaceFrame {
    public static Quaternion Rotation(bool halfTurn = false) => Quaternion.Euler(0, 0, halfTurn ? 0 : 180);

    public static Vector3 Center(Bounds bounds) => new Vector3(bounds.center.x, bounds.center.y,
        bounds.min.z - Mathf.Max(.00001f, bounds.size.z * .004f));

    public static Vector3 Point(Bounds bounds, Vector2 offset, bool halfTurn = false) =>
        Center(bounds) + Rotation(halfTurn) * new Vector3(offset.x, offset.y, 0);
}
