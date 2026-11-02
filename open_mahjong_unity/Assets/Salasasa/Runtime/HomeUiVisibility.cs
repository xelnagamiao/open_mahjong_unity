using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
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
            CaptureAndHide();
        }

        void CaptureAndHide()
        {
            foreach (var canvas in FindObjectsByType<Canvas>(FindObjectsInactive.Include))
            {
                if (!canvas.gameObject.scene.IsValid() || ContainsArtwork(canvas.transform)) continue;
                if (!canvases.ContainsKey(canvas)) canvases.Add(canvas, canvas.enabled);
                canvas.enabled = false;
            }
            // The header can share the artwork canvas. Only the explicit art roots remain visible.
            foreach (var graphic in artwork.canvas.rootCanvas.GetComponentsInChildren<Graphic>(true))
            {
                if (IsArtwork(graphic.transform)) continue;
                if (!graphics.ContainsKey(graphic)) graphics.Add(graphic, graphic.enabled);
                graphic.enabled = false;
            }
            foreach (var raycaster in FindObjectsByType<GraphicRaycaster>(FindObjectsInactive.Include))
            {
                if (!raycaster.gameObject.scene.IsValid()) continue;
                if (!raycasters.ContainsKey(raycaster)) raycasters.Add(raycaster, raycaster.enabled);
                raycaster.enabled = false;
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
            foreach (var pair in canvases) if (pair.Key) pair.Key.enabled = pair.Value;
            foreach (var pair in raycasters) if (pair.Key) pair.Key.enabled = pair.Value;
            foreach (var pair in graphics) if (pair.Key) pair.Key.enabled = pair.Value;
            canvases.Clear(); raycasters.Clear(); graphics.Clear();
        }

        void OnDisable() => Restore();
        void OnDestroy() => Restore();
    }
}
