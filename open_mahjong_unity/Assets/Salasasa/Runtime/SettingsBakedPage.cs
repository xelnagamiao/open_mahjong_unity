using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Salasasa.Home
{
    [DisallowMultipleComponent]
    public sealed class SettingsBakedPage : MonoBehaviour
    {
        public int variant,category;
        public RectTransform stage,panel,content,close,reviewStrip;
        public TMP_Text title;
        public SettingsShapeGraphic panelSurface,panelShadow,headerSurface,titleAccent,divider,reviewSurface;
        public SettingsOrnamentGraphic ornament;
        public SettingsHandOrderPreview handPreview;
        public Button[] tabs,variants;
        public SettingsStyleField[] controls;
        public SettingsStyleSection[] sections;
    }
}
