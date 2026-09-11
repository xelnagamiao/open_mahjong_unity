#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using Unity.Collections;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Editor-only derived outline mesh. The canonical tile FBX remains the only
/// geometric source; runtime uses a saved Mesh asset, with no CPU edge search.
/// </summary>
public static class TileOutlineRibbonBaker
{
    public const string SourcePath = "Assets/Resources/3D/3DCardNew2026.8.5.fbx";
    public const string MeshPath = "Assets/Resources/3D/TileOutlineRibbon.asset";
    public const string MaterialPath = "Assets/Resources/Materials/Tiles/TileOutlineRibbon.mat";
    public const string ShaderName = "Hidden/Mahjong/TileOutlineRibbon";
    const int BakeVersion = 1;
    const string MetadataPrefix = "TileOutlineRibbonBaker:";
    const double WeldStep = 0.0000001;
    const float CoplanarDot = 0.999999f;
    const float PlaneTolerance = 0.0000001f;
    static bool rebuildQueued;
    static bool rebuilding;

    [Serializable]
    sealed class BakeMetadata
    {
        public int version;
        public string sourceGuid;
        public string sourceDependencyHash;
    }

    [MenuItem("Tools/Mahjong/Tile Outline/Rebuild Ribbon Mesh")]
    public static void RebuildRibbonMesh()
    {
        Rebuild(true);
        Debug.Log("Tile outline ribbon mesh rebuilt from the canonical tile FBX.");
    }

    [InitializeOnLoadMethod]
    static void AfterDomainReload()
    {
        QueueExistingAssetRefresh();
    }

    internal static void QueueExistingAssetRefresh()
    {
        if(rebuildQueued || rebuilding || AssetDatabase.IsAssetImportWorkerProcess())return;
        rebuildQueued=true;
        EditorApplication.delayCall+=RefreshExistingAsset;
    }

    static void RefreshExistingAsset()
    {
        rebuildQueued=false;
        if(EditorApplication.isCompiling || EditorApplication.isUpdating) {
            QueueExistingAssetRefresh();
            return;
        }
        // Automatic imports only maintain an already installed ribbon asset.
        // The explicit menu creates the first one and its material.
        if(!AssetDatabase.LoadAssetAtPath<Mesh>(MeshPath))return;
        try { Rebuild(false); }
        catch(Exception error) {
            Debug.LogError("Tile outline ribbon mesh could not be refreshed. Fix the source model and use Tools/Mahjong/Tile Outline/Rebuild Ribbon Mesh.\n"+error);
        }
    }

    static bool HasCurrentMetadata(BakeMetadata expected)
    {
        var importer=AssetImporter.GetAtPath(MeshPath);
        if(importer==null || string.IsNullOrEmpty(importer.userData)
            || !importer.userData.StartsWith(MetadataPrefix,StringComparison.Ordinal))return false;
        try {
            var saved=JsonUtility.FromJson<BakeMetadata>(importer.userData.Substring(MetadataPrefix.Length));
            return saved!=null && saved.version==expected.version && saved.sourceGuid==expected.sourceGuid
                && saved.sourceDependencyHash==expected.sourceDependencyHash;
        } catch(ArgumentException) { return false; }
    }

