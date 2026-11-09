using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Newtonsoft.Json.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.Networking;

namespace Salasasa.DataLab
{
    public sealed class HomeFanData
    {
        public readonly List<FanOccurrence> entries=new List<FanOccurrence>();
        public double wins;
        public bool supported;
        public string asOf;
        public static HomeFanData Read(string json,string scene)
        {
            var root=JObject.Parse(json);var data=PlayerData.Payload(json);
            var result=new HomeFanData {supported=(bool?)data["supports_fan_stats"]==true,asOf=(string)root["meta"]?["as_of_date"]};
            string tier=scene=="events"||scene=="rank"?"total":scene;
            var totals=(data["totals"] as JArray??new JArray()).FirstOrDefault(x=>(string)x["match_tier"]==tier);
            result.wins=(double?)totals?["win_count"]??0;
            if(!result.supported)return result;
            var counts=data["fans"]?[tier] as JObject;
            if(counts==null)return result;
            var dict=data["fan_dict"] as JObject;
            foreach(var item in counts.Properties())
            {
                if(!double.TryParse(item.Value.ToString(),NumberStyles.Any,CultureInfo.InvariantCulture,out double count)||double.IsNaN(count)||double.IsInfinity(count)||count<0)continue;
                result.entries.Add(new FanOccurrence {name=(string)dict?[item.Name]??item.Name,count=count});
            }
            result.entries.Sort((a,b)=>{int count=b.count.CompareTo(a.count);return count!=0?count:StringComparer.Ordinal.Compare(a.name,b.name);});
            return result;
        }
        public string Percent(double count)=>wins>0?(count*100/wins).ToString("0.00",CultureInfo.InvariantCulture)+"%":"—";
    }

    [Serializable] public sealed class HomeFanRow
    {
        public GameObject root;
        public TMP_Text label,count,percent;
    }

