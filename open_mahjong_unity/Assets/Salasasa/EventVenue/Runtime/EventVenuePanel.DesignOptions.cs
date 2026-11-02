using UnityEngine;

namespace Salasasa.Events
{
    public sealed partial class EventVenuePanel
    {
        public void ConfigureAppearance(int design, bool original)
        {
            Design = Mathf.Clamp(design, 0, view.PaletteCount - 1);
            Original = true;
            view.ApplyPalette(Design);
            PlayerPrefs.SetInt("EventVenuePalette", Design);
            PlayerPrefs.SetInt("EventVenueOriginalPrefabs", Original ? 1 : 0);
            PlayerPrefs.Save();
            if (!gameObject.activeInHierarchy) return;
            foreach (var card in GetComponentsInChildren<EventVenueNativeCard>(true))
                if (card.gameObject.activeInHierarchy) card.Apply(Original);
            RefreshAll(true);
        }
    }
}
