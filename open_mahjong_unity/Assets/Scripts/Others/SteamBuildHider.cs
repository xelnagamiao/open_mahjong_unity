using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 挂到场景任意物体上，把需要在 Steam 构建中隐藏的物体拖进列表。
/// 当 ConfigManager.BuildForSteam 为 true 时，这些物体都会被 SetActive(false)。
/// </summary>
public class SteamBuildHider : MonoBehaviour {
    [Tooltip("Steam 构建时需要隐藏的物体，可拖拽添加")]
    [SerializeField] private List<GameObject> hideObjects = new List<GameObject>();

    private void Awake() {
        if (!ConfigManager.BuildForSteam) {
            return;
        }
        foreach (GameObject obj in hideObjects) {
            if (obj != null) {
                obj.SetActive(false);
            }
        }
    }
}
