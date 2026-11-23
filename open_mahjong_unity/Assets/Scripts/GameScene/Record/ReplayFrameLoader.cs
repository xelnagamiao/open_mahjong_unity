using System;
using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>Schedules archive loading on the Unity thread. Live packets and immediate seek callers can drain the same steps synchronously.</summary>
public sealed class ReplayFrameLoader : MonoBehaviour {
    internal const double FrameBudgetSeconds = .003;
    private static ReplayFrameLoader active;
    public static bool Busy => active != null;
    private IEnumerator steps;

    public static void Finish(IEnumerable work) {
        foreach (var unused in work) { }
    }

    public static void Begin(IEnumerable work, Action<Exception> onError, Action onComplete = null) {
        Cancel();
        var root = new GameObject("Replay loading", typeof(RectTransform), typeof(Canvas), typeof(GraphicRaycaster));
        var loader = root.AddComponent<ReplayFrameLoader>();
        active = loader;
        var canvas = root.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        UiCanvasLayers.Apply(canvas, UiCanvasLayers.Feedback, UiCanvasLayers.FeedbackOrder, false);
        var background = new GameObject("Input shield", typeof(RectTransform), typeof(Image)).GetComponent<Image>();
        background.rectTransform.SetParent(root.transform, false);
        background.rectTransform.anchorMin = Vector2.zero;
        background.rectTransform.anchorMax = Vector2.one;
        background.rectTransform.offsetMin = background.rectTransform.offsetMax = Vector2.zero;
        background.color = new Color(.06f,.08f,.10f,.94f);
        var label = new GameObject("Status", typeof(RectTransform), typeof(TextMeshProUGUI)).GetComponent<TextMeshProUGUI>();
        label.rectTransform.SetParent(background.transform, false);
        label.rectTransform.sizeDelta = new Vector2(340, 54);
        label.alignment = TextAlignmentOptions.Center;
        label.font = PlatformTypography.ChineseRegular;
        label.fontSize = 22;
        label.raycastTarget = false;
        TableText.Set(label, "加载中…");
        EventSystem.current?.SetSelectedGameObject(null);
        loader.steps = work.GetEnumerator();
        loader.StartCoroutine(loader.Run(onError, onComplete));
    }

    private IEnumerator Run(Action<Exception> onError, Action onComplete) {
        yield return null;
        while (active == this) {
            double deadline = Time.realtimeSinceStartupAsDouble + FrameBudgetSeconds;
            object wait = null;
            try {
                do {
                    bool more = steps.MoveNext();
                    if (active != this) yield break;
                    if (!more) {
                        Cancel();
                        onComplete?.Invoke();
                        yield break;
                    }
                    wait = steps.Current;
                    if (wait != null) break;
                } while (Time.realtimeSinceStartupAsDouble < deadline);
            } catch (Exception error) {
                if (active == this) Cancel();
                onError(error);
                yield break;
            }
            yield return wait;
        }
    }

    public static void Cancel() {
        if (!active) return;
        var loader = active;
        active = null;
        loader.Release();
        loader.gameObject.SetActive(false);
        Destroy(loader.gameObject);
    }

    private void Release() {
        (steps as IDisposable)?.Dispose();
        steps = null;
    }

    private void OnDestroy() {
        if (active == this) active = null;
        Release();
    }
}
