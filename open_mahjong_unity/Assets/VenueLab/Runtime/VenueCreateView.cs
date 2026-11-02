using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Salasasa.Venues
{
    public sealed class VenueCreateView : MonoBehaviour
    {
        public VenueKind kind;
        public int themeIndex;
        public bool warm;
        public TMP_InputField venueName, description, startDate, endDate, organizer, phone, remark;
        public Button close, cancel, preview;
        public TMP_Text validation;
        public VenueApplication Read() => new VenueApplication {
            kind = kind == VenueKind.Base ? "base" : "event", name = venueName.text.Trim(),
            description = description.text.Trim(), planned_start_at = startDate.text.Trim(),
            planned_end_at = endDate.text.Trim(), organizer_name = organizer.text.Trim(),
            organizer_phone = phone.text.Trim(), remark = remark.text.Trim() };
        public void Write(VenueApplication value)
        {
            venueName.SetTextWithoutNotify(value.name ?? ""); description.SetTextWithoutNotify(value.description ?? "");
            startDate.SetTextWithoutNotify(value.planned_start_at ?? ""); endDate.SetTextWithoutNotify(value.planned_end_at ?? "");
            organizer.SetTextWithoutNotify(value.organizer_name ?? ""); phone.SetTextWithoutNotify(value.organizer_phone ?? "");
            remark.SetTextWithoutNotify(value.remark ?? ""); validation.text = "";
        }
    }
}
