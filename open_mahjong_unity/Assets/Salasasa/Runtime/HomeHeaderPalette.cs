using System;
using UnityEngine;

namespace Salasasa.Home
{
    [DisallowMultipleComponent]
    public sealed class HomeHeaderPalette : MonoBehaviour
    {
        public const string Preference = "SalasasaInterface.HomePalette.V2";
        public const int ColorCount = 3;
        [Serializable] sealed class SavedColors { public string[] colors; public bool[] custom; }
        public HomeHeaderAppearance appearance;
        public event Action ColorsApplied;
        readonly Color[] colors = new Color[ColorCount];
        readonly bool[] custom = new bool[ColorCount];
        bool loaded;
        void Awake() => Initialize();
        public void Initialize() { EnsureLoaded(); Apply(); }
        void EnsureLoaded()
        {
            if (loaded) return;
            loaded = true;
            Array.Clear(custom, 0, custom.Length);
            if (!Application.isPlaying || !PlayerPrefs.HasKey(Preference)) return;
            try
            {
                var saved = JsonUtility.FromJson<SavedColors>(PlayerPrefs.GetString(Preference));
                if (saved?.colors == null || saved.custom == null || saved.colors.Length != ColorCount || saved.custom.Length != ColorCount) return;
                for (int i = 0; i < ColorCount; i++)
                    if (saved.custom[i] && ColorUtility.TryParseHtmlString(saved.colors[i], out var value)) { colors[i] = value; custom[i] = true; }
            }
            catch (ArgumentException) { Array.Clear(custom, 0, custom.Length); }
        }
        public Color ResolveColor(int slot, Color fallback)
        {
            EnsureLoaded(); slot = Mathf.Clamp(slot, 0, ColorCount - 1);
            return custom[slot] ? colors[slot] : fallback;
        }
        public bool IsCustomized(int slot) { EnsureLoaded(); return custom[Mathf.Clamp(slot, 0, ColorCount - 1)]; }
        public Color GetColor(int slot) => ResolveColor(slot, appearance ? appearance.DefaultColor(slot) : Color.white);

        public void ResetDefaults()
        {
            EnsureLoaded(); Array.Clear(custom, 0, custom.Length);
            if (Application.isPlaying) { PlayerPrefs.DeleteKey(Preference); PlayerPrefs.Save(); }
            Apply();
        }
        public void RestoreAndApply() { loaded = false; EnsureLoaded(); Apply(); }
        public void BakeDefaults() { loaded = true; Array.Clear(custom, 0, custom.Length); Apply(); }
        void Apply() { if (appearance) appearance.Apply(); ColorsApplied?.Invoke(); }
    }
}