    public sealed class HomeFanStatistics:MonoBehaviour
    {
        public HomeDataPanel owner;
        public GameObject root,table;
        public TextAsset snapshot;
        public TMP_Text scope,note,empty,pageNumber;
        public UnityEngine.UI.Button previous,next;
        public List<HomeFanRow> rows=new List<HomeFanRow>();
        public string state="baked";
        public int pageIndex;
        public HomeFanData Data {get;private set;}
        readonly Dictionary<string,string> cache=new Dictionary<string,string>();
        UnityWebRequest request;
        Coroutine routine;
        int generation;
        string selectedKey,loadingKey,presentedJson,presentedScene;
        bool initialized;
        public void Initialize(HomeDataPanel panel)
        {
            owner=panel;if(initialized)return;initialized=true;
            previous.onClick.AddListener(()=>SetPage(pageIndex-1));
            next.onClick.AddListener(()=>SetPage(pageIndex+1));
        }
        public void RefreshSelection(bool force=false)
        {
            if(!Application.isPlaying||!owner.pageRoot.activeInHierarchy)return;
            string key=owner.ruleKey+":"+(owner.sceneKey=="events"?"events":"match");
            string selection=key+":"+owner.sceneKey;
            if(selectedKey!=selection){pageIndex=0;selectedKey=selection;}
            SetScope();
            if(!owner.Rule.fanStats){CancelRequest();Clear("unsupported","此规则暂不支持番种统计");return;}
            if(owner.sceneKey=="custom"){CancelRequest();Clear("unavailable","自定义房间暂未提供番种统计");return;}
            if(force)cache.Clear();
            if(owner.useSnapshot){
                CancelRequest();
                if(owner.ruleKey=="guobiao"&&owner.sceneKey!="events")Present(snapshot.text);
                else Clear("unavailable","此范围暂无番种快照");
                return;
            }
            if(cache.TryGetValue(key,out var json)){CancelRequest();Present(json);return;}
            if(loadingKey==key&&routine!=null){Clear("loading","正在读取番数统计…");return;}
            CancelRequest();Clear("loading","正在读取番数统计…");loadingKey=key;
            routine=StartCoroutine(Fetch(key,generation,owner.ruleKey,owner.sceneKey=="events"));
        }
        IEnumerator Fetch(string key,int ticket,string rule,bool events)
        {
            var url=owner.apiBase.TrimEnd('/')+ConfigManager.PlatformFanStatsPath+"?rule="+UnityWebRequest.EscapeURL(rule)+(events?"&event_id=all":"");
            var pending=UnityWebRequest.Get(url);request=pending;pending.timeout=12;
            yield return pending.SendWebRequest();
            if(ticket!=generation)yield break;
            bool success=pending.result==UnityWebRequest.Result.Success;
            string json=success?pending.downloadHandler.text:null;
            pending.Dispose();request=null;routine=null;loadingKey=null;
            if(!success){Clear("error","番数统计读取失败，请刷新重试");yield break;}
            try {Present(json);cache[key]=json;}
            catch(Exception){Clear("error","番数统计暂不可用，请刷新重试");}
        }
        public void Present(string json)
        {
            if (Data != null && state == "ready" && presentedJson == json && presentedScene == owner.sceneKey) return;
            Data=HomeFanData.Read(json,owner.sceneKey);
            presentedJson=json;presentedScene=owner.sceneKey;state="ready";SetScope();Render();
        }
        void SetScope()
        {
            if(scope)scope.text="";
            if(note)note.text="";
        }
        public void SetPage(int index)
        {
            int pages=Data==null?1:Math.Max(1,(int)Math.Ceiling(Data.entries.Count/(double)rows.Count));
            pageIndex=Mathf.Clamp(index,0,pages-1);Render();
        }
        void Render()
        {
            bool available=Data!=null&&Data.supported&&Data.wins>0&&Data.entries.Any(x=>x.count>0);
            table.SetActive(available);empty.gameObject.SetActive(!available);
            empty.text=Data!=null&&!Data.supported?"此规则暂不支持番种统计":"当前场次暂无番种记录";
            int pages=available?Math.Max(1,(int)Math.Ceiling(Data.entries.Count/(double)rows.Count)):1;
            pageIndex=Mathf.Clamp(pageIndex,0,pages-1);
            for(int i=0;i<rows.Count;i++)
            {
                int index=pageIndex*rows.Count+i;var row=rows[i];bool visible=available&&index<Data.entries.Count;
                row.root.SetActive(visible);if(!visible)continue;
                var entry=Data.entries[index];row.label.text=entry.name;
                row.count.text=entry.count.ToString("#,0",CultureInfo.InvariantCulture);row.percent.text=Data.Percent(entry.count);
            }
            pageNumber.text=(pageIndex+1)+" / "+pages;
            previous.interactable=available&&pageIndex>0;next.interactable=available&&pageIndex+1<pages;
            SetScope();
            ApplyPalette();
        }
        void Clear(string nextState,string message)
        {
            Data=null;pageIndex=0;state=nextState;table.SetActive(false);empty.gameObject.SetActive(true);empty.text=message;
            foreach(var row in rows)row.root.SetActive(false);
            pageNumber.text="—";previous.interactable=next.interactable=false;ApplyPalette();
        }
        public void ApplyPalette()
        {
            foreach(var button in new[]{previous,next}){
                if(!button)continue;
                button.targetGraphic.color=owner.ActivePalette.colors[(int)(button.interactable?HomeColor.Button:HomeColor.Panel)];
                var color=owner.ActivePalette.colors[(int)HomeColor.Ink];if(!button.interactable)color.a=.32f;
                button.GetComponentInChildren<TMP_Text>().color=color;
            }
        }
        public void CancelRequest()
        {
            generation++;
            if(routine!=null){StopCoroutine(routine);routine=null;}
            if(request!=null){request.Abort();request.Dispose();request=null;}
            loadingKey=null;if(state=="loading")state="idle";
        }
        void OnDisable()=>CancelRequest();
    }
}
