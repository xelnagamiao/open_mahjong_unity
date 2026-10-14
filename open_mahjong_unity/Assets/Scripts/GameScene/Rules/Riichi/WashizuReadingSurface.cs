using UnityEngine;
using UnityEngine.Rendering;

/// <summary>A pooled public reading aid, not an additional physical tile.</summary>
public sealed class WashizuReadingSurface : MonoBehaviour {
    private MeshFilter meshFilter;
    private Mesh originalMesh;
    private Renderer tileRenderer;
    private ShadowCastingMode originalShadows;
    private bool originalReceiveShadows;
    private Tile3D source;
    private uint sourceLease;
    private Transform sourceHand;
    private Vector3 tableOffset;
    public bool IsReadingSurface { get; private set; }

    public void Follow(Tile3D tile, Transform hand, Vector3 offset) {
        source=tile;
        sourceLease=tile.PoolLeaseVersion;
        sourceHand=hand;
        tableOffset=offset;
    }

    public bool FollowOnTable(Vector3 point, Vector3 normal, float clearance) {
        if (!IsReadingSurface || source == null || !source.gameObject.activeInHierarchy
            || source.PoolLeaseVersion != sourceLease || source.transform.parent != sourceHand) return false;
        Vector3 position=source.transform.position+tableOffset;
        transform.position=position+normal*(clearance-Vector3.Dot(position-point,normal));
        return true;
    }

    public void Show(ref Mesh sharedQuad) {
        if (IsReadingSurface) return;
        meshFilter = GetComponentInChildren<MeshFilter>();
        tileRenderer = meshFilter.GetComponent<Renderer>();
        originalMesh = meshFilter.sharedMesh;
        originalShadows = tileRenderer.shadowCastingMode;
        originalReceiveShadows = tileRenderer.receiveShadows;
        if (sharedQuad == null) {
            // Match the authored tile face axes and UVs. The normal is local -Z;
            // RiverTileWorldRotation turns it onto the current table surface.
            Vector3 e = originalMesh.bounds.extents;
            sharedQuad = new Mesh { name = "Washizu tabletop face" };
            sharedQuad.vertices = new[] { new Vector3(-e.x,-e.y,0), new Vector3(e.x,-e.y,0),
                new Vector3(e.x,e.y,0), new Vector3(-e.x,e.y,0) };
            sharedQuad.uv = new[] { new Vector2(0,1),new Vector2(0,0),new Vector2(1,0),new Vector2(1,1) };
            sharedQuad.normals = new[] { Vector3.back,Vector3.back,Vector3.back,Vector3.back };
            sharedQuad.colors = new[] { Color.red,Color.red,Color.red,Color.red };
            sharedQuad.triangles = new[] { 0,2,1,0,3,2 };
            sharedQuad.RecalculateBounds();
        }
        meshFilter.sharedMesh = sharedQuad;
        tileRenderer.shadowCastingMode = ShadowCastingMode.Off;
        tileRenderer.receiveShadows = false;
        IsReadingSurface = true;
    }

    public void Restore() {
        source=null;
        sourceHand=null;
        if (!IsReadingSurface) return;
        if (meshFilter != null) meshFilter.sharedMesh = originalMesh;
        if (tileRenderer != null) {
            tileRenderer.shadowCastingMode = originalShadows;
            tileRenderer.receiveShadows = originalReceiveShadows;
        }
        IsReadingSurface = false;
    }
}
