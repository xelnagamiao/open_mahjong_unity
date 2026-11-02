using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;

namespace Salasasa.DataLab
{
    public enum RecentCardStyle { TierHeader, FullTint, AccentRail }

    public sealed class RecentMatchCard:MonoBehaviour,IPointerEnterHandler,IPointerExitHandler,IPointerDownHandler,IPointerUpHandler
    {
        public RecentCardStyle style;
        public UnityEngine.UI.Button button;
        public UnityEngine.UI.Image frame,header,body,rail,action;
        public UnityEngine.UI.Image[] playerDividers;
        public TMP_Text scene,mode,date;
        public TMP_Text[] players;
        public int layoutIndex = -1;
        public TMP_Text[] ranks = System.Array.Empty<TMP_Text>(), scores = System.Array.Empty<TMP_Text>();
        public UnityEngine.UI.Graphic arrow;
        Color frameColor,headerColor,bodyColor,accentColor,hoverColor;
        bool hovered,pressed;
        int seatCount=4;

        public void Bind(MatchResult match)
        {
            seatCount=Mathf.Clamp(match.seats.Count,1,players.Length);
            for(int i=0;i<players.Length;i++){
                bool visible=i<match.seats.Count;
                players[i].text=visible?(string.IsNullOrWhiteSpace(match.seats[i].name)?"—":match.seats[i].name):"";
                if(layoutIndex<3)continue;
                players[i].gameObject.SetActive(visible);
                ranks[i].gameObject.SetActive(visible);
                ranks[i].text=visible?match.seats[i].RankLabel(match.seats.Count):"";
                scores[i].gameObject.SetActive(visible&&layoutIndex==5);
                scores[i].text=visible?match.seats[i].ScoreLabel:"";
            }
            RefreshGeometry();
        }

        void OnRectTransformDimensionsChange(){RefreshGeometry();}
        public void RefreshGeometry()
        {
            if(layoutIndex<0||layoutIndex>5||!body||!header||!scene||!mode||!date||!action||!arrow
                ||players==null||players.Length!=4)return;
            var root=(RectTransform)transform;float w=root.rect.width,h=root.rect.height;
            if(w<=0||h<=0)return;
            scene.margin=new Vector4(0,0,8,0);mode.margin=new Vector4(0,0,8,0);
            if(layoutIndex<3){RefreshLegacyGeometry(w,h);return;}
            if(ranks==null||ranks.Length!=4||scores==null||scores.Length!=4)return;
            float side=layoutIndex==5?142:0,top=layoutIndex==5?0:28;
            Place(header.rectTransform,1,1,layoutIndex==5?side:w-2,layoutIndex==5?h-2:top-1);
            float bodyTop = layoutIndex == 5 ? 1 : top;
            Place(body.rectTransform,side+1,bodyTop,w-side-2,h-bodyTop-1);
            Place(rail.rectTransform,1,1,3,h-2);
            if(layoutIndex==5){
                Place(scene.rectTransform,12,3,side-20,24);
                Place(mode.rectTransform,12,30,side-20,24);
                Place(date.rectTransform,12,h-28,side-20,24);
                Place(action.rectTransform,w-28,(h-24)*.5f,24,24);
            }else{
                Place(scene.rectTransform,12,0,90,28);Place(mode.rectTransform,108,0,112,28);
                Place(date.rectTransform,w-198,0,156,28);Place(action.rectTransform,w-30,3,24,22);
            }
            Place(arrow.rectTransform,7,5,10,12);
            float bw=w-side-2-(layoutIndex==5?30:0),bh=h-top-2;
            for(int i=0;i<4;i++){
                if(!ranks[i]||!scores[i]||!players[i])continue;
                if(layoutIndex==4){
                    float cw=(seatCount==3&&i==2)?bw:bw*.5f;
                    float x=i%2*bw*.5f,y=i/2*bh*.5f;
                    Place(ranks[i].rectTransform,x+10,y,58,bh*.5f);
                    Place(players[i].rectTransform,x+72,y,cw-82,bh*.5f);
                    FitPlayerLine(ranks[i],15);
                    FitPlayerLine(players[i],16);
                }else{
                    float cw=bw/seatCount,x=i*cw;
                    Place(ranks[i].rectTransform,x+8,layoutIndex==5?2:0,cw-16,24);
                    Place(players[i].rectTransform,x+8,layoutIndex==5?27:24,cw-16,layoutIndex==5?28:bh-24);
                    Place(scores[i].rectTransform,x+8,h-27,cw-16,24);
                }
            }
            for(int i=0;i<playerDividers.Length;i++){
                var divider=playerDividers[i];
                if(layoutIndex==4){
                    divider.gameObject.SetActive(i==0||i==1&&seatCount>2);
                    if(i==0)Place(divider.rectTransform,bw*.5f,5,1,seatCount==3?bh*.5f-10:bh-10);
                    else Place(divider.rectTransform,10,bh*.5f,bw-20,1);
                }else{
                    divider.gameObject.SetActive(i<seatCount-1);
                    Place(divider.rectTransform,(i+1)*bw/seatCount,7,1,bh-14);
                }
            }
        }

