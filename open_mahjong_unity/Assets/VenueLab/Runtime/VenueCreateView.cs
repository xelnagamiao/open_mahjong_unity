using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Salasasa.Venues
{
    public sealed class VenueCreateView : MonoBehaviour
    {
        public VenueTeamIconPicker teamIcon;
        public ScrollRect formScroll;
        public TMP_Text rulesTitle, rulesBody, nameHelp, dateHelp, descriptionHelp, organizerHelp, remarkHelp;
        void Awake()
        {
            foreach (var input in new[] { venueName, startDate, endDate, description, organizer, phone, remark })
                if (input) input.onSelect.AddListener(_ => RevealInput(input));
        }
        void RevealInput(TMP_InputField input)
        {
            if (!formScroll) return;
            LayoutRebuilder.ForceRebuildLayoutImmediate(formScroll.content);
            var viewport = formScroll.viewport;
            var bounds = RectTransformUtility.CalculateRelativeRectTransformBounds(viewport, input.transform);
            float shift = bounds.min.y < viewport.rect.yMin ? viewport.rect.yMin - bounds.min.y :
                bounds.max.y > viewport.rect.yMax ? viewport.rect.yMax - bounds.max.y : 0;
            var position = formScroll.content.anchoredPosition;
            position.y = Mathf.Clamp(position.y + shift, 0, Mathf.Max(0, formScroll.content.rect.height - viewport.rect.height));
            formScroll.StopMovement();
            formScroll.content.anchoredPosition = position;
        }
        void OnEnable() { ApplyHelp(); ResetScroll(); }
        public void ApplyHelp()
        {
            var help = VenueApplicationHelp.Load();
            var notes = help?.For(kind);
            if (notes == null) return;
            if (rulesTitle) rulesTitle.text = notes.rulesTitle;
            if (rulesBody) rulesBody.text = string.Join("\n", System.Array.ConvertAll(notes.rules, value => "• " + value));
            if (nameHelp) nameHelp.text = notes.name;
            if (dateHelp) dateHelp.text = notes.dates;
            if (descriptionHelp) descriptionHelp.text = notes.description;
            if (organizerHelp) organizerHelp.text = help.organizer;
            if (remarkHelp) remarkHelp.text = help.remark;
        }
        public void ResetScroll()
        {
            if (!formScroll) return;
            formScroll.StopMovement();
            formScroll.content.anchoredPosition = Vector2.zero;
            formScroll.verticalNormalizedPosition = 1;
        }
        public VenueKind kind;
        public int themeIndex;
        public bool warm;
        public TMP_InputField venueName, description, startDate, endDate, organizer, phone, remark;
        public Button close, cancel, preview;
        public TMP_Text validation;
        public VenueApplication Read() => new VenueApplication {
            team_icon_url = teamIcon ? teamIcon.Value : "",
            kind = kind == VenueKind.Base ? "base" : "event", name = venueName.text.Trim(),
            description = description.text.Trim(), planned_start_at = startDate.text.Trim(),
            planned_end_at = endDate.text.Trim(), organizer_name = organizer.text.Trim(),
            organizer_phone = phone.text.Trim(), remark = remark.text.Trim() };
        public void Write(VenueApplication value)
        {
            if(teamIcon)teamIcon.Write(value.team_icon_url);
            venueName.SetTextWithoutNotify(value.name ?? ""); description.SetTextWithoutNotify(value.description ?? "");
            startDate.SetTextWithoutNotify(value.planned_start_at ?? ""); endDate.SetTextWithoutNotify(value.planned_end_at ?? "");
            organizer.SetTextWithoutNotify(value.organizer_name ?? ""); phone.SetTextWithoutNotify(value.organizer_phone ?? "");
            remark.SetTextWithoutNotify(value.remark ?? ""); validation.text = "";
        }
    }
}
