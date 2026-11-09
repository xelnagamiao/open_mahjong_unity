using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.U2D;

/// <summary>Real tiles for the fixed design views and rotating preset thumbnails.</summary>
public sealed class CardDesignModelPreview : MonoBehaviour
{
    [SerializeField] GameObject tilePrefab;
    [SerializeField] RawImage[] views;
    [SerializeField] bool autoSpin;
    [SerializeField] int renderWidth = 512, renderHeight = 768;
    [SerializeField] float orthographicSize = 1.8f;
    GameObject rig;
    Camera previewCamera;
    Material material;
    Sprite officialFace;
    bool ownsOfficialFace;
    float nextRender;
    int selectedTile = 11;
    Transform[] pivots = new Transform[3];
    Quaternion[] orientations = { Quaternion.identity, Quaternion.identity, Quaternion.identity };
    RenderTexture[] textures = new RenderTexture[3];
    Card3DAppearance presetAppearance;
    bool useCurrentAppearance = true;
    float spinPhase;
    static int nextRig;
    static readonly float[] InitialYaw = { -18, 150, 64 };

    public void SetPresetPreview(Card3DAppearance appearance, bool current, float phase)
    {
        presetAppearance = appearance; useCurrentAppearance = current; spinPhase = phase;
    }

    public void SelectTile(int tile)
    {
        if (selectedTile != tile) {
            if (ownsOfficialFace) Dispose(officialFace);
            officialFace = null; ownsOfficialFace = false; selectedTile = tile;
        }
        RenderPreview();
    }
    public void RotateCard(int index, Vector2 delta)
    {
        if (index < 0 || index >= orientations.Length || delta.sqrMagnitude < .0001f) return;
        // Camera-space axes keep both drag directions consistent after any previous turn.
        var axis = new Vector3(delta.y, -delta.x, 0);
        orientations[index] = (Quaternion.AngleAxis(axis.magnitude * .45f, axis.normalized) * orientations[index]).normalized;
        if (pivots[index] != null) pivots[index].localRotation = orientations[index];
        RenderPreview();
    }
    void LateUpdate()
    {
        if (Time.unscaledTime < nextRender) return;
        nextRender = Time.unscaledTime + (autoSpin ? 1f / 24 : .15f); RenderPreview();
    }
    void Build()
    {
        if (rig != null) return;
        if (pivots.Length != views.Length)
        {
            pivots = new Transform[views.Length]; orientations = new Quaternion[views.Length]; textures = new RenderTexture[views.Length];
            for (int i = 0; i < orientations.Length; i++) orientations[i] = Quaternion.identity;
        }
        var source = tilePrefab.GetComponentInChildren<MeshFilter>(true);
        material = new Material(Resources.Load<Material>(CardBackManager.MaterialResourcePath)) { hideFlags = HideFlags.HideAndDontSave };
        rig = new GameObject("Card settings preview (temporary)") { hideFlags = HideFlags.HideAndDontSave };
        SceneManager.MoveGameObjectToScene(rig, gameObject.scene); rig.transform.position = new Vector3(0,10000 + nextRig++ * 32,0);
        for (int i = 0; i < views.Length; i++) {
            var pivot = new GameObject("Tile pivot " + i).transform; pivot.SetParent(rig.transform, false);
            pivot.localPosition = new Vector3(i * 20, 0, 0); pivots[i] = pivot;
            var model = new GameObject("Tile " + i, typeof(MeshFilter), typeof(MeshRenderer)); model.layer = 31;
            model.transform.SetParent(pivot, false); model.GetComponent<MeshFilter>().sharedMesh = source.sharedMesh;
            var renderer = model.GetComponent<MeshRenderer>(); renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off; renderer.receiveShadows = false;
            model.transform.localRotation = source.transform.rotation; model.transform.localScale = source.transform.lossyScale;
            model.transform.localScale *= 2.65f / renderer.bounds.size.y;
            model.transform.localRotation = Quaternion.Euler(0, InitialYaw[i % InitialYaw.Length], 0) * Quaternion.Euler(0,0,180) * source.transform.rotation;
            model.transform.position += pivot.position - renderer.bounds.center;
            pivot.localRotation = orientations[i];
            textures[i] = new RenderTexture(renderWidth, renderHeight, 24, RenderTextureFormat.ARGB32) {
                hideFlags = HideFlags.HideAndDontSave, antiAliasing = 4, filterMode = FilterMode.Bilinear };
            textures[i].Create(); views[i].texture = textures[i];
        }
        var cameraObject = new GameObject("Preview camera", typeof(Camera)); cameraObject.transform.SetParent(rig.transform, false);
        previewCamera = cameraObject.GetComponent<Camera>(); previewCamera.enabled = false; previewCamera.orthographic = true;
        previewCamera.cullingMask = 1 << 31; previewCamera.orthographicSize = orthographicSize; previewCamera.aspect = (float)renderWidth / renderHeight;
        previewCamera.clearFlags = CameraClearFlags.SolidColor; previewCamera.backgroundColor = Color.clear;
        previewCamera.nearClipPlane = .1f; previewCamera.farClipPlane = 24;
#if UNITY_EDITOR
        previewCamera.overrideSceneCullingMask = UnityEditor.SceneManagement.EditorSceneManager.GetSceneCullingMask(gameObject.scene);
#endif
    }
    public void RenderPreview()
    {
        if (!gameObject.activeInHierarchy || RenderPipelineManager.currentPipeline == null || views == null || views.Length == 0) return;
        if (autoSpin && (!views[0].isActiveAndEnabled || views[0].canvasRenderer.cull)) return;
        Build(); SyncMaterial();
        for (int i = 0; i < views.Length; i++) {
            if (autoSpin) pivots[i].localRotation = Quaternion.Euler(10, spinPhase + Time.unscaledTime * 22, 0);
            previewCamera.transform.localPosition = new Vector3(i * 20, 0, -12);
            RenderPipeline.SubmitRenderRequest(previewCamera, new UniversalRenderPipeline.SingleCameraRequest { destination = textures[i] });
        }
    }
    void SyncMaterial()
    {
        CardBackManager.SyncSharedVisualsToMaterial(material);
        if (presetAppearance != null && !useCurrentAppearance) Card3DPresetPreviewMaterial.Apply(material, presetAppearance);
        // This independent rig uses world axes, even when the gameplay table rotates.
        material.SetFloat("_TileLightUseTableFrame", 0f);
        // Match MahjongObjectPool's 3D texture path, not the flat gallery sprite.
        var faceTexture = TileFaceResolver.LoadTableTexture(selectedTile);
        var sharedSprite = TileFaceResolver.LoadSharedTableSprite(selectedTile);
        if (sharedSprite != null && sharedSprite.texture == faceTexture) {
            material.SetFloat("_FrontTexContain",0f);
            material.SetTexture("_FrontTex",faceTexture);
            material.SetVector("_FrontTilingOffset",Tile3D.ComputeSpriteTiling(sharedSprite));
        } else if (faceTexture != null) {
            material.SetFloat("_FrontTexContain",1f);
            material.SetTexture("_FrontTex",faceTexture);
            material.SetVector("_FrontTilingOffset",TileTextureLayout.FrontContainTiling(faceTexture));
        } else {
            material.SetFloat("_FrontTexContain",0f);
            if (officialFace == null) {
                var atlas = Resources.Load<SpriteAtlas>(TilePackIds.OfficialAtlasResource);
                officialFace = atlas != null ? atlas.GetSprite(selectedTile.ToString()) : null;
                ownsOfficialFace = officialFace != null;
                if (officialFace == null) officialFace = BuiltinTileSprites.OfficialTable(selectedTile);
            }
            var sprite = officialFace;
            if (sprite != null) {
                material.SetTexture("_FrontTex",sprite.texture);
                material.SetVector("_FrontTilingOffset",Tile3D.ComputeSpriteTiling(sprite));
            }
        }
        material.SetColor("_FrontColor",Color.white);
        material.SetColor("_TableFaceFallbackColor",GameSettings.Current.DefaultTableFaceFallbackColor);
        material.SetFloat("_TableFaceFallbackEnabled",1f);
        // Preserve the mesh/material's authored rotation, also used in gameplay.
        material.SetVector("_TileInstanceParams",new Vector4(0f,TileFaceResolver.TableImageScaleFor(selectedTile),0f,0f));
        var layout = TableFaceLayoutSettings.ForTile(selectedTile);
        var baseline = faceTexture != null ? LegacyFacePlacement.Table(selectedTile) : LegacyFacePlacement.Placement.Identity;
        if (sharedSprite != null && sharedSprite.texture == faceTexture)
            baseline.scale = Vector2.Scale(baseline.scale, Tile3D.ComputeSpriteCanvasFit(sharedSprite));
        material.SetVector("_ArtworkLayout", new Vector4(layout.x, layout.y, layout.scale, 0));
        material.SetVector("_ArtworkBaseline", new Vector4(baseline.offset.x, baseline.offset.y, baseline.scale.x, baseline.scale.y));
    }
    void OnDisable() => Release();
    void OnDestroy() => Release();
    void Release()
    {
        for (int i = 0; i < textures.Length; i++) {
            if (views != null && i < views.Length && views[i] != null) views[i].texture = null;
            if (textures[i] != null) textures[i].Release(); Dispose(textures[i]); textures[i] = null;
        }
        Dispose(rig); Dispose(material); if (ownsOfficialFace) Dispose(officialFace);
        officialFace = null; ownsOfficialFace = false; rig = null; material = null; previewCamera = null;
    }
    static void Dispose(Object obj) { if (obj == null) return; if (Application.isPlaying) Destroy(obj); else DestroyImmediate(obj); }
}
