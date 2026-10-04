using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public partial class TipsContainer {
    private GameObject guangdongHintPager;
    private TMP_Text guangdongHintPageText;
    private Button guangdongHintPrevious, guangdongHintNext;
    private int guangdongHintPage, guangdongHintCount = -1;
    private void UpdateGuangdongHintPages() {
        const int perPage = 10;
        bool active = GuangdongMilRules.IsMil(_recordTipsContext?.SubRule ?? GameSession.Current.SubRule);
        int count = TileContainer.transform.childCount;
        bool paged = active && count > perPage;
        if (guangdongHintCount != count) { guangdongHintCount = count; guangdongHintPage = 0; }
        int pages = Mathf.Max(1, (count + perPage - 1) / perPage);
        guangdongHintPage = Mathf.Clamp(guangdongHintPage, 0, pages - 1);
        foreach (Transform row in new[] {TileContainer.transform, FanContainer.transform}) {
            for (int i = 0; i < row.childCount; i++) row.GetChild(i).gameObject.SetActive(!paged || i / perPage == guangdongHintPage);
        }
        if (!paged) { if (guangdongHintPager != null) guangdongHintPager.SetActive(false); return; }
        if (guangdongHintPager == null) {
            guangdongHintPager = new GameObject("GuangdongWaitPages", typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
            guangdongHintPager.layer = gameObject.layer;
            guangdongHintPager.transform.SetParent(transform, false);
            // The host layout preserves child sizes and aligns right. Reserve the complete button row.
            ((RectTransform)guangdongHintPager.transform).sizeDelta = new Vector2(302,33);
            var layout = guangdongHintPager.GetComponent<HorizontalLayoutGroup>();
            layout.childAlignment = TextAnchor.MiddleCenter; layout.spacing = 12;
            layout.childControlHeight = layout.childControlWidth = false;
            layout.childForceExpandHeight = layout.childForceExpandWidth = false;
            guangdongHintPager.GetComponent<LayoutElement>().preferredHeight = 33;
            TMP_FontAsset font = FanContainer.GetComponentsInChildren<TMP_Text>(true).FirstOrDefault(t=>t.font!=null)?.font ?? TMP_Settings.defaultFontAsset;
            guangdongHintPrevious = GuangdongHintPageButton("Previous", "上一页", font);
            guangdongHintNext = GuangdongHintPageButton("Next", "下一页", font);
            var label = new GameObject("Page", typeof(RectTransform), typeof(TextMeshProUGUI));
            label.layer = gameObject.layer; label.transform.SetParent(guangdongHintPager.transform, false); label.transform.SetSiblingIndex(1);
            guangdongHintPageText = label.GetComponent<TextMeshProUGUI>();
            guangdongHintPageText.font = font; guangdongHintPageText.fontSize = 18; guangdongHintPageText.color = Color.white;
            guangdongHintPageText.alignment = TextAlignmentOptions.Center; guangdongHintPageText.raycastTarget = false;
            guangdongHintPageText.rectTransform.sizeDelta = new Vector2(90,33);
            guangdongHintPrevious.onClick.AddListener(()=>{guangdongHintPage--;UpdateContainerSize();});
            guangdongHintNext.onClick.AddListener(()=>{guangdongHintPage++;UpdateContainerSize();});
        }
        guangdongHintPager.SetActive(true); guangdongHintPager.transform.SetAsLastSibling();
        guangdongHintPageText.text = $"{guangdongHintPage+1}/{pages}";
        guangdongHintPrevious.interactable = guangdongHintPage > 0;
        guangdongHintNext.interactable = guangdongHintPage+1 < pages;
    }
    private Button GuangdongHintPageButton(string name, string caption, TMP_FontAsset font) {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
        go.layer = gameObject.layer; go.transform.SetParent(guangdongHintPager.transform, false);
        ((RectTransform)go.transform).sizeDelta = new Vector2(94,33); go.GetComponent<Image>().color = new Color32(70,86,126,255);
        var label = new GameObject("Caption", typeof(RectTransform), typeof(TextMeshProUGUI));
        label.layer = gameObject.layer; label.transform.SetParent(go.transform,false);
        var text=label.GetComponent<TextMeshProUGUI>(); text.text=caption; text.font=font; text.fontSize=18; text.color=Color.white;
        text.alignment=TextAlignmentOptions.Center; text.raycastTarget=false;
        text.rectTransform.anchorMin=Vector2.zero; text.rectTransform.anchorMax=Vector2.one; text.rectTransform.offsetMin=text.rectTransform.offsetMax=Vector2.zero;
        return go.GetComponent<Button>();
    }
}
