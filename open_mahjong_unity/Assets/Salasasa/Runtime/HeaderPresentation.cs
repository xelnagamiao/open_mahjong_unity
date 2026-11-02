using UnityEngine;

namespace Salasasa.Home
{
    [DisallowMultipleComponent]
    public sealed class HeaderPresentation : MonoBehaviour
    {
        public FourSideHeader navigation;
        public HeaderPresentationProfile profile;
        public HeaderButtonPresentation[] buttons;
        int appliedRevision = -1;
        AppLanguage appliedLanguage;
        void OnEnable() { ConfigManager.OnLanguageChanged += Refresh; PlatformTypography.Changed += Refresh; Refresh(); }
        void OnDisable() { ConfigManager.OnLanguageChanged -= Refresh; PlatformTypography.Changed -= Refresh; }
        void Update() { if (profile && (appliedRevision != profile.revision || appliedLanguage != ConfigManager.CurrentLanguage)) Refresh(); }
        public void Refresh()
        {
            if (!profile || !navigation || buttons == null) return;
            foreach (var item in buttons) if (item) { item.profile = profile; item.Apply(); }
            if (navigation.returnGame) navigation.returnGame.ApplyPresentation();
            navigation.RequestLayout();
            appliedRevision = profile.revision;
            appliedLanguage = ConfigManager.CurrentLanguage;
        }
    }
}
