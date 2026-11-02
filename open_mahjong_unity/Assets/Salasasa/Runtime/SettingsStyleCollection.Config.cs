using System;
using UnityEngine;

namespace Salasasa.Home
{
    public sealed partial class SettingsStyleCollection
    {
        static bool SettingEditable(string key) => key != "TileOutlineStyleRow" &&
            (key != "MeldSpacingRow" || RuleRegistry.Current?.MeldSpacingEnabled != true);

        static int ReadSetting(string key)
        {
            var config = ConfigManager.Instance;
            switch (key)
            {
                case "ConfigSliderMaster": return config.MasterVolume;
                case "ConfigSliderMusic": return config.MusicVolume;
                case "ConfigSliderEffect": return config.SoundEffectVolume;
                case "ConfigSliderVoice": return config.VoiceVolume;
                case "GongHuSoundRow": return config.GongHuSoundEnabled ? 1 : 0;
                case "MatchSuccessSoundRow": return config.MatchSuccessSoundEnabled ? 1 : 0;
                case "ConfigAskHandActionCancel (1)": return config.MoqieShortcutMode;
                case "ConfigAskOtherActionCancel (1)": return config.AskOtherPassShortcutMode;
                case "HandCutConfirmRow": return config.HandCutConfirmMode;
                case "OpeningAutoBuhuaRow": return config.OpeningAutoBuhuaEnabled ? 1 : 0;
                case "ForcePassRow": return config.ForcePassEnabled ? 1 : 0;
                case "StreamerModeRow": return config.StreamerModeEnabled ? 1 : 0;
                case "RealtimeSpectateModeRow": return config.RealtimeSpectateMode;
                case "HandSortSuitRow": return config.HandSortSuitOrderMode;
                case "HandSortHonorRow": return config.HandSortHonorOrderMode;
                case "HandSortDragonRow": return config.HandSortDragonOrderMode;
                case "HandSortRiichiDragonRow": return config.HandSortRiichiDragonOrderMode;
                case "ConfigWhiteType": return config.WhiteDragonFaceMode;
                case "ActionButtonColorRow": return config.ActionButtonColorEnabled ? 1 : 0;
                case "VsyncRow": return config.VsyncEnabled ? 1 : 0;
                case "TileOutlineStyleRow": return 0;
                case "MeldSpacingRow": return RuleRegistry.Current?.MeldSpacingEnabled == true || config.MeldSpacingEnabled ? 1 : 0;
                case "LanguageRow": return (int)config.LanguageMode;
                case "FrameRateLimit": return Mathf.Max(0, Array.IndexOf(ConfigManager.TargetFrameRateOptions, config.TargetFrameRate));
                default: throw new ArgumentOutOfRangeException(nameof(key), key, "Unknown settings field");
            }
        }

        void WriteSetting(string key, int value)
        {
            if (synchronizing || !Ready || !ConfigManager.Instance || !SettingEditable(key)) return;
            var config = ConfigManager.Instance;
            switch (key)
            {
                case "ConfigSliderMaster": config.SetMasterVolume(value); break;
                case "ConfigSliderMusic": config.SetMusicVolume(value); break;
                case "ConfigSliderEffect": config.SetSoundEffectVolume(value); break;
                case "ConfigSliderVoice": config.SetVoiceVolume(value); break;
                case "GongHuSoundRow": config.SetGongHuSoundEnabled(value == 1); break;
                case "MatchSuccessSoundRow": config.SetMatchSuccessSoundEnabled(value == 1); break;
                case "ConfigAskHandActionCancel (1)": config.SetMoqieShortcutMode(value); break;
                case "ConfigAskOtherActionCancel (1)": config.SetAskOtherPassShortcutMode(value); break;
                case "HandCutConfirmRow": config.SetHandCutConfirmMode(value); break;
                case "OpeningAutoBuhuaRow": config.SetOpeningAutoBuhuaEnabled(value == 1); break;
                case "ForcePassRow": config.SetForcePassEnabled(value == 1); break;
                case "StreamerModeRow": config.SetStreamerModeEnabled(value == 1); break;
                case "RealtimeSpectateModeRow": config.SetRealtimeSpectateMode(value); break;
                case "HandSortSuitRow": config.SetHandSortSuitOrderMode(value); break;
                case "HandSortHonorRow": config.SetHandSortHonorOrderMode(value); break;
                case "HandSortDragonRow": config.SetHandSortDragonOrderMode(value); break;
                case "HandSortRiichiDragonRow": config.SetHandSortRiichiDragonOrderMode(value); break;
                case "ConfigWhiteType": config.SetWhiteDragonFaceMode(value); break;
                case "ActionButtonColorRow": config.SetActionButtonColorEnabled(value == 1); break;
                case "VsyncRow": config.SetVsyncEnabled(value == 1); break;
                case "MeldSpacingRow": config.SetMeldSpacingEnabled(value == 1); break;
                case "LanguageRow": config.SetLanguageMode(value); break;
                case "FrameRateLimit": config.SetTargetFrameRate(ConfigManager.TargetFrameRateOptions[Mathf.Clamp(value, 0, ConfigManager.TargetFrameRateOptions.Length - 1)]); break;
                default: throw new ArgumentOutOfRangeException(nameof(key), key, "Unknown settings field");
            }
            SyncField(key);
            if (key.StartsWith("HandSort") || key == "ConfigWhiteType")
                foreach (var page in bakedPages) if (page.handPreview) page.handPreview.Refresh();
        }
    }
}
