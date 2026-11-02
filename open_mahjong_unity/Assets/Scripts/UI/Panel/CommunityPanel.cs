using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace Salasasa.Community {
    [DefaultExecutionOrder(210)]
    public sealed class CommunityPanel : MonoBehaviour {
        public const float CompactFrameWidth=1536, FrameWidthRatio=.8f, VerticalInset=32, CardGap=16;
        public PlatformContentClient contentClient;
        public CommunityAppearance appearance;
        public CommunityQrDialog qrDialog;
        public RectTransform items;
        public CommunityCard itemTemplate;
        public CommunityCard[] bakedCards;
        public UnityEngine.UI.ScrollRect scroll;
        public UnityEngine.UI.Button refresh, close;
        public UnityEngine.UI.Image surface,header,headerLine;
        public TMP_Text title,status,empty,refreshLabel;
        public List<CommunityCard> Cards { get; } = new List<CommunityCard>();
        readonly List<CommunityCard> pool=new List<CommunityCard>();
        int shownRevision=-1;
        Vector2 lastSize,lastPageSize;
        float nextAvailability;
        void Awake(){pool.AddRange(bakedCards??Array.Empty<CommunityCard>());if(refresh)refresh.onClick.AddListener(contentClient.Refresh);if(close)close.onClick.AddListener(Close);}
        void OnEnable(){contentClient.EnsureLoaded();contentClient.Changed+=Render;Render();contentClient.Refresh();}
        void OnDisable(){contentClient.Changed-=Render;if(qrDialog&&qrDialog.gameObject.activeSelf)qrDialog.Close();}
        void OnDestroy(){if(refresh)refresh.onClick.RemoveListener(contentClient.Refresh);}
        void Close()=>UnityEngine.Object.FindAnyObjectByType<Salasasa.Home.CompleteLobbyController>()?.OpenHome();
        void Render(){
            status.text=contentClient.Status;if(refresh)refresh.interactable=!contentClient.Busy;
            var document=contentClient.Document;if(document==null)return;
            if(shownRevision!=document.revision){
                shownRevision=document.revision;Cards.Clear();
                for(int i=0;i<document.communities.Length;i++){
                    if(i>=pool.Count)pool.Add(Instantiate(itemTemplate,items));
                    var card=pool[i];card.gameObject.SetActive(true);card.name="CommunityItem_"+document.communities[i].id;card.Bind(document.communities[i],this);Cards.Add(card);
                }
                for(int i=document.communities.Length;i<pool.Count;i++)pool[i].gameObject.SetActive(false);
                empty.gameObject.SetActive(Cards.Count==0);scroll.verticalNormalizedPosition=1;
            }
            foreach(var card in Cards)card.RefreshAvailability();
            RefreshAppearance();
        }
        public void RefreshAppearance(){
            if(!appearance||appearance.Current==null)return;
            var p=appearance.Current;surface.color=p.surface;header.color=p.header;headerLine.color=p.line;title.color=p.text;status.color=empty.color=p.muted;if(refresh)refresh.image.color=p.accent;if(refreshLabel)refreshLabel.color=p.actionText;
            foreach(var card in Cards)card.Apply(p);
            Layout();
        }
        void LateUpdate(){
            if(scroll&&scroll.viewport&&(lastPageSize!=((RectTransform)transform).rect.size||lastSize!=scroll.viewport.rect.size))Layout();
            if(Time.unscaledTime>=nextAvailability){nextAvailability=Time.unscaledTime+1;foreach(var card in Cards)card.RefreshAvailability();}
        }
        public void Layout(){
            if(!scroll||!scroll.viewport||!items||!surface)return;
            lastPageSize=((RectTransform)transform).rect.size;
            var frame=(RectTransform)surface.transform.parent;
            frame.anchorMin=new Vector2(.5f,0);frame.anchorMax=new Vector2(.5f,1);frame.pivot=new Vector2(.5f,.5f);
            frame.sizeDelta=new Vector2(Mathf.Min(CompactFrameWidth,lastPageSize.x*FrameWidthRatio),-2*VerticalInset);
            frame.anchoredPosition=Vector2.zero;frame.localScale=Vector3.one;
            lastSize=scroll.viewport.rect.size;float width=lastSize.x;
            if(width<=0)return;
            int columns=width>=2*CommunityCard.MinimumWidth+CardGap?2:1;
            float cellWidth=(width-CardGap*(columns-1))/columns;
            int rows=Mathf.CeilToInt(Cards.Count/(float)columns);
            var heights=new float[rows];
            for(int i=0;i<Cards.Count;i++)heights[i/columns]=Mathf.Max(heights[i/columns],Cards[i].FitContent(cellWidth));
            float y=0;
            for(int i=0;i<Cards.Count;i++){
                if(i>0&&i%columns==0)y+=heights[i/columns-1]+CardGap;
                var r=(RectTransform)Cards[i].transform;r.anchorMin=r.anchorMax=new Vector2(0,1);r.pivot=new Vector2(0,1);r.localScale=Vector3.one;r.sizeDelta=new Vector2(cellWidth,heights[i/columns]);r.anchoredPosition=new Vector2((i%columns)*(cellWidth+CardGap),-y);
            }
            items.sizeDelta=new Vector2(0,rows>0?y+heights[rows-1]:0);
        }
    }
}
