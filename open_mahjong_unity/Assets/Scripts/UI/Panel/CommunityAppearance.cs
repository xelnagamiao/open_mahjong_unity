using System;
using UnityEngine;

namespace Salasasa.Community {
    [Serializable] public sealed class CommunityPalette {
        public string id,label;
        public Color surface,card,header,text,muted,accent,line,actionText;
    }
    public sealed class CommunityAppearance : MonoBehaviour {
        public CommunityPanel panel;
        public CommunityPalette palette;
        public CommunityPalette Current => palette;
    }
}
