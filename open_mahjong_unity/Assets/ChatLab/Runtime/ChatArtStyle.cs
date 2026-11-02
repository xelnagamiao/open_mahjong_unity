using TMPro;
using UnityEngine;

namespace Salasasa.ChatLab
{
    [CreateAssetMenu(menuName="Salasasa/Chat Art Style")]
    public sealed class ChatArtStyle : ScriptableObject
    {
        public string displayName;
        public TMP_FontAsset bodyFont,titleFont;
        public Color panel,sidebar,header,row,text,secondary,accent,ownMessage,input,border;
        public Color headerText,sidebarText,sidebarSecondary,accentText,headerAccent,notice,listSelection;
        public Color sidebarInput,sidebarBorder;
        public Color chatHeader,chatHeaderText,ownSender;
        public Color[] senderColors=System.Array.Empty<Color>();
        [Range(0,2)] public int silhouette;
        [Range(0,3)] public int motif;
        [Range(0,2)] public int bubbleSilhouette;
        public float cornerCut=9,borderWidth=1,bubbleBorderWidth;
    }
}
