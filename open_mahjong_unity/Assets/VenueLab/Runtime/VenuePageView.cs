using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Salasasa.Venues
{
    public sealed class VenuePageView : MonoBehaviour
    {
        public VenueKind kind;
        public int themeIndex;
        public bool warm, doubleColumn;
        public TMP_InputField search;
        public TMP_Dropdown sort;
        public Button clearSearch, create, previous, next;
        public Button[] filters;
        public TMP_Text resultCount, pagination, sortText, emptyTitle;
        public GameObject emptyState;
        public VenueCardView[] cards;
        public Color selectedColor, idleColor, selectedTextColor, idleTextColor;
        void OnEnable()
        {
            AlignSearchText();
            RefreshSortOptions();
            ConfigManager.OnLanguageChanged += RefreshSortOptions;
        }
        void OnDisable() { ConfigManager.OnLanguageChanged -= RefreshSortOptions; }
        public void RefreshSortOptions()
        {
            if (!sort) return;
            int selected = sort.value;
            sort.options = VenueQuery.SortLabels(kind).Select(key => new TMP_Dropdown.OptionData(PlatformText.Get(key))).ToList();
            ShowSort(selected);
        }
        public void ShowSort(int index)
        {
            if (!sort) return;
            sort.SetValueWithoutNotify(Mathf.Clamp(index, 0, 2));
            sort.RefreshShownValue();
        }
        public void AlignSearchText()
        {
            if (!search || !search.textViewport || !search.textComponent) return;
            var viewport=search.textViewport;
            viewport.anchorMin=Vector2.zero;viewport.anchorMax=Vector2.one;
            viewport.offsetMin=new Vector2(52,6);viewport.offsetMax=new Vector2(-42,-6);
            foreach(var text in new TMP_Text[]{search.textComponent,search.placeholder as TMP_Text})
            {
                if(!text)continue;
                var rect=text.rectTransform;rect.anchorMin=Vector2.zero;rect.anchorMax=Vector2.one;
                rect.offsetMin=rect.offsetMax=Vector2.zero;rect.localScale=Vector3.one;
                text.margin=Vector4.zero;text.alignment=TextAlignmentOptions.Left;
            }
            search.lineType=TMP_InputField.LineType.SingleLine;
            search.caretWidth=2;search.ForceLabelUpdate();
        }
        public void ShowFilter(int index)
        {
            for (int i = 0; i < filters.Length; i++)
            {
                filters[i].targetGraphic.color = i == index ? selectedColor : idleColor;
                filters[i].GetComponentInChildren<TMP_Text>().color = i == index ? selectedTextColor : idleTextColor;
            }
        }
    }
}
