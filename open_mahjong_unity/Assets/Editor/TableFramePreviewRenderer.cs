using System;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

/// <summary>Editor-only thumbnails of the actual frame; no Board source image is needed.</summary>
internal static class TableFramePreviewRenderer
{
    const string Model = "Assets/Resources/TableFrame/Model/TableFrame_Upright_V8.fbx";
    const string MaterialPath = "Assets/TableFrame/SceneAssets/Frame_OrangeWood_V8.mat";
    internal static string Fingerprint(string style, string source)
    {
        return "frame-model-v1:" + AssetDatabase.GetAssetDependencyHash(Model) + ":" +
            AssetDatabase.GetAssetDependencyHash(MaterialPath) + ":" +
            (source == null ? ColorUtility.ToHtmlStringRGB(TableFrameStyles.SolidColor(style)) : AssetDatabase.GetAssetDependencyHash(source).ToString()) + ":" + style;
    }
    internal static byte[] Render(string style, string source, int size)
    {
        if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
            throw new InvalidOperationException("Frame thumbnails require an Editor graphics device.");
        var mesh = AssetDatabase.LoadAllAssetsAtPath(Model).OfType<Mesh>().FirstOrDefault();
        var authored = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
        if (mesh == null || authored == null || authored.shader == null || !authored.shader.isSupported)
            throw new InvalidOperationException("Frame thumbnail model/material is unavailable.");
        var scene = EditorSceneManager.NewPreviewScene();
        Material material = null;
        RenderTexture target = null;
        Texture2D pixels = null;
        var previous = RenderTexture.active;
        try
        {
            material = new Material(authored) { hideFlags = HideFlags.HideAndDontSave };
            material.mainTexture = source == null ? Texture2D.whiteTexture : AssetDatabase.LoadAssetAtPath<Texture2D>(source);
            if (material.mainTexture == null) throw new InvalidOperationException("Missing frame base: " + source);
            material.SetFloat("_UseSolidColor", source == null ? 1 : 0);
            material.SetVector("_SolidColor", TableFrameStyles.SolidColor(style));
            material.SetVector("_BaseTone", TableFrameStyles.BaseTone(style));
            var model = new GameObject("Frame Preview", typeof(MeshFilter), typeof(MeshRenderer));
            SceneManager.MoveGameObjectToScene(model, scene);
            model.GetComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = model.GetComponent<MeshRenderer>(); renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            var cameraObject = new GameObject("Frame Preview Camera", typeof(Camera));
            SceneManager.MoveGameObjectToScene(cameraObject, scene);
            var camera = cameraObject.GetComponent<Camera>();
            var bounds = renderer.bounds;
            float extent = Mathf.Max(bounds.extents.x, bounds.extents.z);
            camera.orthographic = true; camera.orthographicSize = extent * 1.06f; camera.aspect = 1;
            camera.transform.position = bounds.center + Vector3.up * (extent * 3 + 1);
            camera.transform.LookAt(bounds.center, Vector3.forward);
            camera.nearClipPlane = .01f; camera.farClipPlane = extent * 6 + 10;
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = Color.clear;
            camera.allowHDR = false;
            camera.overrideSceneCullingMask = EditorSceneManager.GetSceneCullingMask(scene);
            cameraObject.AddComponent<UniversalAdditionalCameraData>().renderPostProcessing = false;
            target = new RenderTexture(size, size, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            target.Create(); camera.targetTexture = target;
            RenderPipeline.SubmitRenderRequest(camera, new UniversalRenderPipeline.SingleCameraRequest { destination = target });
            RenderTexture.active = target;
            pixels = new Texture2D(size, size, TextureFormat.RGBA32, false);
            pixels.ReadPixels(new Rect(0, 0, size, size), 0, 0); pixels.Apply();
            return pixels.EncodeToPNG();
        }
        finally
        {
            RenderTexture.active = previous;
            EditorSceneManager.ClosePreviewScene(scene);
            if (target != null) { target.Release(); UnityEngine.Object.DestroyImmediate(target); }
            if (pixels != null) UnityEngine.Object.DestroyImmediate(pixels);
            if (material != null) UnityEngine.Object.DestroyImmediate(material);
        }
    }
}
