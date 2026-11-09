using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.InputSystem;

namespace Salasasa.DataLab
{
    public enum HomeColor { Panel,Cell,Ink,Muted,Line,Rule,RuleInk,Accent,AccentInk,Callout,CalloutInk,Subtle,Button }
    [Serializable] public sealed class HomePalette {
        public string name;public Color[] colors;public Color[] tierSurfaces;
        public HomePalette Copy(string label=null)=>new HomePalette{name=label??name,colors=(Color[])colors.Clone(),tierSurfaces=tierSurfaces==null?null:(Color[])tierSurfaces.Clone()};
    }
    [Serializable] public sealed class HomeTint { public UnityEngine.UI.Graphic graphic;public HomeColor color; }
    [Serializable] public sealed class HomeTab { public string key;public UnityEngine.UI.Button button;public TMP_Text text;public UnityEngine.UI.Image line; }
    [Serializable] public sealed class HomeMetric { public GameObject root;public TMP_Text label,value;public UnityEngine.UI.Graphic surface; }
    [Serializable] public sealed class HomeRecord { public GameObject root;public UnityEngine.UI.Button button;public UnityEngine.UI.Image tierBadge;public RecentMatchCard card;public TMP_Text date,scene,mode;public TMP_Text[] players; }
    [Serializable] public sealed class HomeRecordLayout { public string name;public GameObject root;public List<HomeRecord> rows=new List<HomeRecord>(); }

