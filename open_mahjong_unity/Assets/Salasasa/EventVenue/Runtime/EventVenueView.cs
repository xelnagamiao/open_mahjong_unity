using UnityEngine;
using UnityEngine.UI;

namespace Salasasa.Events
{
    public sealed class EventVenueView : MonoBehaviour
    {
        public string[] paletteNames = { "暖橙", "夜紫", "雾蓝", "酒红" };
        public int PaletteCount => paletteNames.Length;
        public GameObject participationSummary, registrationForm, registrationWithdrawal;
        public GameObject managementRail;
        public GameObject[] pages;
        public GameObject[] managementPages;
        public Color paper, ink, muted, accent, onAccent, border, field;

        public void ApplyPalette(int palette)
        {
            palette = Mathf.Clamp(palette, 0, PaletteCount - 1);
            foreach (var binding in GetComponentsInChildren<EventVenuePaletteBinding>(true)) binding.Apply(palette);
        }
#if UNITY_EDITOR
        public void CapturePalette(int palette)
        {
            foreach (var binding in GetComponentsInChildren<EventVenuePaletteBinding>(true)) binding.Capture(palette);
        }
#endif
    }
}
