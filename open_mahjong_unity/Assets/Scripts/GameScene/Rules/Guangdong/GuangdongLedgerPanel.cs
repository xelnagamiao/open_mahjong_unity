using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;

/// <summary>用权威明细显示打鬼、鬼牌解释、奖马及分变；不重新随机翻马或重新计分。</summary>
public sealed class GuangdongLedgerPanel : MonoBehaviour {
    private static GuangdongLedgerPanel instance;
    private GuangdongPublicState state;
    private GuangdongResult result;
    private GuangdongHorses horses;
    private TMP_Text caption, body, pageText;
    private UnityEngine.UI.Button previous, next;
    private bool expanded;
    private int page;
    public static void Show(GuangdongPublicState state, GuangdongResult result, GuangdongHorses horses = null) {
        if (state == null && result == null && horses == null) { Hide(); return; }
        if (GameCanvas.Instance == null) return;
        if (instance == null) {
            var go = new GameObject("GuangdongLedger", typeof(RectTransform), typeof(UnityEngine.UI.Image), typeof(Canvas), typeof(UnityEngine.UI.GraphicRaycaster), typeof(GuangdongLedgerPanel));
            go.transform.SetParent(GameCanvas.Instance.transform, false); go.layer = GameCanvas.Instance.gameObject.layer;
            var canvas = go.GetComponent<Canvas>(); canvas.overrideSorting = true; canvas.sortingLayerID = SortingLayer.NameToID("PageUI"); canvas.sortingOrder = 6;
            instance = go.GetComponent<GuangdongLedgerPanel>(); instance.Build();
        }
        instance.state = state; instance.result = result; instance.horses = result?.horses ?? horses;
        instance.gameObject.SetActive(true); instance.Refresh();
    }
    public static void Hide() {
        if (instance == null) return;
        instance.gameObject.SetActive(false); instance.state = null; instance.result = null; instance.horses = null; instance.expanded = false; instance.page = 0;
    }
    private static string Seat(int seat) => new[] {"东位","南位","西位","北位"}[Mathf.Clamp(seat,0,3)];
    private static string Deltas(int[] values) => values == null ? "无" : string.Join("  ", values.Select((value,seat) => Seat(seat)+" "+value.ToString("+#;-#;0")));
    private void Refresh() {
        var lines = new List<string> {"花鬼留手，不补花"};
        if (state?.ghost_discard_counts?.Length == 4) lines.Add("打鬼  "+string.Join("  ",state.ghost_discard_counts.Select((count,seat)=>Seat(seat)+" "+count+"张")));
        foreach (var kong in state?.kong_ledger ?? new List<GuangdongKongEntry>()) {
            string kind=kong.kind=="concealed"?"暗杠":kong.kind=="added"?"加杠":"直杠";
            lines.Add(kind+(kong.tile.HasValue?" "+GuangdongMilRules.TileName(kong.tile.Value):"")+"："+Deltas(kong.changes));
        }
        if (result != null && !result.draw) {
            lines.Add($"{result.fan}番  基本分{result.base_score}分  系数×{result.coefficient}");
            foreach (var map in result.substitutions ?? Array.Empty<GuangdongSubstitution>())
                lines.Add($"{GuangdongMilRules.TileName(map.physical)} 代 {GuangdongMilRules.TileName(map.logical)}");
        }
        if (horses != null) {
            lines.Add("奖马顺序："+((horses.tiles?.Length ?? 0)==0 ? "无剩余牌" : string.Join("、",horses.tiles.Select(GuangdongMilRules.TileName))));
            lines.Add("中马 "+(horses.hits?.Sum()??0)+"张"+(horses.payer.HasValue?"  "+Seat(horses.payer.Value)+"责任支付":""));
            lines.Add("奖马分变："+Deltas(horses.changes));
        }
        if (result?.draw == true) lines.Add("流局退杠："+Deltas(result.refund_changes));
        if (result != null && !result.draw) { lines.Add("和牌（含奖马）："+Deltas(result.win_changes)); lines.Add("杠分净额："+Deltas(result.kong_changes)); }
        int pages = Math.Max(1,(lines.Count+3)/4); page=Mathf.Clamp(page,0,pages-1);
        caption.text="花鬼账目 · "+(expanded?"收起":"展开");
        body.text=string.Join("\n\n",lines.Skip(page*4).Take(4)); body.gameObject.SetActive(expanded);
        previous.gameObject.SetActive(expanded); next.gameObject.SetActive(expanded); pageText.gameObject.SetActive(expanded);
        previous.interactable=page>0; next.interactable=page+1<pages; pageText.text=$"{page+1}/{pages}";
        var rect=(RectTransform)transform;
        bool settled=result!=null;
        rect.anchorMin=rect.anchorMax=rect.pivot=new Vector2(0,settled?0:1);
        rect.anchoredPosition=settled?new Vector2(16,72):new Vector2(16,-185);
        rect.sizeDelta=new Vector2(430,expanded?340:36);
        GetComponent<UnityEngine.UI.Image>().raycastTarget=expanded;
    }
    private void Build() {
        var rect=(RectTransform)transform; rect.anchorMin=rect.anchorMax=rect.pivot=new Vector2(0,1); rect.anchoredPosition=new Vector2(16,-185);
        GetComponent<UnityEngine.UI.Image>().color=new Color(.055f,.075f,.095f,.99f);
        var font=GameCanvas.Instance.GetComponentsInChildren<TMP_Text>(true).FirstOrDefault(t=>t.font!=null)?.font??TMP_Settings.defaultFontAsset;
        var toggle=Button("Toggle",font,"",new Vector2(0,-2),new Vector2(426,32),true); caption=toggle.GetComponentInChildren<TMP_Text>(); toggle.onClick.AddListener(()=>{expanded=!expanded;Refresh();});
        body=Label("Entries",font,transform); body.alignment=TextAlignmentOptions.TopLeft; body.fontSize=18; body.textWrappingMode=TextWrappingModes.Normal; body.overflowMode=TextOverflowModes.Ellipsis;
        body.rectTransform.anchorMin=Vector2.zero; body.rectTransform.anchorMax=Vector2.one; body.rectTransform.offsetMin=new Vector2(12,44); body.rectTransform.offsetMax=new Vector2(-12,-43);
        previous=Button("Previous",font,"上一页",new Vector2(-99,8),new Vector2(104,30),false); next=Button("Next",font,"下一页",new Vector2(99,8),new Vector2(104,30),false);
        previous.onClick.AddListener(()=>{page--;Refresh();}); next.onClick.AddListener(()=>{page++;Refresh();});
        pageText=Label("Page",font,transform); pageText.rectTransform.anchorMin=pageText.rectTransform.anchorMax=pageText.rectTransform.pivot=new Vector2(.5f,0); pageText.rectTransform.anchoredPosition=new Vector2(0,8); pageText.rectTransform.sizeDelta=new Vector2(76,30);
    }
    private TMP_Text Label(string name,TMP_FontAsset font,Transform parent) {
        var go=new GameObject(name,typeof(RectTransform),typeof(TextMeshProUGUI)); go.layer=gameObject.layer; go.transform.SetParent(parent,false);
        var text=go.GetComponent<TextMeshProUGUI>(); text.font=font; text.color=new Color(.96f,.93f,.82f); text.fontSize=18; text.alignment=TextAlignmentOptions.Center; text.raycastTarget=false; return text;
    }
    private UnityEngine.UI.Button Button(string name,TMP_FontAsset font,string text,Vector2 position,Vector2 size,bool top) {
        var go=new GameObject(name,typeof(RectTransform),typeof(UnityEngine.UI.Image),typeof(UnityEngine.UI.Button)); go.layer=gameObject.layer; go.transform.SetParent(transform,false);
        var rect=(RectTransform)go.transform; rect.anchorMin=rect.anchorMax=rect.pivot=new Vector2(.5f,top?1:0); rect.anchoredPosition=position; rect.sizeDelta=size; go.GetComponent<UnityEngine.UI.Image>().color=new Color(.22f,.31f,.38f);
        var label=Label("Caption",font,go.transform); label.text=text; label.rectTransform.anchorMin=Vector2.zero; label.rectTransform.anchorMax=Vector2.one; label.rectTransform.offsetMin=label.rectTransform.offsetMax=Vector2.zero;
        return go.GetComponent<UnityEngine.UI.Button>();
    }
}
