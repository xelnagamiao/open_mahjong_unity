using System.Collections;
using UnityEngine;

namespace Salasasa.Home
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(CanvasGroup))]
    public sealed class BakedModalFade : MonoBehaviour
    {
        CanvasGroup group;
        Coroutine fadeRoutine;
        public int OpenCount { get; private set; }
        public bool IsAnimating { get; private set; }
        public bool IsClosing { get; private set; }

        void OnEnable()
        {
            group = GetComponent<CanvasGroup>();
            if (Application.isPlaying) BeginShow(0f);
        }
        public void Show()
        {
            if (!gameObject.activeSelf) { gameObject.SetActive(true); return; }
            if (IsClosing) BeginShow(group ? group.alpha : 0f);
        }
        void BeginShow(float from)
        {
            if (!group) group = GetComponent<CanvasGroup>();
            if (fadeRoutine != null) StopCoroutine(fadeRoutine);
            ++OpenCount; IsClosing = false; IsAnimating = true;
            group.alpha = from; group.interactable = true; group.blocksRaycasts = true;
            fadeRoutine = StartCoroutine(Fade(Time.realtimeSinceStartupAsDouble, from, true));
        }
        public void Hide()
        {
            if (!gameObject.activeInHierarchy) { gameObject.SetActive(false); return; }
            if (IsClosing) return;
            if (!group) group = GetComponent<CanvasGroup>();
            if (fadeRoutine != null) StopCoroutine(fadeRoutine);
            IsClosing = true; IsAnimating = true; group.blocksRaycasts = false;
            fadeRoutine = StartCoroutine(Fade(Time.realtimeSinceStartupAsDouble, group.alpha, false));
        }
        IEnumerator Fade(double started, float from, bool show)
        {
            float duration = WindowFadeTransition.DurationSeconds;
            float elapsed = 0f;
            double previous = started;
            while (elapsed < duration)
            {
                yield return null;
                double now = Time.realtimeSinceStartupAsDouble;
                elapsed += Mathf.Min((float)System.Math.Max(0, now - previous), 1f / 30f);
                previous = now;
                float progress = Mathf.Clamp01(elapsed / duration);
                group.alpha = Mathf.Lerp(from, show ? 1f : 0f, progress * progress * (3f - 2f * progress));
            }
            group.alpha = show ? 1f : 0f;
            IsAnimating = false; fadeRoutine = null;
            if (!show) gameObject.SetActive(false);
        }
        void OnDisable()
        {
            if (fadeRoutine != null) StopCoroutine(fadeRoutine);
            fadeRoutine = null; IsAnimating = false; IsClosing = false;
            if (group != null) { group.alpha = 1f; group.interactable = group.blocksRaycasts = true; }
        }
    }
}
