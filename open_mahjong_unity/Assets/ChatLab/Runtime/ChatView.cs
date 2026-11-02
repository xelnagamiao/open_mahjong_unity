using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Salasasa.ChatLab
{
    public sealed class ChatView : MonoBehaviour
    {
        public int index;
        public Color panel, row, accent, text, muted, selected, listSelected, tabIdle, tabText, disabledFill;
        public Color ownSender;
        public Color[] senderColors=System.Array.Empty<Color>();
        public TMP_FontAsset senderFont;
        public float messageFontSize=16, senderFontSize=16, timestampFontSize=12, messageLineHeight=24, messageGap=6;
        public Button close, create, membersButton, applicationsButton, information, send, apply;
        public Button[] tabs;
        public TMP_Text[] tabLabels;
        public UnityEngine.UI.Image[] tabIndicators = new UnityEngine.UI.Image[0];
        public TMP_Text title, status, notice, count, sectionTitle, empty, roomDescription, applyLabel;
        public TMP_InputField search, composer;
        public GameObject chatArea, discoveryArea, joinedControls, composerArea;
        public ChatListRow[] rows;
        public ChatMessageRow[] messages;
        public ChatListRow[] memberRows;
        public ScrollRect messageScroll;
        public RectTransform messageContent;
        public UnityEngine.UI.Image sendFill;
    }

}
