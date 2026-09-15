using System;
using UnityEditor;
using UnityEngine;

/// <summary>Import settings for the production thick frame and its separate material layers.</summary>
public sealed class TableFrameAssetImporter : AssetPostprocessor
{
    private const string Root = "Assets/Resources/TableFrame/";
    private const string SourceTable = "Assets/Resources/Materials/Board/DeskTopNew.fbx";

    private void OnPreprocessModel()
    {
        var importer = (ModelImporter)assetImporter;
        if (assetPath == SourceTable)
        {
            // Keep the source readable for authoring the separate cloth mesh.
            // Runtime rendering uses the mesh assets already assigned in the scene.
            importer.isReadable = true;
            return;
        }
        if (assetPath != Root + "Model/TableFrame_Upright_V8.fbx") return;
        importer.globalScale = 1;
        importer.useFileScale = false;
        importer.bakeAxisConversion = true;
        importer.isReadable = true;
        importer.meshCompression = ModelImporterMeshCompression.Off;
        importer.importNormals = ModelImporterNormals.Import;
        importer.importTangents = ModelImporterTangents.CalculateMikk;
        importer.weldVertices = false;
        importer.optimizeMeshPolygons = false;
        importer.optimizeMeshVertices = false;
        importer.generateSecondaryUV = false;
        importer.importBlendShapes = false;
        importer.importCameras = false;
        importer.importLights = false;
        importer.importAnimation = false;
        importer.materialImportMode = ModelImporterMaterialImportMode.None;
    }

    private void OnPreprocessTexture()
    {
        if (TableSurfaceCompressionPolicy.IsTarget(assetPath))
        {
            TableSurfaceCompressionPolicy.Configure((TextureImporter)assetImporter);
            return;
        }
        if (!assetPath.EndsWith(".png", StringComparison.Ordinal)) return;
        bool baseLayer = assetPath.StartsWith(Root + "Textures/", StringComparison.Ordinal);
        bool overlay = assetPath.StartsWith(Root + "Lighting/", StringComparison.Ordinal)
            || assetPath.StartsWith(Root + "Lines/", StringComparison.Ordinal);
        if (!baseLayer && !overlay) return;
        var importer = (TextureImporter)assetImporter;
        importer.textureType = TextureImporterType.Default;
        importer.sRGBTexture = true;
        importer.alphaSource = overlay ? TextureImporterAlphaSource.FromInput : TextureImporterAlphaSource.None;
        importer.alphaIsTransparency = false;
        importer.isReadable = false;
        importer.mipmapEnabled = true;
        importer.mipMapsPreserveCoverage = false;
        importer.npotScale = TextureImporterNPOTScale.None;
        importer.maxTextureSize = 2048;
        importer.wrapMode = TextureWrapMode.Clamp;
        importer.filterMode = FilterMode.Trilinear;
        importer.anisoLevel = 8;
        importer.textureCompression = TextureImporterCompression.CompressedHQ;
        importer.compressionQuality = 100;
        var platform = importer.GetPlatformTextureSettings("Standalone");
        platform.name = "Standalone";
        platform.overridden = true;
        platform.maxTextureSize = 2048;
        platform.format = TextureImporterFormat.BC7;
        platform.compressionQuality = 100;
        platform.crunchedCompression = false;
        importer.SetPlatformTextureSettings(platform);
    }
}
