#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

/// <summary>Normalize the authored face once, before atlases, labels or effects use it.</summary>
public sealed class TileFaceUvImporter : AssetPostprocessor {
    public const string ModelPath = "Assets/Resources/3D/3DCardNew2026.8.5.fbx";
    public override uint GetVersion() => 1;

    private void OnPostprocessModel(GameObject model) {
        if (assetPath != ModelPath) return;
        foreach (var filter in model.GetComponentsInChildren<MeshFilter>(true)) {
            var mesh = filter.sharedMesh;
            var uv = mesh.uv;
            // The source FBX has sideways UV0. Keep geometry, normals, back UVs,
            // pivots and all existing tile poses; only normalize the front canvas.
            for (int i = 0; i < uv.Length; i++) uv[i] = new Vector2(uv[i].y, 1f - uv[i].x);
            mesh.uv = uv;
        }
    }
}
#endif
