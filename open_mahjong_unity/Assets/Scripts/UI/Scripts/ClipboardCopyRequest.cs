using System;
using UnityEngine;

// A unique receiver keeps asynchronous browser replies paired with each copy operation.
public sealed class ClipboardCopyRequest : MonoBehaviour {
    Action<bool> completed;

    public static void Begin(string text, Action<bool> callback) {
        var owner = new GameObject("ClipboardCopy_" + Guid.NewGuid().ToString("N"));
        DontDestroyOnLoad(owner);
        var request = owner.AddComponent<ClipboardCopyRequest>();
        request.completed = callback;
        request.Invoke(nameof(TimedOut), 8f);
        ClipboardUtility.CopyWithResult(text, owner, nameof(OnClipboardResult));
    }

    [UnityEngine.Scripting.Preserve]
    public void OnClipboardResult(string result) {
        var callback = completed;
        if (callback == null) return;
        completed = null;
        CancelInvoke();
        try { callback(result == "ok"); }
        finally { Destroy(gameObject); }
    }

    void TimedOut() => OnClipboardResult("failed");
}
