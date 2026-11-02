using System;
using TMPro;
using UnityEngine;

namespace Salasasa.Home
{
    [Serializable]
    public sealed class LobbyTheme
    {
        public string name;
        public string reference;
        public Color background;
        public Color backgroundBottom;
        public Color surface;
        public Color ink;
        public Color muted;
        public Color accent;
        public Color accentInk;
        public Color line;
        public Color silhouette;
        public LobbyShape.Kind shape;
        public float corner;
        public TMP_FontAsset font;
        public TMP_FontAsset titleFont;
    }
}