    static void Rebuild(bool force)
    {
        if(rebuilding)return;
        rebuilding=true;
        Mesh generated=null;
        try {
            string sourceGuid=AssetDatabase.AssetPathToGUID(SourcePath);
            Require(!string.IsNullOrEmpty(sourceGuid),"Canonical tile FBX is missing: "+SourcePath);
            var metadata=new BakeMetadata {
                version=BakeVersion,sourceGuid=sourceGuid,
                sourceDependencyHash=AssetDatabase.GetAssetDependencyHash(SourcePath).ToString()
            };
            var existing=AssetDatabase.LoadAssetAtPath<Mesh>(MeshPath);
            if(!force && existing && HasCurrentMetadata(metadata))return;
            string previousGuid=null;long previousLocalId=0;
            if(existing)
                Require(AssetDatabase.TryGetGUIDAndLocalFileIdentifier(existing,out previousGuid,out previousLocalId),
                    "Cannot resolve existing ribbon mesh identity before updating it");
            var source=FindSourceMesh();
            generated=BuildRibbonMesh(source);
            generated.name="TileOutlineRibbon";
            generated.hideFlags=HideFlags.None;
            // Validate all inputs and build the complete mesh before modifying assets.
            var shader=Shader.Find(ShaderName);
            Require(shader,"Required ribbon shader is missing: "+ShaderName);
            EnsureMaterial(shader);
            if(existing) {
                // Update the existing object, retaining its .meta GUID and local ID
                // so prefab references survive all future model edits and bakes.
                EditorUtility.CopySerialized(generated,existing);
                EditorUtility.SetDirty(existing);
                AssetDatabase.SaveAssetIfDirty(existing);
            } else {
                Require(!AssetDatabase.LoadMainAssetAtPath(MeshPath),"Ribbon asset path is occupied by another asset: "+MeshPath);
                EnsureFolder(Path.GetDirectoryName(MeshPath).Replace('\\','/'));
                AssetDatabase.CreateAsset(generated,MeshPath);
                existing=generated;generated=null;
                AssetDatabase.SaveAssetIfDirty(existing);
            }
            var importer=AssetImporter.GetAtPath(MeshPath);
            Require(importer!=null,"Cannot record ribbon source dependency metadata");
            importer.userData=MetadataPrefix+JsonUtility.ToJson(metadata);
            AssetDatabase.WriteImportSettingsIfDirty(MeshPath);
            if(previousGuid!=null) {
                Require(AssetDatabase.TryGetGUIDAndLocalFileIdentifier(existing,out string currentGuid,out long currentLocalId)
                    && currentGuid==previousGuid && currentLocalId==previousLocalId,
                    "Ribbon mesh asset identity changed unexpectedly during the update");
            }
        } finally {
            if(generated)UnityEngine.Object.DestroyImmediate(generated);
            rebuilding=false;
        }
    }

    static Mesh FindSourceMesh()
    {
        Mesh found=null;
        foreach(var asset in AssetDatabase.LoadAllAssetsAtPath(SourcePath)) {
            if(!(asset is Mesh mesh) || mesh.name!="Cube")continue;
            Require(!found,"Canonical FBX has more than one mesh named Cube");
            found=mesh;
        }
        Require(found,"Canonical FBX has no Cube mesh");
        return found;
    }

    static void EnsureMaterial(Shader shader)
    {
        var material=AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
        if(material) {
            Require(material.shader==shader,"Existing ribbon material uses a different shader: "+MaterialPath);
            if(!material.enableInstancing) {
                material.enableInstancing=true;EditorUtility.SetDirty(material);
                AssetDatabase.SaveAssetIfDirty(material);
            }
            return;
        }
        Require(!AssetDatabase.LoadMainAssetAtPath(MaterialPath),"Ribbon material path is occupied by another asset: "+MaterialPath);
        EnsureFolder(Path.GetDirectoryName(MaterialPath).Replace('\\','/'));
        var created=new Material(shader) { name="TileOutlineRibbon",enableInstancing=true };
        AssetDatabase.CreateAsset(created,MaterialPath);
        AssetDatabase.SaveAssetIfDirty(created);
    }

