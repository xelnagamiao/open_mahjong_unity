using UnityEngine;

namespace Salasasa.Home
{
    [DisallowMultipleComponent]
    public sealed class HomeArtworkAccess : MonoBehaviour
    {
        public GameObject[] configurationButtons;
        public RectTransform[] visibilityButtons;
        bool? shown;

        void OnEnable() => Refresh();
        void Update() => Refresh();

        public void Refresh()
        {
            bool development = CompleteLobbyEnvironment.IsDevelopment;
            if (shown == development) return;
            shown = development;
            if (configurationButtons != null)
                foreach (var button in configurationButtons) if (button) button.SetActive(development);
            if (visibilityButtons != null)
                foreach (var button in visibilityButtons)
                    if (button) button.anchoredPosition = new Vector2(development ? 60 : 4, button.anchoredPosition.y);
        }
    }
}
