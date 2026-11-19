#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;

internal static class ClientValidationScreenshot {
    internal static void Capture(string output, string name, Action<bool,string> check) {
        var camera=Camera.main;
        check(camera!=null,"capture camera "+name);if(camera==null)return;
        var canvases=UnityEngine.Object.FindObjectsByType<Canvas>(FindObjectsInactive.Exclude,FindObjectsSortMode.None)
            .Where(c=>c.isRootCanvas && c.renderMode==RenderMode.ScreenSpaceOverlay)
            .Select(c=>new {canvas=c,mode=c.renderMode,worldCamera=c.worldCamera,distance=c.planeDistance}).ToArray();
        var target=RenderTexture.GetTemporary(1280,720,24,RenderTextureFormat.ARGB32);
        var previous=RenderTexture.active;var pixels=new Texture2D(1280,720,TextureFormat.RGB24,false);
        try {
            foreach(var saved in canvases) {
                saved.canvas.renderMode=RenderMode.ScreenSpaceCamera;
                saved.canvas.worldCamera=camera;saved.canvas.planeDistance=camera.nearClipPlane+.1f;
            }
            Canvas.ForceUpdateCanvases();
            RenderPipeline.SubmitRenderRequest(camera,new RenderPipeline.StandardRequest {destination=target});
            RenderTexture.active=target;pixels.ReadPixels(new Rect(0,0,1280,720),0,0);pixels.Apply();
            File.WriteAllBytes(Path.Combine(output,name+".png"),pixels.EncodeToPNG());
            check(pixels.GetPixels32().Count(p=>p.r>8 || p.g>8 || p.b>8)>10000,"rendered image "+name);
        } finally {
            foreach(var saved in canvases) {
                saved.canvas.renderMode=saved.mode;saved.canvas.worldCamera=saved.worldCamera;saved.canvas.planeDistance=saved.distance;
            }
            Canvas.ForceUpdateCanvases();RenderTexture.active=previous;
            RenderTexture.ReleaseTemporary(target);UnityEngine.Object.Destroy(pixels);
        }
    }
}
#endif
