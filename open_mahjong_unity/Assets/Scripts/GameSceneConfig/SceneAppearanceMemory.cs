using System.Collections;
using UnityEngine;

internal static class SceneAppearanceMemory
{
    // A full Resources scan is deliberately reserved for a lifecycle boundary.
    // Appearance controls release the assets they own directly, so a normal
    // selection change never stalls the frame on an unrelated scene asset.
    private const double QuietSeconds = 0.35;
    private const double MaximumDelaySeconds = 1.0;
    private static ConfigManager owner;
    private static bool requested;
    private static double lastRequest;

    internal static void RequestCleanup()
    {
        // Kept as a source-compatible no-op for callers that already perform a
        // targeted release. Do not turn this back into UnloadUnusedAssets.
    }

    internal static void RequestGlobalCleanup()
    {
        if (!Application.isPlaying) return;
        var config = ConfigManager.Instance;
        if (config == null || !config.gameObject.activeInHierarchy) return;

        lastRequest = Time.realtimeSinceStartupAsDouble;
        requested = true;
        if (owner != null) return;
        owner = config;
        owner.StartCoroutine(CollectUnused());
    }

    private static IEnumerator CollectUnused()
    {
        do
        {
            double deadline = Time.realtimeSinceStartupAsDouble + MaximumDelaySeconds;
            // Give the UI and deferred Destroy calls at least one frame to finish.
            do { yield return null; }
            while (Time.realtimeSinceStartupAsDouble < lastRequest + QuietSeconds
                && Time.realtimeSinceStartupAsDouble < deadline);

            requested = false;
            yield return Resources.UnloadUnusedAssets();
            // A superseded asynchronous load may finish during this scan.
        } while (requested);
        owner = null;
    }
}
