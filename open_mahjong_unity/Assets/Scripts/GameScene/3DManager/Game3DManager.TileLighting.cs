using UnityEngine;

public partial class Game3DManager
{
    private static readonly int TileLightingFrameId = Shader.PropertyToID("_TileLightingTableToWorld");
    private Quaternion lastTileLightingRotation;
    private bool hasTileLightingRotation;

    private void ResetTileLightingFrame()
    {
        if (Instance != this) return;
        hasTileLightingRotation = false;
        Shader.SetGlobalMatrix(TileLightingFrameId, Matrix4x4.identity);
    }

    private void RefreshTileLightingFrame()
    {
        if (Instance != this) return;
        // The layout Canvas uses XY on the tabletop: local +Y faces the far seat,
        // local -Z is above the table. Convert once for all tiles, ignoring scale.
        Quaternion rotation = transform.rotation * Quaternion.Euler(-90f, 0f, 0f);
        if (hasTileLightingRotation && rotation == lastTileLightingRotation) return;
        lastTileLightingRotation = rotation;
        hasTileLightingRotation = true;
        Shader.SetGlobalMatrix(TileLightingFrameId, Matrix4x4.Rotate(rotation));
    }
}
