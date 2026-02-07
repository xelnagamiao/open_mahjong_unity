using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(MeshFilter))]
public class OutlineNormalsCalculator : MonoBehaviour
{
    [Tooltip("Distance threshold to consider vertices as the same position")]
    [SerializeField] private float _cospatialVertexDistanceThreshold = 0.01f;

    [Tooltip("Whether to recalculate normals every time the script is enabled (useful for dynamic meshes)")]
    [SerializeField] private bool _recalculateOnEnable = false;

    [Tooltip("Skip recalculation if normals are already calculated (optimized for object pooling)")]
    [SerializeField] private bool _skipIfAlreadyCalculated = true;

    private Mesh _mesh;
    private Vector3[] _originalVertices;
    private Vector3[] _originalNormals;
    private bool _hasCalculated = false;

    private void Awake() {
        GetMesh();
        if (_mesh == null) return;

        if (_skipIfAlreadyCalculated && HasUV1Data()) {
            _hasCalculated = true;
            return;
        }

        _originalVertices = _mesh.vertices;
        _originalNormals = _mesh.normals;
        
        if (!HasUV1Data()) {
            CalculateAndApplySmoothNormals(true);
        } else {
            _hasCalculated = true;
        }
    }

    private void OnEnable() {
        if (_skipIfAlreadyCalculated && _hasCalculated) {
            return;
        }
        if (_recalculateOnEnable) {
            CalculateAndApplySmoothNormals();
            _hasCalculated = true;
        }
    }

    /// <summary>
    /// 检查 UV1 是否已经有数据
    /// </summary>
    private bool HasUV1Data() {
        if (_mesh == null) return false;
        var uv1 = new List<Vector4>();
        _mesh.GetUVs(1, uv1);
        if (uv1.Count > 0) {
            foreach (var uv in uv1) {
                if (uv.sqrMagnitude > 0.0001f) {
                    return true;
                }
            }
        }
        return false;
    }

    private void GetMesh() {
        var meshFilter = GetComponent<MeshFilter>();
        if (meshFilter == null) {
            Debug.LogError("OutlineNormalsCalculator requires a MeshFilter component.", this);
            return;
        }
        _mesh = meshFilter.sharedMesh;
    }

    /// <summary>
    /// 强制计算平滑法线
    /// </summary>
    public void CalculateAndApplySmoothNormals(bool force = false) {
        if (_mesh == null) {
            GetMesh();
            if (_mesh == null) return;
        }
        
        if (!force && _skipIfAlreadyCalculated && HasUV1Data()) {
            _hasCalculated = true;
            return;
        }
        
        if (!_mesh.name.Contains("(Clone)")) {
            var meshFilter = GetComponent<MeshFilter>();
            if (meshFilter != null && meshFilter.sharedMesh == _mesh) {
                _mesh = Instantiate(_mesh);
                _mesh.name = _mesh.name + " (Instance)";
                meshFilter.sharedMesh = _mesh;
            }
        }

        var triangles = _mesh.triangles;
        var vertices = _mesh.vertices;
        var normals = _mesh.normals;

        var vertexGroups = new Dictionary<Vector3, List<int>>();
        for (int i = 0; i < vertices.Length; i++) {
            var pos = vertices[i];
            bool foundGroup = false;
            foreach (var groupPos in vertexGroups.Keys) {
                if (Vector3.Distance(pos, groupPos) < _cospatialVertexDistanceThreshold) {
                    vertexGroups[groupPos].Add(i);
                    foundGroup = true;
                    break;
                }
            }
            if (!foundGroup) {
                vertexGroups[pos] = new List<int> { i };
            }
        }

        var smoothNormals = new Vector3[vertices.Length];
        for (int i = 0; i < triangles.Length; i += 3) {
            int idx0 = triangles[i];
            int idx1 = triangles[i + 1];
            int idx2 = triangles[i + 2];
            Vector3 v0 = vertices[idx0];
            Vector3 v1 = vertices[idx1];
            Vector3 v2 = vertices[idx2];
            Vector3 faceNormal = Vector3.Cross(v1 - v0, v2 - v0).normalized;
            float angle0 = Vector3.Angle(v1 - v0, v2 - v0);
            float angle1 = Vector3.Angle(v2 - v1, v0 - v1);
            float angle2 = Vector3.Angle(v0 - v2, v1 - v2);
            smoothNormals[idx0] += faceNormal * angle0;
            smoothNormals[idx1] += faceNormal * angle1;
            smoothNormals[idx2] += faceNormal * angle2;
        }

        for (int i = 0; i < smoothNormals.Length; i++) {
            if (smoothNormals[i].sqrMagnitude > 0.0001f) {
                smoothNormals[i] = smoothNormals[i].normalized;
            } else {
                smoothNormals[i] = normals[i];
            }
        }

        var finalSmoothNormals = new Vector3[vertices.Length];
        foreach (var group in vertexGroups.Values) {
            if (group.Count == 0) continue;
            Vector3 averageNormal = Vector3.zero;
            foreach (int idx in group) {
                averageNormal += smoothNormals[idx];
            }
            averageNormal /= group.Count;
            averageNormal.Normalize();
            foreach (int idx in group) {
                finalSmoothNormals[idx] = averageNormal;
            }
        }

        var uv1List = new List<Vector4>();
        for (int i = 0; i < finalSmoothNormals.Length; i++) {
            Vector3 n = finalSmoothNormals[i];
            uv1List.Add(new Vector4(n.x, n.y, n.z, 1));
        }
        _mesh.SetUVs(1, uv1List);
        _hasCalculated = true;
    }
    
    [ContextMenu("Calculate and Apply Smooth Normals")]
    private void CalculateAndApplySmoothNormalsContextMenu() {
        CalculateAndApplySmoothNormals(true);
    }

    [ContextMenu("Reset to Original Normals")]
    public void ResetToOriginalNormals() {
        if (_mesh == null || _originalNormals == null) return;
        var uv1List = new List<Vector4>();
        for (int i = 0; i < _originalNormals.Length; i++) {
            Vector3 n = _originalNormals[i];
            uv1List.Add(new Vector4(n.x, n.y, n.z, 1));
        }
        _mesh.SetUVs(1, uv1List);
    }
}