        void RefreshLegacyGeometry(float w,float h)
        {
            Place(rail.rectTransform,0,0,4,h);
            Place(action.rectTransform,w-35,layoutIndex==2?3:5,28,layoutIndex==2?24:28);
            Place(arrow.rectTransform,7,layoutIndex==2?5:7,14,14);
            if(layoutIndex==1){
                Place(header.rectTransform,1.25f,1.25f,105.25f,h-2.5f);
                Place(body.rectTransform,106.5f,1.25f,w-107.75f,h-2.5f);
                Place(scene.rectTransform,14,8,82,32);Place(mode.rectTransform,14,h-34,82,24);
                Place(date.rectTransform,120,0,w-164,28);
                float bw=w-107.75f,bh=h-2.5f;
                for(int i=0;i<4;i++)Place(players[i].rectTransform,i%2*bw*.5f+12,i<2?27:bh*.5f+12,bw*.5f-22,bh*.5f-15);
                Place(playerDividers[0].rectTransform,bw*.5f,32,1,bh-40);
            }else{
                float split=layoutIndex==0?36:30,bw=w-2.5f,bh=h-split-2.5f;
                Place(header.rectTransform,1.25f,1.25f,w-2.5f,split);
                Place(body.rectTransform,1.25f,split+1.25f,bw,bh);
                Place(scene.rectTransform,14,2,88,split-3);Place(mode.rectTransform,108,2,112,split-3);
                Place(date.rectTransform,w-199,2,156,split-3);
                if(layoutIndex==0){
                    for(int i=0;i<4;i++)Place(players[i].rectTransform,i*bw*.25f+8,2,bw*.25f-16,bh-4);
                    for(int i=0;i<3;i++)Place(playerDividers[i].rectTransform,(i+1)*bw*.25f,9,1,bh-18);
                }else{
                    for(int i=0;i<4;i++)Place(players[i].rectTransform,i%2*bw*.5f+12,i/2*bh*.5f,bw*.5f-24,bh*.5f);
                    Place(playerDividers[0].rectTransform,bw*.5f,6,1,bh-12);
                    Place(playerDividers[1].rectTransform,12,bh*.5f-.5f,bw-24,1);
                }
            }
        }

        static void FitPlayerLine(TMP_Text label,float maximumSize)
        {
            // The two player rows can be shorter than the font's ascent/descent at compact resolutions.
            // Ellipsis otherwise removes the entire first line, even when the player name is present.
            label.enableAutoSizing=true;
            label.fontSizeMin=10;
            label.fontSizeMax=maximumSize;
            var localized=label.GetComponent<LocalizedPlatformLabel>();
            if(localized){
                localized.authoredAutoSize=true;
                localized.authoredSize=maximumSize;
                localized.minimumSize=10;
            }
        }

