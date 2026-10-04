using UnityEngine;
using UnityEngine.Rendering;

/// <summary>自由模式出牌区域的场景描边，独立于 UI，不参与射线或牌张计数。</summary>
public sealed class FreeModePlacementPreview : MonoBehaviour {
    private static readonly int ColorId = Shader.PropertyToID("_Color");
    private static readonly int RegionSizeId = Shader.PropertyToID("_RegionSize");
    private static readonly Color RiverColor = new Color(1f, 0.72f, 0.22f, 1f);
    private static readonly Color FlowerColor = new Color(0.25f, 0.95f, 0.83f, 1f);
    private static readonly Color TransferColor = new Color(0.45f, 0.72f, 1f, 1f);
    private FreeGameState state;
    private GameObject marker;
    private Mesh mesh;
    private Material material;
    private bool missingShaderLogged;

    public void Bind(FreeGameState owner) {
        state = owner;
        LateUpdate();
    }

    private void LateUpdate() {
        Game3DManager table = Game3DManager.Instance;
        if (state == null || !state.IsActive || FreeGameState.Active != state
            || GameSession.Current.IsRealtimeSpectator || GameHost.Current.CurrentWindow != "game" || table == null) {
            Hide();
            return;
        }
        bool transfer = state.DiscardDest == FreeDiscardDest.Transfer;
        Vector3 position;
        Quaternion rotation;
        Vector2 size;
        bool hasPlacement = transfer
            ? table.TryGetTransferPlacementPreview(out position, out rotation, out size)
            : table.TryGetSelfPlacementPreview(state.DiscardDest == FreeDiscardDest.Flower, out position, out rotation, out size);
        if (!hasPlacement) {
            Hide();
            return;
        }
        if (!EnsureMarker(table)) return;
        marker.transform.SetPositionAndRotation(position, rotation);
        marker.transform.localScale = new Vector3(size.x, size.y, 1f);
        material.SetColor(ColorId, transfer ? TransferColor : state.DiscardDest == FreeDiscardDest.Flower ? FlowerColor : RiverColor);
        material.SetVector(RegionSizeId, new Vector4(size.x, size.y, 0f, 0f));
        marker.SetActive(true);
    }

    private bool EnsureMarker(Game3DManager table) {
        if (marker != null) return true;
        Shader shader = Resources.Load<Shader>("Shaders/FreeModePlacementPreview");
        if (shader == null) {
            if (!missingShaderLogged) Debug.LogError("自由模式区域描边着色器缺失：Shaders/FreeModePlacementPreview", this);
            missingShaderLogged = true;
            return false;
        }
        material = new Material(shader) { name = "FreeModePlacementPreview (Instance)" };
        mesh = new Mesh {
            name = "FreeModePlacementPreview",
            vertices = new[] { new Vector3(-0.5f, -0.5f), new Vector3(-0.5f, 0.5f),
                new Vector3(0.5f, 0.5f), new Vector3(0.5f, -0.5f) },
            uv = new[] { Vector2.zero, Vector2.up, Vector2.one, Vector2.right },
            triangles = new[] { 0, 1, 2, 0, 2, 3 },
        };
        mesh.RecalculateBounds();
        marker = new GameObject("FreeModePlacementPreview", typeof(MeshFilter), typeof(MeshRenderer));
        // 不挂进河牌/补花容器，也不继承屏幕 UI 的缩放；随 HUD 的 OnDisable/OnDestroy 清理。
        marker.layer = table.gameObject.layer;
        UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(marker, table.gameObject.scene);
        marker.GetComponent<MeshFilter>().sharedMesh = mesh;
        MeshRenderer renderer = marker.GetComponent<MeshRenderer>();
        renderer.sharedMaterial = material;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        renderer.lightProbeUsage = LightProbeUsage.Off;
        renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
        // 使用普通场景排序；材质在所有 Canvas UI 之前绘制，不能借用 BoardUI 的置顶层。
        renderer.sortingLayerID = 0;
        renderer.sortingOrder = 0;
        return true;
    }

    private void Hide() {
        if (marker != null) marker.SetActive(false);
    }

    private void OnDisable() => Hide();

    private void OnDestroy() {
        if (marker != null) Destroy(marker);
        if (mesh != null) Destroy(mesh);
        if (material != null) Destroy(material);
    }
}
