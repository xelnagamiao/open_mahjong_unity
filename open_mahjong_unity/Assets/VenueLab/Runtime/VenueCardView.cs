using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Salasasa.Venues
{
    public sealed class VenueCardView : MonoBehaviour
    {
        public TMP_Text title, description, access, status, metric, metricCaption, dateMonth, identifier, actionText;
        public Button openButton;
        public VenueShapeGraphic accent, selectionMark;
        public VenueShapeGraphic header;
        public bool doubleColumn;
        public Color defaultBodyColor = Color.white, defaultMetricColor = Color.black;
        [HideInInspector] public string entryId;
        public void ApplyBodyColor(Color fill)
        {
            var linear = fill.linear;
            bool dark = .2126f * linear.r + .7152f * linear.g + .0722f * linear.b < .179f;
            var muted = dark ? new Color32(210, 211, 215, 255) : new Color32(80, 80, 80, 255);
            GetComponent<VenueShapeGraphic>().color = fill;
            description.color = muted;
            if (doubleColumn) identifier.color = muted;
            else
            {
                access.color = muted;
                status.color = dark ? Color.white : (Color)new Color32(17, 17, 17, 255);
            }
            metric.color = dark ? Color.white : defaultMetricColor;
            metricCaption.color = muted;
        }
        public void Bind(VenueEntry entry, System.DateTime today, bool selected)
        {
            entryId = entry.event_id;
            title.text = entry.name;
            description.text = string.IsNullOrEmpty(entry.description) ? "暂无介绍" : entry.description;
            status.text = entry.PhaseLabel(today);
            access.text = "加入方式：" + entry.AccessLabel;
            identifier.text = entry.event_id;
            if (entry.Kind == VenueKind.Base)
            {
                metric.text = entry.member_count < 0 ? "—" : entry.member_count.ToString("N0");
                metricCaption.text = "成员";
                metricCaption.gameObject.SetActive(true);
                if (dateMonth) dateMonth.text = entry.is_registered ? "已加入" : "";
            }
            else
            {
                metric.text = entry.StartLabel;
                metricCaption.text = "";
                metricCaption.gameObject.SetActive(false);
                if (dateMonth) dateMonth.text = "";
            }
            actionText.text = entry.Kind == VenueKind.Base ? "进入基地" : "进入赛事";
            if (selectionMark) selectionMark.gameObject.SetActive(selected);
        }
    }
}
