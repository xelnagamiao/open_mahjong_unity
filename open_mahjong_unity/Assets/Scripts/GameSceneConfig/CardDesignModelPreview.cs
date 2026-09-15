using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.U2D;

/// <summary>用主项目模型与材质副本显示聚合页预览，不创建 Tile3D 或修改共享材质。</summary>
public sealed class CardDesignModelPreview : MonoBehaviour
{
    [SerializeField] GameObject tilePrefab;
    GameObject rig;
    Camera previewCamera;
    RawImage picture;
    RenderTexture texture;
    Material material;
    Sprite officialFace;
    bool ownsOfficialFace;
    float nextRender;

    void LateUpdate()
    {
        if (Time.unscaledTime < nextRender) return;
        nextRender = Time.unscaledTime + .15f;
        RenderPreview();
    }

    void Build()
    {
        if (rig != null) return;
        var source = tilePrefab.GetComponentInChildren<MeshFilter>(true);
        material = new Material(Resources.Load<Material>(CardBackManager.MaterialResourcePath));
        material.hideFlags = HideFlags.HideAndDontSave;
        texture = new RenderTexture(1020,382,24,RenderTextureFormat.ARGB32) { hideFlags = HideFlags.HideAndDontSave };
        texture.Create();
        picture = GetComponent<RawImage>();
        if (picture == null) picture = gameObject.AddComponent<RawImage>();
        picture.raycastTarget = false; picture.texture = texture;
        rig = new GameObject("Card settings preview (temporary)") { hideFlags = HideFlags.HideAndDontSave };
        SceneManager.MoveGameObjectToScene(rig,gameObject.scene);
        // Outside the gameplay cameras' range; no global layer or camera changes.
        rig.transform.position = new Vector3(0,10000,0);
        for (int i=0;i<3;i++) {
            var model = new GameObject("Tile "+i,typeof(MeshFilter),typeof(MeshRenderer));
            model.layer = 31;
            model.transform.SetParent(rig.transform,false);
            model.GetComponent<MeshFilter>().sharedMesh = source.sharedMesh;
            var renderer = model.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off; renderer.receiveShadows = false;
            model.transform.localRotation = source.transform.rotation;
            model.transform.localScale = source.transform.lossyScale;
            float scale = 2.65f / renderer.bounds.size.y;
            model.transform.localScale *= scale;
            // Present the authored tile upright to this camera by rotating the model,
            // leaving the gameplay texture rotation/crop unchanged.
            model.transform.localRotation = Quaternion.Euler(0,new[]{-16,164,70}[i],0)
                * Quaternion.Euler(0,0,180) * source.transform.rotation;
            Vector3 target = rig.transform.position + new Vector3((i-1)*2.25f,0,0);
            model.transform.position += target-renderer.bounds.center;
        }
        var cameraObject = new GameObject("Preview camera",typeof(Camera));
        cameraObject.transform.SetParent(rig.transform,false);
        cameraObject.transform.localPosition = new Vector3(0,0,-12);
        previewCamera = cameraObject.GetComponent<Camera>();
        previewCamera.enabled = false; previewCamera.orthographic = true;
        previewCamera.cullingMask = 1 << 31;
        previewCamera.orthographicSize = 1.8f; previewCamera.aspect = 1020f/382;
        previewCamera.clearFlags = CameraClearFlags.SolidColor;
        previewCamera.backgroundColor = new Color(.1f,.11f,.15f,1);
        previewCamera.nearClipPlane = .1f; previewCamera.farClipPlane = 24;
#if UNITY_EDITOR
        previewCamera.overrideSceneCullingMask = UnityEditor.SceneManagement.EditorSceneManager.GetSceneCullingMask(gameObject.scene);
#endif
    }

    public void RenderPreview()
    {
        if (!gameObject.activeInHierarchy || RenderPipelineManager.currentPipeline == null) return;
        Build();
        CardBackManager.SyncSharedVisualsToMaterial(material);
        // Match MahjongObjectPool's 3D texture path, not the flat gallery sprite.
        var faceTexture = TileFaceResolver.LoadTableTexture(11);
        if (faceTexture != null) {
            material.SetFloat("_FrontTexContain",1f);
            material.SetTexture("_FrontTex",faceTexture);
            material.SetVector("_FrontTilingOffset",TileTextureLayout.FrontContainTiling(faceTexture));
        } else {
            material.SetFloat("_FrontTexContain",0f);
            if (officialFace == null) {
                var atlas = Resources.Load<SpriteAtlas>(TilePackIds.OfficialAtlasResource);
                officialFace = atlas != null ? atlas.GetSprite("11") : null;
                ownsOfficialFace = officialFace != null;
                if (officialFace == null) officialFace = Resources.Load<Sprite>(TilePackIds.BuiltinTableResource(TilePackIds.PackOfficial, 11));
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
        material.SetVector("_TileInstanceParams",new Vector4(0f,TileFaceResolver.TableImageScaleFor(11),0f,0f));
        RenderPipeline.SubmitRenderRequest(previewCamera,new UniversalRenderPipeline.SingleCameraRequest { destination=texture });
    }

    void OnDisable() { Release(); }
    void OnDestroy() { Release(); }
    void Release()
    {
        if (picture != null) picture.texture = null;
        if (texture != null) texture.Release();
        Dispose(rig); Dispose(material); Dispose(texture);
        if (ownsOfficialFace) Dispose(officialFace);
        officialFace = null; ownsOfficialFace = false;
        rig = null; material = null; texture = null; previewCamera = null;
    }
    static void Dispose(Object obj)
    {
        if (obj == null) return;
        if (Application.isPlaying) Destroy(obj); else DestroyImmediate(obj);
    }
}
