using System;
using System.Linq;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>十六张拉踢的公开账本。身份用开局座位标识，随轮庄保留。</summary>
public sealed class HongKongLedgerPanel : MonoBehaviour {
    private static HongKongLedgerPanel instance;
    private TMP_Text text, caption;
    private Button cut;
    private HongKongInfo info;
    private bool expanded, canCut;

    public static void Show(HongKongInfo info,bool canCut) {
        if (info?.base_hand_tiles!=16 || GameCanvas.Instance==null) { Hide(); return; }
        if (instance==null) {
            var root=new GameObject("HongKongPullLedger",typeof(RectTransform),typeof(Image),typeof(HongKongLedgerPanel));
            root.transform.SetParent(GameCanvas.Instance.transform,false);
            root.transform.SetAsFirstSibling();
            instance=root.GetComponent<HongKongLedgerPanel>();
            instance.Build();
        }
        instance.info=info;
        instance.canCut=canCut;
        if (canCut) instance.expanded=true;
        instance.gameObject.SetActive(true);
        instance.Refresh();
    }

    private void Refresh() {
        int count=info.pulls?.Length??0;
        caption.text=$"拉踢账本 · {count}笔 {(expanded?"收起":"展开")}";
        var body=new StringBuilder();
        if (info.dealer_streak>0) body.AppendLine($"连庄 {info.dealer_streak}");
        if (count==0) body.Append("暂无欠分");
        else foreach (var debt in info.pulls) body.AppendLine($"{Identity(debt.debtor)}欠{Identity(debt.creditor)} {debt.points}分 / {debt.mouths}口");
        text.text=body.ToString().TrimEnd();
        text.gameObject.SetActive(expanded);
        cut.gameObject.SetActive(expanded && canCut);
        ((RectTransform)transform).sizeDelta=new Vector2(270,expanded ? 60+Math.Max(1,count)*26+(info.dealer_streak>0?26:0)+(canCut?42:0) : 36);
    }

    private static string Identity(int index) => new[] {"东起","南起","西起","北起"}[Mathf.Clamp(index,0,3)];
    public static void Hide() {
        if (instance!=null) { instance.gameObject.SetActive(false); instance.expanded=false; }
    }

    private void Build() {
        var rect=(RectTransform)transform;
        rect.anchorMin=rect.anchorMax=new Vector2(0,1); rect.pivot=new Vector2(0,1);
        // Keep the expanded ledger clear of the left player's avatar/name.
        rect.anchoredPosition=new Vector2(320,-185);
        var background=GetComponent<Image>(); background.color=new Color(0.055f,0.075f,0.095f,0.94f); background.raycastTarget=false;
        var font=GameCanvas.Instance.GetComponentsInChildren<TMP_Text>(true).FirstOrDefault(t=>t.font!=null)?.font ?? TMP_Settings.defaultFontAsset;
        var toggleObject=new GameObject("ToggleLedger",typeof(RectTransform),typeof(Image),typeof(Button));
        toggleObject.transform.SetParent(transform,false);
        var toggleRect=(RectTransform)toggleObject.transform;
        toggleRect.anchorMin=toggleRect.anchorMax=new Vector2(0.5f,1); toggleRect.pivot=new Vector2(0.5f,1);
        toggleRect.anchoredPosition=new Vector2(0,-2); toggleRect.sizeDelta=new Vector2(266,32);
        toggleObject.GetComponent<Image>().color=new Color(0.22f,0.31f,0.38f);
        caption=Label("LedgerCaption",font); caption.transform.SetParent(toggleObject.transform,false);
        caption.fontSize=18; caption.alignment=TextAlignmentOptions.Center;
        caption.rectTransform.anchorMin=Vector2.zero; caption.rectTransform.anchorMax=Vector2.one;
        caption.rectTransform.offsetMin=caption.rectTransform.offsetMax=Vector2.zero;
        toggleObject.GetComponent<Button>().onClick.AddListener(()=> { expanded=!expanded; Refresh(); });
        text=Label("Debts",font); text.fontSize=20; text.color=new Color(0.95f,0.89f,0.71f);
        text.alignment=TextAlignmentOptions.TopLeft; text.textWrappingMode=TextWrappingModes.NoWrap;
        var tr=text.rectTransform; tr.anchorMin=Vector2.zero; tr.anchorMax=Vector2.one; tr.offsetMin=new Vector2(14,12); tr.offsetMax=new Vector2(-12,-44);
        var buttonObject=new GameObject("CutThreeMouthPulls",typeof(RectTransform),typeof(Image),typeof(Button));
        buttonObject.transform.SetParent(transform,false); cut=buttonObject.GetComponent<Button>();
        var br=(RectTransform)buttonObject.transform; br.anchorMin=br.anchorMax=new Vector2(0.5f,0); br.pivot=new Vector2(0.5f,0); br.anchoredPosition=new Vector2(0,10); br.sizeDelta=new Vector2(238,32);
        buttonObject.GetComponent<Image>().color=new Color(0.28f,0.37f,0.48f);
        var cutCaption=Label("Caption",font); cutCaption.transform.SetParent(buttonObject.transform,false);
        cutCaption.text="斩拉（结清三口以上欠分）"; cutCaption.fontSize=17; cutCaption.alignment=TextAlignmentOptions.Center;
        cutCaption.rectTransform.anchorMin=Vector2.zero; cutCaption.rectTransform.anchorMax=Vector2.one; cutCaption.rectTransform.offsetMin=cutCaption.rectTransform.offsetMax=Vector2.zero;
        cut.onClick.AddListener(()=>HongKongGameState.Active?.CutPull());
    }

    private TMP_Text Label(string name,TMP_FontAsset font) {
        var go=new GameObject(name,typeof(RectTransform),typeof(TextMeshProUGUI)); go.transform.SetParent(transform,false);
        var label=go.GetComponent<TextMeshProUGUI>(); label.font=font; label.raycastTarget=false; return label;
    }
}
