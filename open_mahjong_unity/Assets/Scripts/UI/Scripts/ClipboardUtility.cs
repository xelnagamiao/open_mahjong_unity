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
        Copy(text, ok => { if (receiver) receiver.SendMessage(callback, ok ? "ok" : "failed"); });
#endif
    }

    public static void Copy(string text, System.Action<bool> completed = null) {
        System.Action<bool> report = completed ?? (_ => { });
#if UNITY_WEBGL && !UNITY_EDITOR
        ClipboardCopyRequest.Begin(text ?? "", report);
#else
        bool ok;
        try { GUIUtility.systemCopyBuffer = text ?? ""; ok = true; }
        catch (System.Exception) { ok = false; }
        report(ok);
#endif
    }

    public static void CopyAndNotify(string text, string title, string successMessage) {
        Copy(text, ok => {
            if (ok) NotificationManager.Instance?.ShowTip(title, true, successMessage);
        });
    }
}
