using System;
using UnityEngine;

public static class SalaCoinAppearance {
    public const string PreferenceKey = "SalaCoinStyle";
    public const int DefaultStyle = 0;
    public static readonly string[] Names = { "双环铜钱", "平涂方孔", "浅金线描", "双枚叠币", "经典刻纹", "樱花浮雕", "和风波纹" };
    public static readonly string[] ResourcesPaths = {
        "Currency/sala_classic", "Currency/sala_flat", "Currency/sala_outline", "Currency/sala_pair",
        "Currency/sala_anime_classic", "Currency/sala_anime_sakura", "Currency/sala_anime_seigaiha"
    };
    static readonly Sprite[] sprites = new Sprite[ResourcesPaths.Length];
    public static event Action Changed;
    public static int Selected => Mathf.Clamp(PlayerPrefs.GetInt(PreferenceKey, DefaultStyle), 0, Names.Length - 1);
    public static Sprite SpriteAt(int index) {
        index = Mathf.Clamp(index, 0, Names.Length - 1);
        return sprites[index] ? sprites[index] : sprites[index] = Resources.Load<Sprite>(ResourcesPaths[index]);
    }
    public static void Select(int index) {
        PlayerPrefs.SetInt(PreferenceKey, Mathf.Clamp(index, 0, Names.Length - 1));
        Changed?.Invoke();
    }
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetListeners() => Changed = null;
}