    [DefaultExecutionOrder(-1900)]
    public sealed class HomeDataPanel:MonoBehaviour
    {
        public string apiBase => ConfigManager.webApiUrl;
        public TextAsset catalog,snapshotHome,snapshotRecords;
        public GameObject pageRoot,lobbyRoot,overviewRoot,recordsRoot,modeRoot,detailRoot;
        public TMP_Dropdown mainRules,allRules;
        public TMP_InputField playerIdInput;
        public UnityEngine.UI.Button queryPlayer,visitDataStation;
        public TMP_Dropdown statisticsScene,statisticsMode,recordScene;
        public List<HomeRecordLayout> recordLayouts=new List<HomeRecordLayout>();
        public int cardStyleIndex;
        public bool contrastDesign;
        public HomePaletteEditor paletteEditor;
        public HomeFanStatistics fanStatistics;
        public TMP_Text context,status,overviewNote,recordsStatus,pageNumber,recordEmpty;
        public UnityEngine.UI.Button close,open,refresh,visitRecords,previous,next,closeDetail,headerLobby,headerData;
        public RectTransform headerUnderline;
        public MatchDetail detail;
        public List<HomePalette> palettes=new List<HomePalette>();
        public List<HomeTint> tints=new List<HomeTint>();
        public List<HomeTab> ruleTabs=new List<HomeTab>(),sceneTabs=new List<HomeTab>(),modeTabs=new List<HomeTab>(),pageTabs=new List<HomeTab>(),styleTabs=new List<HomeTab>();
        public List<HomeMetric> metrics=new List<HomeMetric>();
        public bool fixedDefaultMetricSurface;
        public Color defaultMetricSurface=new Color32(88,88,88,255);
        public static readonly string[] RecordLayoutNames={"横列分栏","左侧场次","双列名单","横向顺位","双排顺位","成绩分栏"};
        public int RecordLayoutCount=>recordLayouts.Count;
        public int SelectedRecordLayout=>cardStyleIndex;
        public List<HomeRecord> rows=new List<HomeRecord>();
        public int styleIndex,pageIndex;
        public string ruleKey="guobiao",sceneKey="beginner",modeKey="",state="baked";
        public bool useSnapshot;
        public bool fullscreen;
        public bool integrated;
        public event Action Closed;
        public string recordSceneKey="all",recordState="idle";
        public int recordOffset,recordTotal;
        public IReadOnlyList<MatchResult> Matches=>records.records;
        public HomeBlockStats Stats{get;private set;}
        List<DataRule> rules;
        string[] mainRuleKeys;
        readonly Dictionary<string,string> selectedSubRules=new Dictionary<string,string>();
        public IReadOnlyList<DataRule> AvailableRules { get { Initialize(); return rules; } }
        public IReadOnlyList<string> MainRuleKeys { get { Initialize(); return mainRuleKeys; } }
        public IReadOnlyList<DataRule> SubRules { get { Initialize(); return RuleOptions(); } }
        string homeJson,recordsKey;
        PlatformData records=new PlatformData();
        UnityWebRequest homeRequest,recordRequest;
        Coroutine homeRoutine,recordRoutine;
        int recordGeneration;
        bool initialized;
        [NonSerialized] HomePalette previewPalette;
        public HomePalette ActivePalette=>previewPalette??palettes[Mathf.Clamp(styleIndex,0,palettes.Count-1)];
        Color Paint(HomeColor color)=>ActivePalette.colors[(int)color];
        public Color MetricSurfaceColor=>fixedDefaultMetricSurface&&previewPalette==null&&styleIndex!=HomePaletteEditor.CustomIndex?defaultMetricSurface:Paint(HomeColor.Cell);
        public DataRule Rule=>rules.First(x=>x.key==ruleKey);
        public void Initialize()
        {
            if(rules!=null&&rules.Count>0)return;
            rules=DataRule.Read(catalog.text);
            string[] first={"guobiao","riichi","sichuan","qingque","hongque"};
            var families=rules.Select(x=>x.family).Distinct().ToArray();
            mainRuleKeys=first.Where(families.Contains).Concat(families.Where(x=>!first.Contains(x))).ToArray();
            if(!rules.Any(x=>x.key==ruleKey))ruleKey=rules[0].key;
            SyncRuleDropdowns();
        }
        List<DataRule> RuleOptions()=>mainRules?rules.Where(x=>x.family==Rule.family).ToList():rules;
        void SyncRuleDropdowns()
        {
            if(mainRules) {
                var labels=mainRuleKeys.Select(key=>rules.First(x=>x.family==key).familyLabel??key).ToArray();
                SyncDropdown(mainRules,mainRuleKeys,key=>labels[Array.IndexOf(mainRuleKeys,key)],Rule.family);
            }
            var options=RuleOptions();
            SyncDropdown(allRules,options.Select(x=>x.key).ToArray(),key=>{
                var option=options.First(x=>x.key==key);
                return mainRules&&!string.IsNullOrEmpty(option.subRuleLabel)?option.subRuleLabel:option.label;
            },ruleKey);
        }
        public void SelectMainRule(int index)
        {
            Initialize();if(index<0||index>=mainRuleKeys.Length)return;
            string family=mainRuleKeys[index];
            SetRule(selectedSubRules.TryGetValue(family,out string key)&&rules.Any(x=>x.key==key&&x.family==family)
                ?key:rules.First(x=>x.family==family).key);
        }
        public void QueryPlayer()
        {
            string input=playerIdInput?playerIdInput.text.Trim():"";
            if(!int.TryParse(input,out int id)||id<=0) {
                NotificationManager.Instance?.ShowTip("查询玩家",false,"请输入有效的玩家 UID");return;
            }
            ProfileOnClick.OpenPlayerInfo(id,ruleKey);
        }
        public string DataStationUrl=>ConfigManager.dataStationUrl;
        public void ConfirmVisitDataStation()
        {
            NotificationManager.Instance?.ShowConfirmation("前往数据站","前往数据站需要跳转网页，您确定吗？",()=>Application.OpenURL(DataStationUrl));
        }
        void Awake()
        {
            // Authored pages remain visible for inspection; the lobby controls runtime visibility.
            if (integrated) pageRoot.SetActive(false);
            Initialize();
            foreach(var tab in ruleTabs){var key=tab.key;tab.button.onClick.AddListener(()=>SetRule(key));}
            foreach(var tab in sceneTabs){var key=tab.key;tab.button.onClick.AddListener(()=>SetScene(key));}
            foreach(var tab in modeTabs){var key=tab.key;tab.button.onClick.AddListener(()=>SetMode(key));}
            for(int i=0;i<pageTabs.Count;i++){int n=i;pageTabs[i].button.onClick.AddListener(()=>SetPage(n));}
            for(int i=0;i<styleTabs.Count;i++){int n=i;styleTabs[i].button.onClick.AddListener(()=>SetStyle(n));}
            if(mainRules)mainRules.onValueChanged.AddListener(SelectMainRule);
            allRules.onValueChanged.AddListener(i=>{var options=RuleOptions();if(i>=0&&i<options.Count)SetRule(options[i].key);});
            if(queryPlayer)queryPlayer.onClick.AddListener(QueryPlayer);
            if(playerIdInput)playerIdInput.onSubmit.AddListener(_=>QueryPlayer());
            if(visitDataStation)visitDataStation.onClick.AddListener(ConfirmVisitDataStation);
            if(statisticsScene)statisticsScene.onValueChanged.AddListener(i=>SetScene(HomeBlockStats.SceneKeys(ruleKey)[i]));
            if(statisticsMode)statisticsMode.onValueChanged.AddListener(i=>SetMode(AllowedModes()[i]));
            if(recordScene)recordScene.onValueChanged.AddListener(i=>SetRecordScene(AllowedRecordScenes()[i]));
            if(close)close.onClick.AddListener(Close);if(open)open.onClick.AddListener(Open);if(headerLobby)headerLobby.onClick.AddListener(Close);if(headerData)headerData.onClick.AddListener(Open);
            if(visitRecords)visitRecords.onClick.AddListener(()=>SetPage(1));refresh.onClick.AddListener(Reload);
            previous.onClick.AddListener(()=>LoadRecords(Math.Max(0,recordOffset-rows.Count)));
            next.onClick.AddListener(()=>LoadRecords(recordOffset+rows.Count));
            closeDetail.onClick.AddListener(()=>detailRoot.SetActive(false));
            if(recordLayouts.Count>0){foreach(var layout in recordLayouts)for(int i=0;i<layout.rows.Count;i++){int n=i;layout.rows[i].button.onClick.AddListener(()=>ShowMatch(n));}}
            else for(int i=0;i<rows.Count;i++){int n=i;rows[i].button.onClick.AddListener(()=>ShowMatch(n));}
            if(paletteEditor)paletteEditor.Initialize(this);
            if(fanStatistics)fanStatistics.Initialize(this);
            initialized=true;SetCardStyle(cardStyleIndex);SetStyle(styleIndex);SetPage(0);UpdateHeader();
        }
        void Start()=>Reload();
        void Update()
        {
            if(integrated)return;
            var k=Keyboard.current;if(k==null)return;
            if(k.escapeKey.wasPressedThisFrame&&!new[]{mainRules,allRules,statisticsScene,statisticsMode,recordScene}.Any(d=>d&&d.IsExpanded)){if(detailRoot.activeSelf)detailRoot.SetActive(false);else if(pageRoot.activeSelf)Close();}
        }
        void OnDisable(){StopRequests();}
        bool RecordsVisible=>fullscreen||pageIndex==1;
        public void Open(){pageRoot.SetActive(true);if(lobbyRoot)lobbyRoot.SetActive(false);UpdateHeader();if(homeJson==null||state=="idle")Reload();else if(RecordsVisible)EnsureRecords();if(fanStatistics)fanStatistics.RefreshSelection();}
        public void Close(){foreach(var dropdown in new[]{mainRules,allRules,statisticsScene,statisticsMode,recordScene})if(dropdown)dropdown.Hide();StopRequests();detailRoot.SetActive(false);pageRoot.SetActive(false);if(lobbyRoot)lobbyRoot.SetActive(true);UpdateHeader();Closed?.Invoke();}
        public bool HandleEscape()
        {
            if(!pageRoot.activeSelf)return false;
            foreach(var dropdown in new[]{mainRules,allRules,statisticsScene,statisticsMode,recordScene})if(dropdown&&dropdown.IsExpanded){dropdown.Hide();return true;}
            if(detailRoot.activeSelf)detailRoot.SetActive(false);else Close();
            return true;
        }
        void UpdateHeader()
        {
            if(integrated||!headerData||!headerLobby||!headerUnderline)return;
            bool active=pageRoot.activeSelf;
            headerData.GetComponent<TMP_Text>().color=active?new Color32(234,240,246,255):new Color32(159,172,188,255);
            headerLobby.GetComponent<TMP_Text>().color=active?new Color32(159,172,188,255):new Color32(234,240,246,255);
            var r=(active?headerData:headerLobby).GetComponent<RectTransform>();
            var p=headerUnderline.anchoredPosition;p.x=r.anchoredPosition.x+(r.rect.width-headerUnderline.rect.width)/2;headerUnderline.anchoredPosition=p;
        }
        public void OpenPlatform(string rule="guobiao",string scene="beginner")
        {
            Initialize();SetRule(rule);if(HomeBlockStats.SceneKeys(ruleKey).Contains(scene))SetScene(scene);Open();
        }
        public void SetStyle(int index)
        {
            styleIndex=Mathf.Clamp(index,0,palettes.Count-1);
            previewPalette=null;
            ApplyPalette();
            if(initialized&&paletteEditor)paletteEditor.RememberSelection(styleIndex);
        }
        public void PreviewPalette(HomePalette draft){previewPalette=draft;ApplyPalette();}
        void ApplyPalette()
        {
            foreach(var tint in tints)if(tint.graphic)tint.graphic.color=Paint(tint.color);
            if(playerIdInput) {
                playerIdInput.caretColor=Paint(HomeColor.Ink);
                playerIdInput.GetComponent<InputFieldFeedback>()?.SetPalette(Paint(HomeColor.Ink));
            }
            ApplyMetricColors();
            detail.currentPlayerAccent=Paint(HomeColor.Accent);detail.otherPlayerAccent=Paint(HomeColor.Line);
            UpdateTabs();StylePager();ColorRecords();if(detailRoot.activeSelf&&selectedRecord>=0)detail.Present(records.records[selectedRecord],null);
            if(fanStatistics)fanStatistics.ApplyPalette();
        }
        void ApplyMetricColors()
        {
            if(!fixedDefaultMetricSurface)return;
            Color surface=MetricSurfaceColor;
            Color ink=previewPalette!=null||styleIndex==HomePaletteEditor.CustomIndex?Paint(HomeColor.Ink):RecentMatchCard.ReadableInk(surface);
            foreach(var metric in metrics){
                if(metric.surface)metric.surface.color=surface;
                if(metric.label)metric.label.color=ink;
                if(metric.value)metric.value.color=ink;
            }
        }
        public void SelectRecordLayout(int index)=>SetCardStyle(index);
        public void SetCardStyle(int index)
        {
            if(recordLayouts.Count==0)return;
            cardStyleIndex=Mathf.Clamp(index,0,recordLayouts.Count-1);
            for(int i=0;i<recordLayouts.Count;i++)recordLayouts[i].root.SetActive(i==cardStyleIndex);
            rows=recordLayouts[cardStyleIndex].rows;
            RenderRecords();
            if(recordState=="unavailable")recordEmpty.text="自定义房间\n暂未开放公共对局记录";
            else if(recordState=="loading"){recordsStatus.text="正在读取…";recordEmpty.text="正在读取最近对局…";previous.interactable=false;next.interactable=false;StylePager();}
            else if(recordState=="error"){recordsStatus.text="读取失败 · 请刷新";recordEmpty.text="暂时无法连接对局服务";}
        }
        public void SetRule(string key)
        {
            Initialize();if(!rules.Any(x=>x.key==key))key="guobiao";
            ruleKey=key;selectedSubRules[Rule.family]=key;SyncRuleDropdowns();
            if(!HomeBlockStats.SceneKeys(key).Contains(sceneKey))sceneKey=HomeBlockStats.SceneKeys(key)[0];
            if(!AllowedRecordScenes().Contains(recordSceneKey))recordSceneKey="all";
            modeKey="";InvalidateRecords();RenderStats();UpdateTabs();if(RecordsVisible&&initialized)EnsureRecords();
        }
        public void SetScene(string key)
        {
            if(!HomeBlockStats.SceneKeys(ruleKey).Contains(key))return;
            sceneKey=key;modeKey="";if(!fullscreen)InvalidateRecords();RenderStats();UpdateTabs();if(!fullscreen&&RecordsVisible&&initialized)EnsureRecords();
        }
        public void SetMode(string key){modeKey=key;RenderStats();UpdateTabs();}
        public void SetPage(int index)
        {
            pageIndex=Mathf.Clamp(index,0,1);overviewRoot.SetActive(fullscreen||pageIndex==0);recordsRoot.SetActive(RecordsVisible);modeRoot.SetActive(fullscreen||pageIndex==0);
            UpdateTabs();if(RecordsVisible&&initialized)EnsureRecords();
        }
#if UNITY_EDITOR
        public void BakePresentation()
        {
            Initialize();
            useSnapshot=false;
            pageRoot.SetActive(true);
            detailRoot.SetActive(false);
            SetStyle(styleIndex);SetPage(0);
            status.text="";context.text=Rule.label+"  /  "+HomeBlockStats.SceneLabel(sceneKey);
            foreach (var metric in metrics) { metric.root.SetActive(true); metric.value.text="—"; }
            overviewNote.text=recordsStatus.text="";
            recordEmpty.gameObject.SetActive(false);
            foreach(var layout in recordLayouts) layout.root.SetActive(layout==recordLayouts[cardStyleIndex]);
            rows=recordLayouts[cardStyleIndex].rows;
            foreach(var row in rows) {
                row.root.SetActive(true);row.date.text="—";row.scene.text="—";row.mode.text="—";
                foreach(var player in row.players) player.text="—";
                for(int i=0;i<row.card.ranks.Length;i++) row.card.ranks[i].text="第"+(i+1)+"名";
                foreach(var score in row.card.scores) if(score)score.gameObject.SetActive(false);
                row.card.Present(new MatchResult { room="match",tier="" },ActivePalette);
                row.card.RefreshGeometry();
            }
            pageNumber.text="1 / 1";previous.interactable=next.interactable=false;
            if(fanStatistics) {
                fanStatistics.owner=this;fanStatistics.root.SetActive(true);fanStatistics.table.SetActive(true);
                fanStatistics.empty.gameObject.SetActive(false);fanStatistics.pageNumber.text="1 / 1";
                foreach(var row in fanStatistics.rows) {row.root.SetActive(true);row.label.text=row.count.text=row.percent.text="—";}
                fanStatistics.ApplyPalette();
            }
        }
#endif
        public void PreviewSnapshot()
        {
            rules=null;Initialize();homeJson=snapshotHome.text;RenderStats();SetStyle(styleIndex);SetPage(pageIndex);status.text="平台数据 · 快照";
            if(fullscreen){records.ReadRecords(SnapshotRecords(0));recordOffset=0;recordTotal=records.recordTotal;RenderRecords();recordState="baked";}
        }
        void UpdateTabs()
        {
            if(rules==null)return;
            foreach(var tab in ruleTabs)ColorTab(tab,tab.key==ruleKey,true);
            var available=HomeBlockStats.SceneKeys(ruleKey);float x=0;
            foreach(var tab in sceneTabs){bool shown=available.Contains(tab.key);tab.button.gameObject.SetActive(shown);if(!shown)continue;
                var r=tab.button.GetComponent<RectTransform>();r.anchoredPosition=new Vector2(x,r.anchoredPosition.y);x+=r.rect.width;ColorTab(tab,tab.key==sceneKey,false);}
            var allowed=AllowedModes();float mx=0;
            foreach(var tab in modeTabs){bool shown=allowed.Contains(tab.key);tab.button.gameObject.SetActive(shown);if(!shown)continue;
                var r=tab.button.GetComponent<RectTransform>();r.anchoredPosition=new Vector2(mx,r.anchoredPosition.y);mx+=r.rect.width;
                ColorTab(tab,tab.key==modeKey,false);if(tab.key.Length>0)tab.text.text=Rule.family=="hongque"?ModeHands(tab.key)+"局":ModeLabel(tab.key);}
            for(int i=0;i<pageTabs.Count;i++)ColorTab(pageTabs[i],i==pageIndex,false);
            for(int i=0;i<styleTabs.Count;i++)ColorTab(styleTabs[i],i==styleIndex,false);
            context.text=Rule.label+"  /  "+HomeBlockStats.SceneLabel(sceneKey);
            SyncDropdown(statisticsScene,HomeBlockStats.SceneKeys(ruleKey),ScopeLabel,sceneKey);
            SyncDropdown(statisticsMode,AllowedModes(),key=>key==""?"全部局制":Rule.family=="hongque"?ModeHands(key)+"局":ModeLabel(key),modeKey);
            SyncDropdown(recordScene,AllowedRecordScenes(),ScopeLabel,recordSceneKey);
        }
        static void SyncDropdown(TMP_Dropdown dropdown,string[] keys,Func<string,string> label,string selected)
        {
            if(!dropdown)return;
            var labels=keys.Select(label).ToList();
            if(!dropdown.options.Select(x=>x.text).SequenceEqual(labels)){dropdown.Hide();dropdown.ClearOptions();dropdown.AddOptions(labels);}
            dropdown.SetValueWithoutNotify(Math.Max(0,Array.IndexOf(keys,selected)));
        }
        public static string ScopeLabel(string key)=>key=="all"?"全部匹配场":key=="rank"?"段位场":key=="events"?"比赛场":key=="mcrpl"?"MCRPL":PlatformData.Label(key);
        public string[] AllowedRecordScenes()=>new[]{"all"}.Concat(HomeBlockStats.SceneKeys(ruleKey).Where(x=>x!="rank")).ToArray();
        public void SetRecordScene(string key)
        {
            if(!AllowedRecordScenes().Contains(key))return;
            recordSceneKey=key;InvalidateRecords();UpdateTabs();if(initialized&&pageRoot.activeSelf)EnsureRecords();
        }
        static int ModeHands(string key)=>key=="quanzhuang"?16:key=="xifeng"?12:key=="banzhuang"?8:4;
        public string[] AllowedModes()
        {
            if(Rule.family!="hongque")return new[]{"","quanzhuang","xifeng","banzhuang","dongfeng"};
            return sceneKey=="custom"||sceneKey=="events"?new[]{"","quanzhuang","xifeng","banzhuang","dongfeng"}:new[]{"","quanzhuang","banzhuang"};
        }
        static string ModeLabel(string key)=>key=="quanzhuang"?"全庄战":key=="xifeng"?"东西战":key=="banzhuang"?"半庄战":"东风战";
        void ColorTab(HomeTab tab,bool active,bool dark)
        {
            tab.button.targetGraphic.color=dark?(active?Paint(HomeColor.Accent):Paint(HomeColor.Rule)):(active?Paint(HomeColor.Subtle):Paint(HomeColor.Panel));
            tab.text.color=dark?(active?Paint(HomeColor.AccentInk):Paint(HomeColor.RuleInk)):(active?Paint(HomeColor.Accent):Paint(HomeColor.Muted));
            tab.text.fontStyle=FontStyles.Normal;
            if(tab.line){tab.line.gameObject.SetActive(active);tab.line.color=Paint(HomeColor.Accent);}
        }
        public void Reload()
        {
            if(!initialized||!pageRoot.activeSelf)return;
            StopRequests();InvalidateRecords();status.text="正在更新…";state="loading";if(homeJson==null)ClearMetrics();
            if(fanStatistics)fanStatistics.RefreshSelection(true);
            if(RecordsVisible)EnsureRecords();
            if(useSnapshot){homeJson=snapshotHome.text;RenderStats();state="ready";status.text="平台数据 · 快照";return;}
            homeRoutine=StartCoroutine(FetchHome());
        }
        IEnumerator FetchHome()
        {
            var request=UnityWebRequest.Get(apiBase.TrimEnd('/')+ConfigManager.PlatformHomeStatsPath);homeRequest=request;request.timeout=12;yield return request.SendWebRequest();
            if(request.result==UnityWebRequest.Result.Success){
                try{homeJson=request.downloadHandler.text;RenderStats();state="ready";status.text="更新于 "+DateTime.Now.ToString("HH:mm");}
                catch(Exception){homeJson=null;state="error";status.text="数据格式异常，请刷新重试";ClearMetrics();}
            }else{state="error";status.text=homeJson==null?"连接失败，请刷新重试":"更新失败 · 保留上次数据";if(homeJson==null)ClearMetrics();}
            request.Dispose();homeRequest=null;homeRoutine=null;
        }
        void ClearMetrics(){foreach(var m in metrics)m.value.text="—";}
        void RenderStats()
        {
            if(fanStatistics)fanStatistics.RefreshSelection();
            if(homeJson==null){ClearMetrics();return;}
            Stats=HomeBlockStats.Read(homeJson,Rule,sceneKey,modeKey);
            for(int i=0;i<metrics.Count;i++){var m=metrics[i];bool show=i<Stats.cells.Count;m.root.SetActive(show);if(show){m.label.text=Stats.cells[i].Key;m.value.text=Stats.cells[i].Value;}}
            overviewNote.text=Stats.games==0?"当前范围暂无对局":!Stats.details?"部分明细暂不可用，以 — 显示":"";
        }
        void InvalidateRecords()
        {
            CancelRecord();recordsKey=null;records.records.Clear();recordOffset=0;recordTotal=0;
            foreach(var row in rows)row.root.SetActive(false);detailRoot.SetActive(false);selectedRecord=-1;
            recordState="idle";recordsStatus.text="";pageNumber.text="1 / 1";previous.interactable=false;next.interactable=false;
        }
        string RecordScope=>fullscreen?recordSceneKey:sceneKey;
        void EnsureRecords(){if(pageRoot.activeInHierarchy&&recordsKey!=ruleKey+":"+RecordScope)LoadRecords(0);}
        public void LoadRecords(int offset)
        {
            if(!initialized)return;
            if(offset<0||offset>0&&offset>=recordTotal)return;
            CancelRecord();recordsKey=ruleKey+":"+RecordScope;
            if(RecordScope=="custom"){
                records.records.Clear();recordOffset=0;recordTotal=0;RenderRecords();recordState="unavailable";recordEmpty.text="自定义房间\n暂未开放公共对局记录";return;
            }
            previous.interactable=false;next.interactable=false;StylePager();
            recordState="loading";
            recordsStatus.text="正在读取…";recordEmpty.gameObject.SetActive(records.records.Count==0);recordEmpty.text="正在读取最近对局…";
            recordRoutine=StartCoroutine(FetchRecords(offset,recordGeneration));
        }
        public string RecordsRoute(int offset)=>ConfigManager.PlatformRecentRecordsPath+"?rule="+UnityWebRequest.EscapeURL(ruleKey)+"&limit="+rows.Count+"&offset="+offset+
            (RecordScope=="events"?"&event_id=all":RecordScope=="rank"||RecordScope=="all"?"":"&match_tier="+UnityWebRequest.EscapeURL(RecordScope));
        string SnapshotRecords(int offset)
        {
            var all=PlayerData.Payload(snapshotRecords.text);
            var items=(all["items"] as Newtonsoft.Json.Linq.JArray??new Newtonsoft.Json.Linq.JArray()).Where(x=>(string)x["rule"]==Rule.family&&(string)x["sub_rule"]==Rule.subRule)
                .Where(x=>RecordScope=="events"?(string)x["room_type"]=="events":(string)x["room_type"]=="match"&&(RecordScope=="all"||RecordScope=="rank"||(string)x["match_tier"]==RecordScope)).ToArray();
            return new Newtonsoft.Json.Linq.JObject{["data"]=new Newtonsoft.Json.Linq.JObject{["items"]=new Newtonsoft.Json.Linq.JArray(items.Skip(offset).Take(rows.Count)),["total"]=items.Length}}.ToString();
        }
        IEnumerator FetchRecords(int offset,int ticket)
        {
            string json;
            if(useSnapshot){
                json=SnapshotRecords(offset);yield return null;
            }else{
                var request=UnityWebRequest.Get(apiBase.TrimEnd('/')+RecordsRoute(offset));recordRequest=request;request.timeout=12;yield return request.SendWebRequest();
                if(ticket!=recordGeneration)yield break;
                if(request.result!=UnityWebRequest.Result.Success){
                    request.Dispose();recordRequest=null;recordRoutine=null;recordsKey=null;recordState="error";recordsStatus.text="读取失败 · 请刷新";recordEmpty.text="暂时无法连接对局服务";previous.interactable=recordOffset>0;next.interactable=recordOffset+rows.Count<recordTotal;StylePager();yield break;
                }
                json=request.downloadHandler.text;request.Dispose();recordRequest=null;
            }
            if(ticket!=recordGeneration)yield break;
            try{records.ReadRecords(json,offset);recordOffset=offset;recordTotal=records.recordTotal;RenderRecords();recordState="ready";}
            catch(Exception){recordState="error";recordsStatus.text="对局格式异常 · 请刷新";recordsKey=null;recordEmpty.text="对局数据暂不可用";}
            recordRoutine=null;
        }
        void RenderRecords()
        {
            for(int i=0;i<rows.Count;i++){
                var row=rows[i];bool visible=i<records.records.Count;row.root.SetActive(visible);if(!visible)continue;
                var match=records.records[i];row.date.text=match.DateLabel;row.scene.text=match.SceneLabel;row.mode.text=match.ModeLabel;
                if(row.card)row.card.Bind(match);
                else for(int j=0;j<row.players.Length;j++){row.players[j].text=j<match.seats.Count?match.seats[j].name:"";}
            }
            recordEmpty.gameObject.SetActive(records.records.Count==0);recordEmpty.text="当前场次暂无公开对局";
            recordsStatus.text="";
            int pages=Math.Max(1,(int)Math.Ceiling(recordTotal/(double)rows.Count));pageNumber.text=(recordOffset/rows.Count+1)+" / "+pages;
            previous.interactable=recordOffset>0;next.interactable=recordOffset+rows.Count<recordTotal;StylePager();
            ColorRecords();
        }
        void ColorRecords()
        {
            for(int i=0;i<rows.Count&&i<records.records.Count;i++){
                var row=rows[i];
                if(row.card){row.card.Present(records.records[i],ActivePalette);continue;}
                if(!row.tierBadge)continue;
                var match=records.records[i];string key=match.room=="events"?"events":match.tier;
                row.tierBadge.color=TierColor(key,true);row.scene.color=TierColor(key,false);
            }
        }
        public static Color TierColor(string key,bool background)
        {
            string hex=key switch{
                "beginner"=>background?"E8F1FD":"2564B4",
                "intermediate"=>background?"FFF1D8":"926018",
                "advanced"=>background?"F0E9FA":"7546AC",
                "mcrpl"=>background?"FCE8E6":"B14C43",
                "events"=>background?"FCE8F2":"A64074",
                _=>background?"E8ECF2":"4E607B"};
            ColorUtility.TryParseHtmlString("#"+hex,out var color);return color;
        }
        void StylePager()
        {
            foreach(var button in new[]{previous,next}){
                if(!button)continue;var colors=button.colors;colors.disabledColor=Color.white;button.colors=colors;
                button.targetGraphic.color=Paint(button.interactable?HomeColor.Button:HomeColor.Panel);
                var color=Paint(HomeColor.Ink);if(!button.interactable)color.a=.32f;button.GetComponentInChildren<TMP_Text>().color=color;
            }
        }
        int selectedRecord=-1;
        public void ShowMatch(int index)
        {
            if(index<0||index>=records.records.Count)return;selectedRecord=index;detailRoot.SetActive(true);detail.Present(records.records[index],null);
        }
        void CancelRecord(){recordGeneration++;if(recordRoutine!=null){StopCoroutine(recordRoutine);recordRoutine=null;}if(recordRequest!=null){recordRequest.Abort();recordRequest.Dispose();recordRequest=null;}}
        void StopRequests(){if(fanStatistics)fanStatistics.CancelRequest();if(recordRoutine!=null||recordRequest!=null)recordsKey=null;CancelRecord();if(homeRoutine!=null||homeRequest!=null)state="idle";if(homeRoutine!=null){StopCoroutine(homeRoutine);homeRoutine=null;}if(homeRequest!=null){homeRequest.Abort();homeRequest.Dispose();homeRequest=null;}}
    }
}
