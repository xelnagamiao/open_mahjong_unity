using System;
using System.Collections;
using UnityEngine;

namespace Salasasa.Home
{
    [DisallowMultipleComponent]
    public sealed class LobbyTableTransition : MonoBehaviour
    {
        [SerializeField] CanvasGroup[] lobbyLayers = Array.Empty<CanvasGroup>();
        [SerializeField] CanvasGroup[] tableLayers = Array.Empty<CanvasGroup>();

        public bool IsRunning { get; private set; }
        Coroutine routine;
        LayerState[] lobbyState = Array.Empty<LayerState>(), tableState = Array.Empty<LayerState>();

        readonly struct LayerState
        {
            public readonly CanvasGroup Group;
            public readonly float Alpha;
            public readonly bool Interactable, BlocksRaycasts;

            public LayerState(CanvasGroup group)
            {
                Group = group;
                Alpha = group.alpha;
                Interactable = group.interactable;
                BlocksRaycasts = group.blocksRaycasts;
            }

            public void Restore()
            {
                if (!Group) return;
                Group.alpha = Alpha;
                Group.interactable = Interactable;
                Group.blocksRaycasts = BlocksRaycasts;
            }
        }

        public bool TryPlay(float phaseDuration, Action resumeTable)
        {
            if (IsRunning) return true;
            if (!isActiveAndEnabled || phaseDuration <= 0f || resumeTable == null
                || !Configured(lobbyLayers) || !Configured(tableLayers)) return false;
            lobbyState = Capture(lobbyLayers);
            tableState = Capture(tableLayers);
            IsRunning = true;
            routine = StartCoroutine(Play(phaseDuration, resumeTable));
            return true;
        }

        static bool Configured(CanvasGroup[] layers)
        {
            if (layers == null || layers.Length == 0) return false;
            foreach (var layer in layers) if (!layer) return false;
            return true;
        }

        static LayerState[] Capture(CanvasGroup[] layers)
        {
            var states = new LayerState[layers.Length];
            for (int i = 0; i < layers.Length; i++) states[i] = new LayerState(layers[i]);
            return states;
        }

        IEnumerator Play(float phaseDuration, Action resumeTable)
        {
            try
            {
                SetInput(lobbyState, false);
                SetInput(tableState, false);
                // Keep the old window current until its separate canvases have faded out.
                yield return Fade(lobbyState, 1f, 0f, phaseDuration);
                SetAlpha(tableState, 0f);
                resumeTable();
                // The suspended GamePanel stays active; fade its canvas roots independently.
                yield return Fade(tableState, 0f, 1f, phaseDuration);
            }
            finally
            {
                Restore();
                routine = null;
                IsRunning = false;
            }
        }

        static IEnumerator Fade(LayerState[] states, float from, float to, float duration)
        {
            for (float elapsed = 0f; elapsed < duration; elapsed += Time.unscaledDeltaTime)
            {
                float progress = Mathf.SmoothStep(0f, 1f, elapsed / duration);
                SetAlpha(states, Mathf.LerpUnclamped(from, to, progress));
                yield return null;
            }
            SetAlpha(states, to);
        }

        static void SetAlpha(LayerState[] states, float factor)
        {
            foreach (var state in states) if (state.Group) state.Group.alpha = state.Alpha * factor;
        }

        static void SetInput(LayerState[] states, bool enabled)
        {
            foreach (var state in states) if (state.Group) state.Group.blocksRaycasts = enabled && state.BlocksRaycasts;
        }

        public void Cancel()
        {
            if (routine != null) StopCoroutine(routine);
            routine = null;
            Restore();
            IsRunning = false;
        }

        void Restore()
        {
            foreach (var state in lobbyState) state.Restore();
            foreach (var state in tableState) state.Restore();
            lobbyState = tableState = Array.Empty<LayerState>();
        }

        void OnDisable() => Cancel();
        void OnDestroy() => Cancel();
    }
}
