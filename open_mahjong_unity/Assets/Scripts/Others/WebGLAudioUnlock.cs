using System.Runtime.InteropServices;
using UnityEngine;

/// <summary>
/// Installs one WebGL-wide audio unlock handler as soon as the Unity runtime starts.
/// The handler resumes Unity's AudioContext in the capture phase of the first user
/// interaction, before gameplay UI processes that same input.
/// </summary>
public static class WebGLAudioUnlock {
#if UNITY_WEBGL && !UNITY_EDITOR
    [DllImport("__Internal")]
    private static extern void InstallWebGLAudioUnlock();
#endif

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Initialize() {
#if UNITY_WEBGL && !UNITY_EDITOR
        InstallWebGLAudioUnlock();
#endif
    }
}
