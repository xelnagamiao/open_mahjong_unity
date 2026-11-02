using UnityEngine;
using UnityEngine.Events;
using TMPro;
namespace Salasasa.RightEntrances
{
    [System.Serializable] public sealed class EntranceRouteEvent : UnityEvent<string>{}
    public enum EntranceMetric { Matching, Rooms, Spectatable, VenueWaiting }
    [System.Serializable] public sealed class EntranceMetricBinding
    {
        public TMP_Text label;
        public EntranceMetric metric;
        public string format="{0}";
    }
    public sealed class EntranceRouter : MonoBehaviour
    {
        public EntranceButton[] buttons;
        public TMP_Text[] matchingCounts;
        public EntranceMetricBinding[] metrics;
        public EntranceRouteEvent routeRequested=new EntranceRouteEvent();
        public string SelectedRoute{get;private set;}="";
        public int MatchingCount{get;private set;}=-1;
        public int RoomCount{get;private set;}=-1;
        public int SpectatableCount{get;private set;}=-1;
        public int VenueWaitingCount{get;private set;}=-1;
        void OnEnable(){ConfigManager.OnLanguageChanged+=RefreshLanguage;RefreshLanguage();}
        void OnDisable(){ConfigManager.OnLanguageChanged-=RefreshLanguage;}
        void RefreshLanguage(){SetCounts(MatchingCount,RoomCount,SpectatableCount);SetVenueWaitingCount(VenueWaitingCount);}
        public void Request(string route){SelectedRoute=route;foreach(var b in buttons)b.SetSelected(b.route==route);routeRequested.Invoke(route);}
        public void Clear(){SelectedRoute="";foreach(var b in buttons)b.SetSelected(false);}
        public static string FormatCount(int value)=>value<0?"—":value>9999?"9999+":value.ToString();
        public int GetCount(EntranceMetric metric)=>metric==EntranceMetric.Matching?MatchingCount:metric==EntranceMetric.Rooms?RoomCount:metric==EntranceMetric.VenueWaiting?VenueWaitingCount:SpectatableCount;
        public void SetMatchingCount(int value)
        {
            MatchingCount=value;
            if(matchingCounts!=null)foreach(var t in matchingCounts)if(t)t.text=PlatformText.Format("匹配人数({0})",FormatCount(value));
            Apply(EntranceMetric.Matching,value);
        }
        public void SetRoomCount(int value){RoomCount=value;Apply(EntranceMetric.Rooms,value);}
        public void SetSpectatableCount(int value){SpectatableCount=value;Apply(EntranceMetric.Spectatable,value);}
        public void SetVenueWaitingCount(int value){VenueWaitingCount=value;Apply(EntranceMetric.VenueWaiting,value);}
        public void SetCounts(int matching,int rooms,int spectatable){SetMatchingCount(matching);SetRoomCount(rooms);SetSpectatableCount(spectatable);}
        void Apply(EntranceMetric metric,int value)
        {
            if(metrics==null)return;
            foreach(var binding in metrics)if(binding.metric==metric&&binding.label)
                binding.label.text=string.Format(PlatformText.Get(binding.format),FormatCount(value));
        }
    }
}

