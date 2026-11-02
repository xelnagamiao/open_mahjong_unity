using UnityEngine;
using TMPro;

namespace Salasasa.Events
{
    [RequireComponent(typeof(TMP_InputField))]
    public sealed class EventVenueInput : MonoBehaviour
    {
        public EventVenuePanel controller;
        public string key;
        public TMP_InputField field;
        void Awake() { field.onValueChanged.AddListener(Changed); }
        void OnDestroy() { if (field != null) field.onValueChanged.RemoveListener(Changed); }
        void Changed(string value) { controller.SetDraft(key, value); }
    }
}