    static void EnsureFolder(string path)
    {
        if(AssetDatabase.IsValidFolder(path))return;
        int split=path.LastIndexOf('/');
        Require(split>0,"Asset folder must be under Assets: "+path);
        string parent=path.Substring(0,split);EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent,path.Substring(split+1));
    }

    struct PointKey : IEquatable<PointKey>
    {
        public long x,y,z;
        public PointKey(Vector3 p) {
            x=(long)Math.Round(p.x/WeldStep,MidpointRounding.AwayFromZero);
            y=(long)Math.Round(p.y/WeldStep,MidpointRounding.AwayFromZero);
            z=(long)Math.Round(p.z/WeldStep,MidpointRounding.AwayFromZero);
        }
        public bool Equals(PointKey p)=>x==p.x && y==p.y && z==p.z;
        public override bool Equals(object o)=>o is PointKey p && Equals(p);
        public override int GetHashCode() { unchecked { return ((x.GetHashCode()*397)^y.GetHashCode())*397^z.GetHashCode(); } }
    }
    struct EdgeKey : IEquatable<EdgeKey>
    {
        public int lo,hi;
        public EdgeKey(int a,int b) { lo=Math.Min(a,b);hi=Math.Max(a,b); }
        public bool Equals(EdgeKey e)=>lo==e.lo && hi==e.hi;
        public override bool Equals(object o)=>o is EdgeKey e && Equals(e);
        public override int GetHashCode() { unchecked { return lo*397^hi; } }
    }
    struct EdgeUse
    {
        public int triangle,from,to;
        public EdgeUse(int t,int a,int b) { triangle=t;from=a;to=b; }
    }

    /// <summary>
    /// Create one quad for each non-coplanar edge of a closed, oriented mesh.
    /// The input is read through MeshData and need not be CPU-readable at runtime.
    /// Returned transient mesh belongs to the caller until saved as an asset.
    /// </summary>
    internal static Mesh BuildRibbonMesh(Mesh source)
    {
        Require(source,"Source mesh is null");
        Vector3[] points,shadingNormals;int[] triangles;
        using(var snapshot=Mesh.AcquireReadOnlyMeshData(source)) {
            var data=snapshot[0];
            Require(data.subMeshCount==1,"Tile body must have one submesh");
            var sub=data.GetSubMesh(0);
            Require(sub.topology==MeshTopology.Triangles && sub.indexCount>0 && sub.indexCount%3==0,"Tile body must contain triangles");
            using(var a=new NativeArray<Vector3>(data.vertexCount,Allocator.Temp)) { data.GetVertices(a);points=a.ToArray(); }
            using(var a=new NativeArray<Vector3>(data.vertexCount,Allocator.Temp)) { data.GetNormals(a);shadingNormals=a.ToArray(); }
            using(var a=new NativeArray<int>(sub.indexCount,Allocator.Temp)) { data.GetIndices(a,0,true);triangles=a.ToArray(); }
        }
        var weldedIds=new int[points.Length];var uniquePoints=new List<Vector3>();
        var pointLookup=new Dictionary<PointKey,int>();
        for(int i=0;i<points.Length;i++) {
            var key=new PointKey(points[i]);
            if(!pointLookup.TryGetValue(key,out int id)) { id=uniquePoints.Count;pointLookup.Add(key,id);uniquePoints.Add(points[i]); }
            weldedIds[i]=id;
        }
        var faceNormals=new Vector3[triangles.Length/3];var edges=new Dictionary<EdgeKey,List<EdgeUse>>();
        for(int face=0;face<faceNormals.Length;face++) {
            int a=triangles[face*3],b=triangles[face*3+1],c=triangles[face*3+2];
            Vector3 cross=Vector3.Cross(points[b]-points[a],points[c]-points[a]);
            Require(cross.sqrMagnitude>1e-24f,"Degenerate source triangle "+face);
            // Vector3.normalized returns zero for some of this model's very
            // small bevel triangles. Explicit division preserves their normals.
            faceNormals[face]=cross/Mathf.Sqrt(cross.sqrMagnitude);
            Require(Vector3.Dot(faceNormals[face],shadingNormals[a]+shadingNormals[b]+shadingNormals[c])>0,"Source triangle winding is inconsistent with its outward normal: "+face);
            AddEdge(edges,face,weldedIds[a],weldedIds[b]);
            AddEdge(edges,face,weldedIds[b],weldedIds[c]);
            AddEdge(edges,face,weldedIds[c],weldedIds[a]);
        }
        foreach(var uses in edges.Values) {
            Require(uses.Count==2,"Tile must be closed after welding coincident UV/material seams");
            Require(uses[0].from==uses[1].to && uses[0].to==uses[1].from,"Adjacent triangle winding is inconsistent");
        }
        ValidateConvexShapeAndOrigin(points,triangles,faceNormals);
        var ordered=new List<EdgeKey>(edges.Keys);
        ordered.Sort((a,b)=>a.lo!=b.lo?a.lo.CompareTo(b.lo):a.hi.CompareTo(b.hi));
        var positions=new List<Vector3>();var normal0=new List<Vector3>();
        var oppositeEndpoint=new List<Vector3>();var normal1=new List<Vector3>();
        var corner=new List<Vector2>();var indices=new List<int>();
        foreach(var edge in ordered) {
            var uses=edges[edge];
            var n0=faceNormals[uses[0].triangle];var n1=faceNormals[uses[1].triangle];
            if(Vector3.Dot(n0,n1)>=CoplanarDot)continue;
            Vector3 a=uniquePoints[edge.lo],b=uniquePoints[edge.hi];int first=positions.Count;
            for(int i=0;i<4;i++) {
                bool atA=i<2;
                positions.Add(atA?a:b);oppositeEndpoint.Add(atA?b:a);
                normal0.Add(n0);normal1.Add(n1);corner.Add(new Vector2(i==1||i==2?1:0,0));
            }
            indices.Add(first);indices.Add(first+1);indices.Add(first+2);
            indices.Add(first);indices.Add(first+2);indices.Add(first+3);
        }
        Require(positions.Count>0,"Source has no non-coplanar silhouette edges");
        var mesh=new Mesh { name="TileOutlineRibbon",hideFlags=HideFlags.HideAndDontSave,
            indexFormat=positions.Count>65535?IndexFormat.UInt32:IndexFormat.UInt16 };
        try {
            // CPU triangles are intentionally degenerate. The ribbon shader
            // projects the exact endpoints and expands each quad in pixels.
            mesh.SetVertices(positions);mesh.SetNormals(normal0);
            mesh.SetUVs(0,oppositeEndpoint);mesh.SetUVs(1,normal1);mesh.SetUVs(2,corner);
            mesh.SetIndices(indices,MeshTopology.Triangles,0,false);mesh.bounds=source.bounds;
            return mesh;
        } catch {
            UnityEngine.Object.DestroyImmediate(mesh);
            throw;
        }
    }

    static void ValidateConvexShapeAndOrigin(Vector3[] points,int[] triangles,Vector3[] faceNormals)
    {
        // The shader uses the projected local origin to select each edge's
        // outward screen direction. That is valid for a convex closed body
        // with its local origin strictly inside. Validate before asset writes;
        // do not silently bake a future concave or off-centre edited model.
        for(int face=0;face<faceNormals.Length;face++) {
            Vector3 anchor=points[triangles[face*3]];
            Vector3 normal=faceNormals[face];
            Require(Vector3.Dot(normal,anchor)>PlaneTolerance,
                "Tile local origin must be strictly inside every outward face plane; failed triangle "+face);
            for(int vertex=0;vertex<points.Length;vertex++) {
                if(Vector3.Dot(normal,points[vertex]-anchor)>PlaneTolerance)
                    throw new InvalidOperationException("Tile outline ribbons require a convex body; vertex "+vertex+" is outside triangle "+face+" plane");
            }
        }
    }

    static void AddEdge(Dictionary<EdgeKey,List<EdgeUse>> edges,int face,int a,int b)
    {
        Require(a!=b,"Source triangle collapsed at the position-weld tolerance");
        var edge=new EdgeKey(a,b);
        if(!edges.TryGetValue(edge,out var uses)) { uses=new List<EdgeUse>(2);edges.Add(edge,uses); }
        uses.Add(new EdgeUse(face,a,b));
    }
    static void Require(bool condition,string message) { if(!condition)throw new InvalidOperationException(message); }
}

sealed class TileOutlineRibbonSourcePostprocessor : AssetPostprocessor
{
    static void OnPostprocessAllAssets(string[] imported,string[] deleted,string[] moved,string[] movedFrom)
    {
        foreach(string path in imported) {
            if(!string.Equals(path,TileOutlineRibbonBaker.SourcePath,StringComparison.OrdinalIgnoreCase))continue;
            TileOutlineRibbonBaker.QueueExistingAssetRefresh();
            return;
        }
    }
}
#endif
