using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;

namespace Salasasa.Home
{
    public sealed partial class LobbyOverlay : MonoBehaviour
    {
        public LobbyShowcase owner;
        public string CurrentPage { get; private set; }
        public bool IsOpen => layer != null;
        GameObject layer;
        Transform window;
        LobbyTheme T => owner.Theme;

        public void Close()
        {
            ResetArtworkPicker();
            if (layer != null)
            {
                layer.SetActive(false);
                if (Application.isPlaying) Destroy(layer); else DestroyImmediate(layer);
            }
            layer = null; window = null; CurrentPage = "";
            if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);
        }

        public void Open(string action, string title)
        {
            if (!CompleteLobbyEnvironment.IsDevelopment || (action != "appearance" && action != "character")) return;
            Close(); CurrentPage = action;
            ArtworkPicker();
        }

        void Window(string title, float width = 640, float height = 652, bool centered = false, bool dimBackground = true)
        {
            var root = LobbyUI.Rect(transform, "ModalLayer", 0, 0, 1600, 900); LobbyUI.Stretch(root); layer = root.gameObject;
            var scrim = LobbyUI.Shape(root, "DismissArea", 0, 0, 1600, 900, new Color(.05f, .07f, .1f, dimBackground ? .34f : 0));
            LobbyUI.Stretch(scrim.rectTransform); scrim.raycastTarget = true;
            scrim.gameObject.AddComponent<UnityEngine.UI.Button>().onClick.AddListener(Close);
            float outerWidth = width + (centered ? 0 : 56);
            var holder = LobbyUI.Rect(root, "Window", 0, 0, outerWidth, height);
            if (centered) { holder.anchorMin = holder.anchorMax = new Vector2(.5f, .5f); holder.pivot = new Vector2(.5f, .5f); holder.anchoredPosition = Vector2.zero; }
            else { holder.anchorMin = holder.anchorMax = new Vector2(1, 1); holder.anchoredPosition = new Vector2(-outerWidth - 22, -126); }
            LobbyUI.Shape(holder, "Shadow", 8, 12, outerWidth, height, new Color(.05f, .07f, .1f, .18f), T.shape, T.corner);
            var bg = LobbyUI.Shape(holder, "Surface", 0, 0, outerWidth, height, T.surface, T.shape, T.corner, T.line); bg.raycastTarget = true;
            window = centered ? bg.transform : LobbyUI.Rect(bg.transform, "Content", 28, 0, width, height);
            Label("PanelTitle", title, 30, 34, width - 115, 49, 32, T.ink, true);
            LobbyUI.Shape(window, "TitleRule", 30, 115, width - 60, 1, T.line);
            var close = LobbyUI.Button(window, "Close", "", width - 74, 38, 44, 44, T, Close);
            LobbyUI.Icon(close.transform, "CloseIcon", InkGraphic.Form.Close, 10, 10, 24, 24, T.ink);
        }

        TMP_Text Label(string name, string value, float x, float y, float w, float h, float size, Color color, bool heading = false)
            => LobbyUI.Text(window, name, value, x, y, w, h, size, color, heading ? T.titleFont : T.font);
        UnityEngine.UI.Button Button(string name, string label, float x, float y, float w, float h, Action click, bool primary = false)
            => LobbyUI.Button(window, name, label, x, y, w, h, T, click, primary);
    }
}
