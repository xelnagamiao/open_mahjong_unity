using System;
using System.Linq;
using Salasasa.Leaderboards;
using UnityEngine;

namespace Salasasa.Home
{
    [Serializable]
    public sealed class DesignPresetChoice
    {
        public DesignOption option;
        public int value;
    }

    public sealed partial class InterfacePlansController
    {
        public UnityEngine.UI.Button restorePresetsButton;
        public DesignPresetChoice[] presetDefaults;
        bool restoringPreset;

        public static readonly DesignOption[] RestorableDesignOptions =
        {
            DesignOption.Navigation, DesignOption.Home, DesignOption.EntrancePalette,
            DesignOption.Inventory, DesignOption.RankingPalette, DesignOption.History,
            DesignOption.Settings, DesignOption.SettingsHint, DesignOption.EventDesign,
            DesignOption.HomeCurtain
        };

        public int DefaultDesignValue(DesignOption option)
        {
            switch (option)
            {
                case DesignOption.Navigation: return owner.headerNavigation.DefaultSide;
                case DesignOption.Home: return home.DefaultVariant;
                case DesignOption.EntrancePalette: return EntranceMainBridge.DefaultPalette;
                case DesignOption.RankingPalette: return LeaderboardPaletteEditor.DefaultPreset;
                case DesignOption.Settings: return SettingsStyleCollection.DefaultVariant;
                case DesignOption.HomeCurtain: return FourSideHeader.DefaultHomeCurtain;
                case DesignOption.MailPalette: return owner.mail ? owner.mail.defaultPalette : 0;
                case DesignOption.Inventory:
                case DesignOption.History:
                case DesignOption.SettingsHint:
                case DesignOption.EventDesign: return 0;
                default: throw new ArgumentOutOfRangeException(nameof(option), option, "Not an interface design preset.");
            }
        }

        public void RestoreDesignPreset()
        {
            if (!Application.isPlaying || restoringPreset) return;
            if (!owner || !owner.headerNavigation || !home || !homePalette || !entrances
                || !venue || !inventory || !ranking || !ranking.paletteEditor || !statistics
                || !statistics.paletteEditor || !settings || !eventVenue || !iconWorkbench || !frame)
                throw new InvalidOperationException("Interface preset dependencies have not been baked.");

            int page = SelectedPage;
            Vector2 position = frame.anchoredPosition;
            bool wasOpen = gameObject.activeSelf;
            restoringPreset = true;
            try
            {
                foreach (var dropdown in GetComponentsInChildren<TMPro.TMP_Dropdown>())
                    if (dropdown.IsExpanded) dropdown.Hide();

                owner.headerNavigation.SelectSide(DefaultDesignValue(DesignOption.Navigation));
                home.SelectVariant(DefaultDesignValue(DesignOption.Home));
                owner.headerNavigation.SelectHomeCurtain(DefaultDesignValue(DesignOption.HomeCurtain));
                entrances.SelectPalette(DefaultDesignValue(DesignOption.EntrancePalette));
                settings.SelectVariant(DefaultDesignValue(DesignOption.Settings));
                eventVenue.ConfigureAppearance(DefaultDesignValue(DesignOption.EventDesign), true);

                venue.SetVariant(0);
                inventory.SelectVariant(0);
                statistics.SetCardStyle(0);
                settings.SelectHintStyle(0);
                iconWorkbench.RestoreDefaults();
                PlatformTypography.Refresh();
                if (owner.mail) owner.mail.SelectPalette(owner.mail.defaultPalette);

                homePalette.ResetDefaults();
                ranking.paletteEditor.ResetAllPresets();
                statistics.paletteEditor.ResetAllPresets();
                PlayerPrefs.Save();
            }
            finally
            {
                SelectedPage = page;
                gameObject.SetActive(wasOpen);
                Refresh(); frame.anchoredPosition = position;
                restoringPreset = false;
            }
        }
    }
}
