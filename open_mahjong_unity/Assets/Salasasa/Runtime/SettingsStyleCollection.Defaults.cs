namespace Salasasa.Home
{
    public sealed partial class SettingsStyleCollection
    {
        public void RestoreCurrentPageDefaults()
        {
            var config = ConfigManager.Instance;
            if (!Ready || config == null) return;

            switch (Category)
            {
                case 0:
                    config.SetMasterVolume(100);
                    config.SetMusicVolume(100);
                    config.SetSoundEffectVolume(100);
                    config.SetVoiceVolume(100);
                    config.SetGongHuSoundEnabled(true);
                    config.SetMatchSuccessSoundEnabled(true);
                    break;
                case 1:
                    config.SetMoqieShortcutMode(0);
                    config.SetAskOtherPassShortcutMode(0);
                    config.SetHandCutConfirmMode(ConfigManager.DefaultHandCutConfirmMode);
                    config.SetOpeningAutoBuhuaEnabled(true);
                    config.SetForcePassEnabled(false);
                    config.SetStreamerModeEnabled(false);
                    config.SetRealtimeSpectateMode(0);
                    break;
                case 2:
                    config.SetHandSortSuitOrderMode(0);
                    config.SetHandSortHonorOrderMode(0);
                    config.SetHandSortDragonOrderMode(0);
                    config.SetHandSortRiichiDragonOrderMode(2);
                    config.SetWhiteDragonFaceMode(1);
                    config.SetActionButtonColorEnabled(false);
                    config.SetTileCornerLabelsEnabled(false);
                    config.SetDoraHintsEnabled(false);
                    config.ApplyTileOutlineStyle();
                    // The rule can still force spacing on; this only restores the user preference.
                    config.SetMeldSpacingEnabled(false);
                    config.SetLanguageMode((int)AppLanguage.SimplifiedChinese);
                    config.SetTargetFrameRate(ConfigManager.TargetFrameRateOptions[0]);
                    config.SetVsyncEnabled(true);
                    break;
                default:
                    return;
            }

            RefreshLayout();
        }
    }
}
