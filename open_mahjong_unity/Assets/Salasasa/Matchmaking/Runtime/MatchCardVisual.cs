using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Salasasa.MatchLab;

namespace Salasasa.Matchmaking
{
    public sealed class MatchCardVisual : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler, IPointerUpHandler
    {
        public MatchButton entry;
        public Graphic surface,selection;
        public TMP_Text title,population,status;
        public MatchShape icon;
        public Color accent,ink,normal=Color.white;
        bool ready,inside,pressed,selected,available,pending,leaving;
        void LateUpdate()
        {
            var n=MatchNetworkManager.Instance;
            bool p=n!=null&&n.IsQueuePending(entry.QueueType),l=n!=null&&n.IsLeavePending;
            if(!ready||selected!=entry.IsSelected||available!=entry.AdmissionAvailable||pending!=p||leaving!=l){selected=entry.IsSelected;available=entry.AdmissionAvailable;pending=p;leaving=l;Refresh();}
        }
        void Refresh()
        {
            ready=true;surface.color=selected?accent:normal;
            if(inside)surface.color=Color.Lerp(surface.color,selected?ink:accent,pressed?.16f:.08f);
            title.color=selected?ink:new Color32(37,45,50,255);
            population.color=selected?ink:new Color32(81,93,101,255);
            if(status){status.text=selected?(pending?(leaving?"取消中":"加入中"):"匹配中"):available?"":"未解锁";status.color=population.color;}
            icon.gameObject.SetActive(selected||!available);icon.form=selected?MatchShape.Form.Check:MatchShape.Form.Lock;icon.color=population.color;icon.SetVerticesDirty();
            selection.color=ink;
        }
        public void OnPointerEnter(PointerEventData e){inside=true;Refresh();}
        public void OnPointerExit(PointerEventData e){inside=pressed=false;Refresh();}
        public void OnPointerDown(PointerEventData e){pressed=e.button==PointerEventData.InputButton.Left;Refresh();}
        public void OnPointerUp(PointerEventData e){pressed=false;Refresh();}
        void OnEnable(){ready=false;}
        void OnDisable(){ready=false;inside=pressed=false;}
    }
}
