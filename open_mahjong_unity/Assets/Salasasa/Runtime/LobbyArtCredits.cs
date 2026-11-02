using TMPro;
using UnityEngine;

namespace Salasasa.Home
{
    [DisallowMultipleComponent]
    public sealed class LobbyArtCredits : MonoBehaviour
    {
        public LobbyShowcase artwork;
        public TMP_Text characterLabel, backgroundLabel;
        public UnityEngine.UI.Button characterSource, backgroundSource;

        void OnEnable()
        {
            if (!artwork || !characterSource || !backgroundSource) return;
            artwork.ArtworkChanged += Refresh;
            ConfigManager.OnLanguageChanged += Refresh;
            characterSource.onClick.AddListener(OpenCharacterSource);
            backgroundSource.onClick.AddListener(OpenBackgroundSource);
            Refresh();
        }

        void OnDisable()
        {
            if (artwork) artwork.ArtworkChanged -= Refresh;
            ConfigManager.OnLanguageChanged -= Refresh;
            if (characterSource) characterSource.onClick.RemoveListener(OpenCharacterSource);
            if (backgroundSource) backgroundSource.onClick.RemoveListener(OpenBackgroundSource);
        }

        public void Refresh()
        {
            if (!artwork) return;
            bool characterVisible = artwork.artworkEnabled && artwork.showCharacterFill && artwork.characterImage.texture;
            bool backgroundVisible = artwork.artworkEnabled && artwork.backgroundImage.texture;
            var character = artwork.CurrentArtwork; var background = artwork.CurrentBackgroundArtwork;
            characterLabel.text = characterVisible ? PlatformText.Format("角色：{0}",artwork.characterArtwork ? PlatformText.Get("自定义素材") : character?.characterCredit) : "";
            backgroundLabel.text = backgroundVisible ? PlatformText.Format("背景：{0}",artwork.backgroundArtwork ? PlatformText.Get("自定义素材") : background?.backgroundCredit) : "";
            characterSource.interactable = characterVisible && !artwork.characterArtwork && !string.IsNullOrEmpty(character?.characterSourceUrl);
            backgroundSource.interactable = backgroundVisible && !artwork.backgroundArtwork && !string.IsNullOrEmpty(background?.backgroundSourceUrl);
        }

        void OpenCharacterSource() { Open(artwork.CurrentArtwork?.characterSourceUrl); }
        void OpenBackgroundSource() { Open(artwork.CurrentBackgroundArtwork?.backgroundSourceUrl); }
        static void Open(string url)
        {
            if (System.Uri.TryCreate(url, System.UriKind.Absolute, out var uri) && uri.Scheme == "https") Application.OpenURL(url);
        }
    }
}
