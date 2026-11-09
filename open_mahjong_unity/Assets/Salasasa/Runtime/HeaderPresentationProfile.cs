using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace Salasasa.Home
{
    [Serializable]
    public sealed class HeaderEntryPresentation
    {
        [Tooltip("稳定的导航路由；不要用显示文字作为 ID。")]
        public string route;
        public string simplifiedChinese, traditionalChinese, english;
        [Min(12)] public float fontSize = 18;
        [Min(12)] public float minimumFontSize = 15;
        public float characterSpacing;
        public Vector2 captionOffset = new Vector2(0, -20);
        public Vector2 captionSize = new Vector2(72, 22);
        public TextAlignmentOptions captionAlignment = TextAlignmentOptions.Baseline;
        public Vector2 artworkOffset;
        public Vector2 shadowOffset = new Vector2(0, -2);
        [Min(44)] public float minimumWidth = 72;
        [Min(44)] public float maximumWidth = 128;
        public bool growForTranslation = true;
        public Vector2 iconSize = new Vector2(72, 64);
        public Vector2 iconOffset;
        public Vector2 iconShadowOffset;
        public Color textColor = Color.white, selectedTextColor = Color.white;
        public Color iconColor = Color.white, selectedIconColor = Color.white;
        [Tooltip("主菜单颜色继续由现有界面方案中的主菜单调色控制。")]
        public bool useHomePalette;
        public string LocalizationKey => "header." + route;

        public string Label(AppLanguage language)
        {
            if (language == AppLanguage.English && !string.IsNullOrEmpty(english)) return english;
            if (language == AppLanguage.TraditionalChinese && !string.IsNullOrEmpty(traditionalChinese)) return traditionalChinese;
            return PlatformText.HasLocaleCatalog(language) ? PlatformText.Get(simplifiedChinese ?? "", language) : simplifiedChinese ?? "";
        }
    }

    [CreateAssetMenu(menuName = "Salasasa/Header Presentation", fileName = "HeaderPresentation")]
    public sealed class HeaderPresentationProfile : ScriptableObject
    {
        public TMP_FontAsset font;
        public Material faceMaterial, outlineMaterial, shadowMaterial;
        [Min(0)] public float labelGap = 8;
        [Min(0)] public float utilityGap = 28;
        public List<HeaderEntryPresentation> entries = new List<HeaderEntryPresentation>();
        [NonSerialized] public int revision;
        public HeaderEntryPresentation Find(string route) => entries.Find(entry => entry.route == route);
        public void Changed() { unchecked { revision++; } }
        void OnValidate() => Changed();
    }
}
