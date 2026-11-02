using TMPro;
using UnityEngine;
using UnityEngine.UI;
namespace Salasasa.ChatLab
{
    public sealed class ChatListRow : MonoBehaviour
    {
        public Button button, avatarButton, deleteButton;
        public TMP_Text title, detail, badge, time, initials;
        public RawImage portrait;
        public UnityEngine.UI.Image fill, mark;
        public GameObject badgeRoot;
        public string boundId;
        public int boundIndex;
    }
}
