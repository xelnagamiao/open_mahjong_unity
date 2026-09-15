using UnityEngine;

public partial class Game3DManager {
    private void OnEnable() {
        TileContactShadow.RegisterSurface(gameObject.scene, this, TryGetTileContactSurface);
    }

    private bool TryGetTileContactSurface(out Vector3 point, out Vector3 normal, out float clearance) {
        GetTileSurface(out point, out normal);
        clearance = tileSurfaceClearance;
        return isActiveAndEnabled && (tileSurfacePlane != null || tableSurfaceMesh != null);
    }

    private void OnDestroy() {
        TileContactShadow.UnregisterSurface(gameObject.scene, this);
    }
}
