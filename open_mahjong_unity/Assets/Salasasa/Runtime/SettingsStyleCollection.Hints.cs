using UnityEngine;

namespace Salasasa.Home
{
    public sealed partial class SettingsStyleCollection
    {
        public const string HintPreferenceKey = "SettingsHintDesignV1";
        public static readonly string[] HintStyleNames = { "主题色签" };
        public int HintStyle => 0;

        public void SelectHintStyle(int index)
        {
            foreach (var page in bakedPages)
                if (page && page.TryGetComponent<SettingsPagePresentation>(out var presentation))
                    presentation.ApplyHintStyle(0);
        }
    }
}
