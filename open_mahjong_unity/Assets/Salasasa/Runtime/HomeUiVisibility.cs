using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Salasasa.Home
{
    [DisallowMultipleComponent, DefaultExecutionOrder(20000)]
    public sealed class HomeUiVisibility : MonoBehaviour
    {
        public LobbyShowcase artwork;
        public bool IsHidden { get; private set; }
        readonly Dictionary<Canvas, bool> canvases = new Dictionary<Canvas, bool>();
        readonly Dictionary<GraphicRaycaster, bool> raycasters = new Dictionary<GraphicRaycaster, bool>();
        readonly Dictionary<Graphic, bool> graphics = new Dictionary<Graphic, bool>();
        readonly List<Canvas> artworkCanvases = new List<Canvas>();
        int hiddenFrame;
        string window;

        public static void Toggle(CompleteLobbyController owner)
        {
            if (owner && owner.TryGetComponent<HomeUiVisibility>(out var visibility)) visibility.Toggle();
        }

        public void Toggle()
        {
            if (IsHidden) { Restore(); return; }
            if (!artwork || !artwork.canvas) return;
            window = WindowsManager.Instance ? WindowsManager.Instance.GetCurrentWindow() : "";
            hiddenFrame = Time.frameCount;
            IsHidden = true;
            CaptureAndHide();
            UiCanvasLayers.Configured += SuppressCanvas;
            SceneManager.sceneLoaded += SceneLoaded;
        }

        void LateUpdate()
        {
            if (!IsHidden) return;
            var keyboard = Keyboard.current;
            var mouse = Mouse.current;
            var touch = Touchscreen.current;
            bool pressed = (keyboard != null && (keyboard.escapeKey.wasPressedThisFrame || keyboard.f6Key.wasPressedThisFrame)) ||
                (mouse != null && (mouse.leftButton.wasPressedThisFrame || mouse.rightButton.wasPressedThisFrame)) ||
                (touch != null && touch.primaryTouch.press.wasPressedThisFrame);
            if ((Time.frameCount > hiddenFrame && pressed) ||
                (WindowsManager.Instance && WindowsManager.Instance.GetCurrentWindow() != window))
            { Restore(); return; }
            // Disabled raycasters leave the registry. Only newly enabled UI (e.g. a
            // TMP dropdown or a modal) and the few artwork graphics remain here.
            var active = RaycasterManager.GetRaycasters();
            for (int i = active.Count - 1; i >= 0; i--)
                if (i < active.Count && active[i] is GraphicRaycaster raycaster)
                {
                    SuppressCanvas(raycaster.GetComponent<Canvas>());
                    Suppress(raycaster, raycasters);
                }
            foreach (var canvas in artworkCanvases)
            {
                if (!canvas) continue;
                var registered = GraphicRegistry.GetGraphicsForCanvas(canvas);
                for (int i = registered.Count - 1; i >= 0; i--)
                    if (i < registered.Count && registered[i] && !IsArtwork(registered[i].transform))
                        Suppress(registered[i], graphics);
            }
        }

        void SceneLoaded(Scene scene, LoadSceneMode mode)
        {
            foreach (var root in scene.GetRootGameObjects())
            {
                foreach (var canvas in root.GetComponentsInChildren<Canvas>(true)) SuppressCanvas(canvas);
                foreach (var raycaster in root.GetComponentsInChildren<GraphicRaycaster>(true)) Suppress(raycaster, raycasters);
            }
        }

        static void Suppress<T>(T component, Dictionary<T, bool> original) where T : Behaviour
        {
            original.TryAdd(component, component.enabled);
            if (component.enabled) component.enabled = false;
        }

        void SuppressCanvas(Canvas canvas)
        {
            if (!canvas || !canvas.gameObject.scene.IsValid()) return;
            if (ContainsArtwork(canvas.transform))
            {
                if (!artworkCanvases.Contains(canvas)) artworkCanvases.Add(canvas);
            }
            else Suppress(canvas, canvases);
        }

        void CaptureAndHide()
        {
            foreach (var canvas in FindObjectsByType<Canvas>(FindObjectsInactive.Include, UnityEngine.FindObjectsSortMode.None))
                SuppressCanvas(canvas);
            // The header can share the artwork canvas. Only the explicit art roots remain visible.
            foreach (var graphic in artwork.canvas.rootCanvas.GetComponentsInChildren<Graphic>(true))
            {
                if (IsArtwork(graphic.transform)) continue;
                Suppress(graphic, graphics);
            }
            foreach (var raycaster in FindObjectsByType<GraphicRaycaster>(FindObjectsInactive.Include, UnityEngine.FindObjectsSortMode.None))
            {
                if (!raycaster.gameObject.scene.IsValid()) continue;
                Suppress(raycaster, raycasters);
            }
        }

        bool ContainsArtwork(Transform canvas)
            => IsAncestor(canvas, artwork.background ? artwork.background.transform : null) ||
               IsAncestor(canvas, artwork.backgroundImage ? artwork.backgroundImage.transform : null) ||
               IsAncestor(canvas, artwork.artworkShade ? artwork.artworkShade.transform : null) ||
               IsAncestor(canvas, artwork.characterLayer ? artwork.characterLayer.transform : null);

        bool IsArtwork(Transform graphic)
            => IsAncestor(artwork.background ? artwork.background.transform : null, graphic) ||
               IsAncestor(artwork.backgroundImage ? artwork.backgroundImage.transform : null, graphic) ||
               IsAncestor(artwork.artworkShade ? artwork.artworkShade.transform : null, graphic) ||
               IsAncestor(artwork.characterLayer ? artwork.characterLayer.transform : null, graphic);

        static bool IsAncestor(Transform parent, Transform child)
            => parent && child && (parent == child || child.IsChildOf(parent));

        public void Restore()
        {
            if (!IsHidden) return;
            IsHidden = false;
            UiCanvasLayers.Configured -= SuppressCanvas;
            SceneManager.sceneLoaded -= SceneLoaded;
            artworkCanvases.Clear();
            foreach (var pair in canvases) if (pair.Key) pair.Key.enabled = pair.Value;
            foreach (var pair in raycasters) if (pair.Key) pair.Key.enabled = pair.Value;
            foreach (var pair in graphics) if (pair.Key) pair.Key.enabled = pair.Value;
            canvases.Clear(); raycasters.Clear(); graphics.Clear();
        }

        void OnDisable() => Restore();
        void OnDestroy() => Restore();
    }
}