        static void Place(RectTransform rect,float x,float y,float width,float height)
        {
            rect.anchorMin=rect.anchorMax=rect.pivot=Vector2.up;
            rect.anchoredPosition=new Vector2(x,-y);rect.sizeDelta=new Vector2(Mathf.Max(0,width),Mathf.Max(0,height));
        }

        public static Color Accent(string tier)
        {
            switch(tier){
                case "beginner":case "elo":return new Color32(85,144,98,255);
                case "intermediate":return new Color32(56,162,176,255);
                case "advanced":return new Color32(255,143,0,255);
                case "mcrpl":return new Color32(255,3,0,255);
                case "events":return new Color32(191,110,173,255);
                default:return new Color32(144,144,144,255);
            }
        }
        public void Present(MatchResult match,HomePalette palette)
        {
            string tier=match.room=="events"?"events":match.tier;
            accentColor=Accent(tier);
            var colors=palette.colors;
            Color panel=colors[(int)HomeColor.Panel],cell=colors[(int)HomeColor.Cell],line=colors[(int)HomeColor.Line];
            int slot=tier=="beginner"||tier=="elo"?0:tier=="intermediate"?1:tier=="advanced"?2:tier=="mcrpl"?3:tier=="events"?4:5;
            Color tierFill=palette.tierSurfaces!=null&&palette.tierSurfaces.Length==6?palette.tierSurfaces[slot]:Color.Lerp(panel,accentColor,.3f);
            headerColor=style==RecentCardStyle.AccentRail?colors[(int)HomeColor.Button]:tierFill;
            bodyColor=cell;
            frameColor=line;hoverColor=colors[(int)HomeColor.Muted];
            rail.color=accentColor;rail.gameObject.SetActive(style!=RecentCardStyle.FullTint);
            Color headerInk=style==RecentCardStyle.AccentRail?colors[(int)HomeColor.Ink]:ReadableInk(tierFill);
            Color playerInk=colors[(int)HomeColor.Ink];
            action.color=style==RecentCardStyle.FullTint?bodyColor:headerColor;
            arrow.color=style==RecentCardStyle.FullTint?playerInk:headerInk;
            scene.color=mode.color=headerInk;
            date.color=style==RecentCardStyle.FullTint?playerInk:headerInk;
            foreach(var text in players)text.color=playerInk;
            foreach(var text in ranks)if(text)text.color=playerInk;
            foreach(var text in scores)if(text)text.color=playerInk;
            foreach(var divider in playerDividers)divider.color=new Color(playerInk.r,playerInk.g,playerInk.b,.18f);
            ApplyState();
        }
        public static Color ReadableInk(Color background)
        {
            Color light=new Color32(240,240,237,255),dark=new Color32(39,41,42,255);
            float lightContrast=Contrast(light,background),darkContrast=Contrast(dark,background);
            if(Mathf.Max(lightContrast,darkContrast)>=4.5f)return lightContrast>darkContrast?light:dark;
            return Contrast(Color.white,background)>Contrast(Color.black,background)?Color.white:Color.black;
        }
        static float Contrast(Color a,Color b){float x=Luminance(a),y=Luminance(b);return (Mathf.Max(x,y)+.05f)/(Mathf.Min(x,y)+.05f);}
        static float Luminance(Color c){c=c.linear;return c.r*.2126f+c.g*.7152f+c.b*.0722f;}
        void ApplyState()
        {
            frame.color=hovered||pressed?hoverColor:frameColor;
            header.color=headerColor;body.color=bodyColor;
        }
        public void OnPointerEnter(PointerEventData e){hovered=true;ApplyState();}
        public void OnPointerExit(PointerEventData e){hovered=false;pressed=false;ApplyState();}
        public void OnPointerDown(PointerEventData e){pressed=true;ApplyState();}
        public void OnPointerUp(PointerEventData e){pressed=false;ApplyState();}
        void OnDisable(){hovered=false;pressed=false;}
    }
}
