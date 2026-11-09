using System.Runtime.InteropServices;
using UnityEngine;

public static class ClipboardUtility {
#if UNITY_WEBGL && !UNITY_EDITOR
    [DllImport("__Internal")]
    private static extern void CopyInvitationWithResult(string text, string receiver, string callback);
#endif
    public static void CopyWithResult(string text, GameObject receiver, string callback) {
#if UNITY_WEBGL && !UNITY_EDITOR
        CopyInvitationWithResult(text, receiver.name, callback);
#else
        string result;
        try { Copy(text); result = "ok"; } catch (System.Exception) { result = "failed"; }
        receiver.SendMessage(callback, result);
#endif
    }

#if UNITY_WEBGL && !UNITY_EDITOR
    [DllImport("__Internal")]
    private static extern void CopyToClipboard(string text);
#endif

    public static void Copy(string text) {
#if UNITY_WEBGL && !UNITY_EDITOR
        CopyToClipboard(text);
#else
        GUIUtility.systemCopyBuffer = text;
#endif
    }
}
