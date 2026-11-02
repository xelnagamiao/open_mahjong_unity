using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine.SceneManagement;

public sealed class PlatformCanvasLayerBuildValidation : IProcessSceneWithReport {
    public int callbackOrder => 100;
    public void OnProcessScene(Scene scene, BuildReport report) {
        if (report == null || scene.path != "Assets/Scenes/MainScene.unity") return;
        PlatformCanvasLayerBaker.Validate(scene);
    }
}
