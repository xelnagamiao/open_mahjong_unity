using System;
using System.Linq;
using UnityEngine;

namespace Salasasa.DataLab
{
    public sealed class HomePaletteEditor : MonoBehaviour
    {
        public const int PresetCount = 4, CustomIndex = 4, DefaultPreset = 0;
        public HomeDataPanel owner;
        HomePalette factoryCustomPalette;
        bool initialized;
        bool TestSession => Environment.GetCommandLineArgs().Any(x => x == "--data-capture" || x == "--palette-verify" || x == "--statistics-validate" || x == "--statistics-verify" || x == "-data-page-capture");
        string Key => "Salasasa.DataLab.Palette.v1" + (TestSession ? ".validation" : "");
        string SelectionKey => Key + ".selection";
        public bool LoadedSavedPalette { get; private set; }
        [Serializable] sealed class SavedPalette { public int version = 1, basePreset; public string[] colors, tiers; }
        public void Initialize(HomeDataPanel panel)
        {
            if (initialized) return;
            initialized = true; owner = panel;
            factoryCustomPalette = panel.palettes[CustomIndex].Copy();
            LoadedSavedPalette = LoadSaved();
            if (!Environment.GetCommandLineArgs().Contains("--data-capture") || Environment.GetCommandLineArgs().Contains("--palette-verify"))
                owner.styleIndex = Mathf.Clamp(PlayerPrefs.GetInt(SelectionKey, DefaultPreset), 0, CustomIndex);
        }
        public void RememberSelection(int index)
        {
            if(!Application.isPlaying||TestSession)return;
            PlayerPrefs.SetInt(SelectionKey,index);PlayerPrefs.Save();
        }

        bool LoadSaved()
        {
            if(!PlayerPrefs.HasKey(Key))return false;
            try{
                var saved=JsonUtility.FromJson<SavedPalette>(PlayerPrefs.GetString(Key));
                if(saved==null||saved.version!=1||saved.colors==null||saved.colors.Length!=13||saved.tiers==null||saved.tiers.Length!=6||saved.basePreset<0||saved.basePreset>=PresetCount)return false;
                if(saved.colors.Concat(saved.tiers).Any(x=>!TryHex(x,out _)))return false;
                owner.palettes[CustomIndex]=new HomePalette{name="我的配色",colors=saved.colors.Select(Parse).ToArray(),tierSurfaces=saved.tiers.Select(Parse).ToArray()};
                return true;
            }catch(ArgumentException){return false;}
        }

        public void ResetAllPresets()
        {
            if (!initialized) Initialize(owner);
            PlayerPrefs.DeleteKey(Key); PlayerPrefs.DeleteKey(SelectionKey);
            LoadedSavedPalette = false;
            owner.palettes[CustomIndex] = factoryCustomPalette.Copy();
            owner.SetStyle(DefaultPreset);
            PlayerPrefs.DeleteKey(SelectionKey); PlayerPrefs.Save();
        }
        public void ClearValidationSave()
        {
            if(!TestSession)return;
            PlayerPrefs.DeleteKey(Key);PlayerPrefs.DeleteKey(SelectionKey);PlayerPrefs.Save();
        }

        static bool TryHex(string value,out Color color)
        {
            color=Color.white;string clean=(value??"").Trim().TrimStart('#');
            return clean.Length==6&&ColorUtility.TryParseHtmlString("#"+clean,out color);
        }
        static Color Parse(string value){TryHex(value,out var color);return color;}
    }
}
