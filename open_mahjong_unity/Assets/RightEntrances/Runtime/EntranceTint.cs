using UnityEngine;
namespace Salasasa.RightEntrances
{
    public enum EntranceTone { Surface, Secondary, Ink, Muted, Accent, OnAccent, Border, Dark, OnDark }
    public sealed class EntranceTint : MonoBehaviour
    {
        public UnityEngine.UI.Graphic target;
        public EntranceTone tone;
        [Range(0,1)] public float opacity=1;
        public void Apply(EntrancePalette p){var c=p.Get(tone);c.a*=opacity;target.color=c;}
    }
    [System.Serializable] public struct EntrancePalette
    {
        public string name;
        public Color surface,secondary,ink,muted,accent,onAccent,border,dark,onDark;
        public Color Get(EntranceTone t)=>t switch{EntranceTone.Surface=>surface,EntranceTone.Secondary=>secondary,EntranceTone.Ink=>ink,EntranceTone.Muted=>muted,EntranceTone.Accent=>accent,EntranceTone.OnAccent=>onAccent,EntranceTone.Border=>border,EntranceTone.Dark=>dark,_=>onDark};
    }
}
