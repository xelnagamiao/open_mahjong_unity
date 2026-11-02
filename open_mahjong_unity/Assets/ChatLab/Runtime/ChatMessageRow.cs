using TMPro;
using UnityEngine;
using UnityEngine.UI;
namespace Salasasa.ChatLab
{
    public sealed class ChatMessageRow : MonoBehaviour
    {
        public Button button, avatarButton, nameButton;
        public TMP_Text author, body, time;
        public RawImage portrait;
        public UnityEngine.UI.Image bubble;
        public RectTransform bubbleRect;
        public int boundIndex;
    }
}
