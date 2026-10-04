using System;
using System.Linq;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>公开杭州状态；不显示任何他家私密手牌或听牌提示。</summary>
public sealed class HangzhouStatePanel : MonoBehaviour {
    private static HangzhouStatePanel instance;
    private TMP_Text caption, body;
    private HangzhouInfo info;
    private bool expanded;
    public static void Show(HangzhouInfo value) {
        if (value == null || GameCanvas.Instance == null) { Hide(); return; }
        if (instance == null) {
            var root=new GameObject("HangzhouState",typeof(RectTransform),typeof(Image),typeof(Canvas),typeof(GraphicRaycaster),typeof(HangzhouStatePanel));
            root.transform.SetParent(GameCanvas.Instance.transform,false); root.layer=GameCanvas.Instance.gameObject.layer;
            // Share the table UI's ordering so the existing result content
            // naturally covers this HUD, and hiding that content reveals it.
            Transform presentation=RoundEndPresentation.Instance?.transform;
            while (presentation!=null && presentation.parent!=root.transform.parent) presentation=presentation.parent;
            if (presentation!=null) root.transform.SetSiblingIndex(presentation.GetSiblingIndex());
            else root.transform.SetAsFirstSibling();
            root.GetComponent<Canvas>().overrideSorting=false;
            instance=root.GetComponent<HangzhouStatePanel>(); instance.Build();
        }
        if (instance.info?.hand_number != value.hand_number) instance.expanded=false;
        instance.info=value; instance.gameObject.SetActive(true); instance.Refresh();
    }
    public static void Hide() { if (instance != null) { instance.gameObject.SetActive(false); instance.info=null; } }
    public static string Describe(HangzhouInfo value) {
        if (value == null) return "";
        var text=new StringBuilder();
        text.AppendLine($"白板财神 · {Math.Max(1,value.dealer_streak)}老庄 ×{Math.Max(2,value.dealer_multiplier)}");
        int Count(int[] values,int i) => values!=null && i<values.Length ? values[i] : 0;
        string Seat(int i) { if(i<0 || i>3) return "?"; int original=value.seat_to_original!=null && i<value.seat_to_original.Length ? value.seat_to_original[i] : i;return new[]{"东起","南起","西起","北起"}[Mathf.Clamp(original,0,3)]; }
        for (int seat=0;seat<4;seat++) {
            string label=Seat(seat);
            bool locked=value.forced_draw_discard!=null && seat<value.forced_draw_discard.Length && value.forced_draw_discard[seat];
            text.AppendLine($"{label} · 财飘{Count(value.cai_piao_counts,seat)} · 十风{Count(value.ten_winds_counts,seat)}/10 · 吃{Count(value.chi_counts,seat)}{(locked ? " · 仅摸切" : "")}");
            if (Count(value.chi_counts,seat)>=3) text.AppendLine($"  {label}和：{Seat((seat+3)%4)}全包；{Seat((seat+3)%4)}和：{label}双倍全包");
        }
        var payment=value.ledger?["payment"] as Newtonsoft.Json.Linq.JObject;
        if (payment?["transfers"] is Newtonsoft.Json.Linq.JArray transfers) {
            text.AppendLine("支付明细");
            foreach (var transfer in transfers.OfType<Newtonsoft.Json.Linq.JObject>()) {
                int payer=(int?)transfer["payer"] ?? -1,winner=(int?)transfer["winner"] ?? -1;
                string reason=(string)transfer["reason"] ?? "";
                if ((string)value.ledger?["source"]=="ten_winds" && reason=="自摸") reason="十风";
                text.AppendLine($"{Seat(payer)}付{Seat(winner)} {transfer["points"]}分 · {reason}");
            }
        }
        var score=value.ledger?["score"] as Newtonsoft.Json.Linq.JObject;
        var decomposition=score?["decomposition"] as Newtonsoft.Json.Linq.JObject;
        if (decomposition?["joker_uses"] is Newtonsoft.Json.Linq.JArray uses && uses.Count>0) {
            string Tile(int id) => id>=41 && id<=47 ? new[]{"东","南","西","北","中","白","发"}[id-41] : $"{id%10}{(id/10==1 ? "万" : id/10==2 ? "饼" : "条")}";
            text.AppendLine("财神替代："+string.Join("、",uses.OfType<Newtonsoft.Json.Linq.JObject>().Select(use=>Tile((int?)use["logical"] ?? 0))));
        }
        text.AppendLine($"杠后暗弃{value.burned_count}张");
        if (value.phase=="waiting_hangzhou_ten_winds") text.AppendLine("十风和牌选择");
        return text.ToString().TrimEnd();
    }
    private void Refresh() {
        caption.text=$"白板财神 · {Math.Max(1,info.dealer_streak)}老庄 · {(expanded ? "收起" : "详情")}";
        body.text=Describe(info); body.gameObject.SetActive(expanded);
        ((RectTransform)transform).sizeDelta=new Vector2(410,expanded ? Mathf.Min(450,body.preferredHeight+62) : 36);
    }
    private void Build() {
        var rect=(RectTransform)transform; rect.anchorMin=rect.anchorMax=rect.pivot=new Vector2(0,1); rect.anchoredPosition=new Vector2(16,-185);
        GetComponent<Image>().color=new Color(.055f,.075f,.095f,.96f); GetComponent<Image>().raycastTarget=false;
        var font=GameCanvas.Instance.GetComponentsInChildren<TMP_Text>(true).FirstOrDefault(t=>t.font!=null)?.font ?? TMP_Settings.defaultFontAsset;
        var button=new GameObject("Toggle",typeof(RectTransform),typeof(Image),typeof(Button));button.transform.SetParent(transform,false);
        var buttonRect=(RectTransform)button.transform;buttonRect.anchorMin=new Vector2(0,1);buttonRect.anchorMax=new Vector2(1,1);buttonRect.pivot=new Vector2(.5f,1);buttonRect.sizeDelta=new Vector2(0,36);
        button.GetComponent<Image>().color=new Color(.15f,.28f,.25f);
        button.GetComponent<Button>().onClick.AddListener(()=>{expanded=!expanded;Refresh();});
        caption=Label(button.transform,"Caption",font);caption.alignment=TextAlignmentOptions.Center;caption.fontSize=18;
        caption.rectTransform.anchorMin=Vector2.zero;caption.rectTransform.anchorMax=Vector2.one;caption.rectTransform.offsetMin=caption.rectTransform.offsetMax=Vector2.zero;
        body=Label(transform,"Details",font);body.fontSize=18;body.alignment=TextAlignmentOptions.TopLeft;body.textWrappingMode=TextWrappingModes.Normal;
        body.rectTransform.anchorMin=Vector2.zero;body.rectTransform.anchorMax=Vector2.one;body.rectTransform.offsetMin=new Vector2(12,10);body.rectTransform.offsetMax=new Vector2(-12,-44);
    }
    private static TMP_Text Label(Transform parent,string name,TMP_FontAsset font) {
        var go=new GameObject(name,typeof(RectTransform),typeof(TextMeshProUGUI));go.transform.SetParent(parent,false);
        var text=go.GetComponent<TextMeshProUGUI>();text.font=font;text.raycastTarget=false;text.color=new Color(.96f,.93f,.82f);return text;
    }
}
