using System;
using UnityEngine;

namespace Salasasa.Home
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(InventoryPanel))]
    public sealed class InventoryPalette : MonoBehaviour
    {
        public enum Role { Surface, Header, Sidebar, Detail, Card, SelectedCard, Stroke, Ink, Muted, Accent, OnAccent, Badge, BadgeInk, EquippedInk, HeaderInk, Category, SidebarInk, Close, CloseInk }
        [Serializable] public sealed class Binding { public UnityEngine.UI.Graphic graphic; public Role role; }
        [Serializable] public sealed class Palette
        {
            public string name;
            public Color[] colors;
            public Color this[Role role] => colors[(int)role];
        }
        public Palette palette = Graphite();
        public Binding[] bindings;
        InventoryPanel panel;
        public Palette Current => palette;

        void Awake() { ApplyCurrent(); }
        void OnEnable() { ApplyCurrent(); }
        public void ApplyCurrent()
        {
            if (palette == null || palette.colors == null) return;
            if (!panel) panel = GetComponent<InventoryPanel>();
            foreach (var binding in bindings ?? Array.Empty<Binding>())
                if (binding.graphic) binding.graphic.color = Current[binding.role];
            panel.ApplyAppearance(Current);
        }

        public static Palette Graphite() =>
            Make("原版石墨", "3A3A3A", "202020", "303030", "484848", "555555", "68584A", "737373", "FFFFFF", "D0D0D0", "ECA15B", "161616", "2D2D2D", "FFFFFF", "B9DBBB", "FFFFFF", "414141", "FFFFFF", "AE463B", "FFFFFF");
        static Palette Make(string name, params string[] hex)
        {
            var colors = new Color[hex.Length];
            for (int i = 0; i < hex.Length; i++) ColorUtility.TryParseHtmlString("#" + hex[i], out colors[i]);
            return new Palette { name = name, colors = colors };
        }
    }
}
