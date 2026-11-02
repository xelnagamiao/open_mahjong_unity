using UnityEngine;
using UnityEngine.UI;

namespace Salasasa.Home
{
    public sealed class DeveloperOptionsPanel : MonoBehaviour
    {
        public CompleteLobbyController owner;
        public Button closeButton, dismissButton, classroomButton;
        public bool IsOpen => gameObject.activeSelf;

        void Awake()
        {
            closeButton.onClick.AddListener(Close);
            dismissButton.onClick.AddListener(Close);
            classroomButton.onClick.AddListener(() => owner.Open("classroom"));
        }

        public void Show()
        {
            if(CompleteLobbyEnvironment.IsDevelopment)gameObject.SetActive(true);
        }

        public void Close() => gameObject.SetActive(false);

        void Update()
        {
            if(!CompleteLobbyEnvironment.IsDevelopment)Close();
        }
    }
}
