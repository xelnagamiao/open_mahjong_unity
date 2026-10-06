using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Resting styles and layout are authored in MainScene; transitions animate the authored slots.
public sealed class MatchLobbyView : MonoBehaviour {
    public const int MaximumSelections=4;
    [SerializeField] private MatchButton[] entries;
    [SerializeField] private GameObject[] rulePages;
    [SerializeField] private Button[] ruleButtons;
    [SerializeField] private GameObject[] selectedTabs;
    [SerializeField] private TMP_Text rankText,pointsText,selectionText,startText,elapsedText,limitText;
    [SerializeField] private TMP_Text[] rulePlayerCounts=new TMP_Text[0];
    [SerializeField] private GameObject variantBar;
    [SerializeField] private Button[] variantButtons=new Button[0];
    [SerializeField] private GameObject[] selectedVariants=new GameObject[0];
    [SerializeField] private TMP_Text[] variantLabels=new TMP_Text[0],variantPlayerCounts=new TMP_Text[0];
    [SerializeField] private TMP_Text variantHint;
    [SerializeField] private Image progress;
    [SerializeField] private MatchQueueSlot[] slots;
    [SerializeField] private Button startButton,clearButton,cancelButton;
    [SerializeField] private GameObject queueStatus,joiningLabel,queueingLabel,cancelingLabel,foundLabel;
    [SerializeField] private GameObject queueGroup;
    [SerializeField] private GameObject eloHelp;
    [SerializeField] private CanvasGroup queueOpacity;
    [SerializeField, Min(.01f)] private float transitionDuration=.24f;
    private readonly List<MatchButton> selected=new List<MatchButton>(MaximumSelections);
    private readonly int[] rememberedRules={0,1,2,3};
    private Dictionary<string,int> playerCounts;
    private int activeRule,renderedState=-1,lastSecond=-1;
    private int renderedVersion=-1;
    private bool bound;
    private bool hasPresented;
    public IReadOnlyList<MatchButton> Selections=>selected;
    public int ActiveRule=>activeRule;
    public int ActiveFamily=>RankedRules.FamilyIndex(activeRule);
    public void UpdateRulePlayerCounts(Dictionary<string,int> counts){
        playerCounts=counts==null?null:new Dictionary<string,int>(counts);
        for(int i=0;i<rulePlayerCounts.Length;i++){
            int total=0;bool known=playerCounts!=null;
            foreach(int rule in RankedRules.FamilyRuleIndices[i]){
                if(playerCounts!=null&&playerCounts.TryGetValue(RankedRules.Ids[rule],out int count))total+=count;
                else known=false;
            }
            rulePlayerCounts[i].text=known?total.ToString():"—";
        }
        RefreshVariantCounts();
    }
    public bool IsBusy=>MatchStateManager.Instance.IsQueueing||MatchStateManager.Instance.IsMatchFound||
        (MatchNetworkManager.Instance!=null&&(MatchNetworkManager.Instance.IsJoinPending||MatchNetworkManager.Instance.IsLeavePending));
    private void Awake(){Bind();}
    private void OnEnable(){hasPresented=false;Bind();SelectRule(activeRule);RefreshState();}
    private void OnDisable(){hasPresented=false;}
    private void Bind(){
        if(bound)return;bound=true;
        for(int i=0;i<ruleButtons.Length;i++){int index=i;ruleButtons[i].onClick.AddListener(()=>SelectFamily(index));}
        for(int i=0;i<variantButtons.Length;i++){int index=i;variantButtons[i].onClick.AddListener(()=>SelectVariant(index));}
        cancelButton.onClick.AddListener(CancelMatching);
    }
    public void SelectFamily(int index){
        if(index<0||index>=rememberedRules.Length)return;
        SelectRule(rememberedRules[index]);
    }
    public void SelectVariant(int index){
        var rules=RankedRules.FamilyRuleIndices[ActiveFamily];
        if(index<0||index>=rules.Length)return;
        SelectRule(rules[index]);
    }
    public void SelectRule(int index){
        if(index<0||index>=rulePages.Length)return;activeRule=index;
        int family=ActiveFamily;rememberedRules[family]=index;
        for(int i=0;i<rulePages.Length;i++)rulePages[i].SetActive(i==index);
        for(int i=0;i<selectedTabs.Length;i++)selectedTabs[i].SetActive(i==family);
        var rules=RankedRules.FamilyRuleIndices[family];
        if(variantBar!=null){
            variantBar.SetActive(rules.Length>1);
            for(int i=0;i<variantButtons.Length;i++){
                variantButtons[i].gameObject.SetActive(i<rules.Length);
                if(i>=rules.Length)continue;
                variantLabels[i].text=RankedRules.VariantName(rules[i]);
                selectedVariants[i].SetActive(rules[i]==index);
            }
            if(variantHint!=null){variantHint.text=string.Empty;variantHint.gameObject.SetActive(false);}
        }
        RefreshVariantCounts();
        RefreshRank();
    }
    private void RefreshVariantCounts(){
        var rules=RankedRules.FamilyRuleIndices[ActiveFamily];
        for(int i=0;i<variantPlayerCounts.Length&&i<rules.Length;i++)
            variantPlayerCounts[i].text=playerCounts!=null&&playerCounts.TryGetValue(RankedRules.Ids[rules[i]],out int count)?count.ToString():"—";
    }
    public void ToggleSelection(MatchButton entry){
        var network=MatchNetworkManager.Instance;
        if(MatchStateManager.Instance.IsMatchFound||(network!=null&&network.IsMatchCommitted))return;
        if(network!=null)foreach(string queue in network.CurrentQueues)if(queue==entry.QueueType){
            network.SendLeaveQueue(queue);RefreshState();return;
        }
        if(!entry.IsAvailable){Tip("本规则的段位匹配暂未开放");return;}
        if(UserDataManager.Instance==null||UserDataManager.Instance.IsTourist){Tip("游客无法进行排位匹配，请先注册账号");return;}
        if(!entry.CanEnter()){Tip("当前段位无法加入该队列");return;}
        network?.SendJoinQueue(entry.QueueType);RefreshState();
    }
    public void RemoveSelection(MatchButton entry){MatchNetworkManager.Instance?.SendLeaveQueue(entry.QueueType);RefreshState();}
    public void ClearSelection(){CancelMatching();}
    public void CancelMatching(){MatchNetworkManager.Instance?.SendLeaveQueue();RefreshState();}
    private static void Tip(string message){NotificationManager.Instance?.ShowTip("匹配",false,message);}
    private int StateCode(){
        var state=MatchStateManager.Instance;var network=MatchNetworkManager.Instance;
        if(state.IsMatchFound||(network!=null&&network.IsMatchCommitted))return 4;
        if(network!=null&&network.IsLeavePending)return 3;
        if(state.IsQueueing)return 2;
        return network!=null&&network.IsJoinPending?1:0;
    }
    private void Update(){
        int code=StateCode();int version=MatchNetworkManager.Instance!=null?MatchNetworkManager.Instance.ViewVersion:0;
        if(code!=renderedState||version!=renderedVersion)RefreshState();
        if(code==2||code==3){int second=Mathf.FloorToInt(MatchStateManager.Instance.ElapsedTime);if(second!=lastSecond){lastSecond=second;elapsedText.text=$"{second/60:00}:{second%60:00}";}}
        if(queueOpacity!=null&&queueGroup.activeSelf){
            bool visible=selected.Count>0||code!=0;
            queueOpacity.alpha=Mathf.MoveTowards(queueOpacity.alpha,visible?1:0,Time.unscaledDeltaTime/transitionDuration);
            if(!visible&&!HasVisibleSlots()){queueGroup.SetActive(false);}
        }
        FitQueueSlots(true);
    }
    private bool HasVisibleSlots(){foreach(var slot in slots)if(slot.IsVisible)return true;return false;}
    private void FitQueueSlots(bool animate){
        if(queueGroup==null||!queueGroup.activeSelf)return;
        var layout=queueGroup.GetComponent<HorizontalLayoutGroup>();
        var slotLayout=slots[0].transform.parent.GetComponent<HorizontalLayoutGroup>();
        var container=(RectTransform)queueGroup.transform.parent.parent;
        var containerLayout=container.GetComponent<HorizontalOrVerticalLayoutGroup>();
        float available=container.rect.width-containerLayout.padding.horizontal-layout.padding.horizontal-slotLayout.padding.horizontal;
        if(queueStatus.activeSelf)available-=LayoutUtility.GetPreferredWidth((RectTransform)queueStatus.transform)+layout.spacing;
        float preferred=0;int count=0;
        foreach(var slot in slots)if(slot.IsVisible){preferred+=slot.PreferredWidth;count++;}
        available-=Mathf.Max(0,count-1)*slotLayout.spacing;
        if(preferred<=0||available<=0)return;
        float scale=Mathf.Min(1,available/preferred);
        foreach(var slot in slots)if(slot.IsVisible)slot.FitWidth(slot.PreferredWidth*scale,animate);
    }
    public void RefreshState(){
        var network=MatchNetworkManager.Instance;
        renderedState=StateCode();renderedVersion=network!=null?network.ViewVersion:0;
        selected.Clear();
        if(network!=null)foreach(string queue in network.CurrentQueues)
            foreach(var entry in entries)if(entry.QueueType==queue){selected.Add(entry);break;}
        int count=selected.Count;
        bool busy=count>0||renderedState!=0;
        bool animate=Application.isPlaying&&hasPresented;
        if(queueGroup!=null&&busy&&!queueGroup.activeSelf){queueGroup.SetActive(true);if(queueOpacity!=null)queueOpacity.alpha=animate?0:1;}
        selectionText.text=renderedState==4?"已找到对局":"匹配场次  "+count+" / 4";
        foreach(var slot in slots)if(slot.Entry==null||!selected.Contains(slot.Entry))slot.Bind(this,null,false,animate);
        foreach(var entry in selected){
            MatchQueueSlot target=null;
            foreach(var slot in slots)if(slot.Entry==entry){target=slot;break;}
            if(target==null)foreach(var slot in slots)if(slot.Entry==null){target=slot;break;}
            if(target==null)foreach(var slot in slots)if(!selected.Contains(slot.Entry)){target=slot;break;}
            if(target==null)continue;
            if(target.Entry!=entry)target.transform.SetAsLastSibling();
            bool pending=network!=null&&network.IsQueuePending(entry.QueueType);
            target.Bind(this,entry,renderedState!=4&&!pending,animate);
        }
        bool presenting=busy||HasVisibleSlots();
        if(queueGroup!=null)queueGroup.SetActive(presenting);
        if(!animate&&queueOpacity!=null)queueOpacity.alpha=busy?1:0;
        hasPresented=true;
        foreach(var entry in entries)entry.SetSelected(selected.Contains(entry));
        startButton.gameObject.SetActive(false);clearButton.gameObject.SetActive(false);
        selectionText.gameObject.SetActive(false);limitText.gameObject.SetActive(false);
        queueStatus.SetActive(presenting);joiningLabel.SetActive(renderedState==1);queueingLabel.SetActive(renderedState==2);cancelingLabel.SetActive(renderedState==3||(!busy&&presenting));foundLabel.SetActive(renderedState==4);
        cancelButton.gameObject.SetActive(busy&&renderedState!=4);cancelButton.interactable=renderedState!=3;
        elapsedText.text=renderedState==1?"连接中":renderedState==4?"即将进入":"00:00";lastSecond=-1;
        FitQueueSlots(animate);
    }
    private void RefreshRank(){
        string rule=RankedRules.Ids[activeRule];
        if(eloHelp!=null)eloHelp.SetActive(!RankedRules.IsGrade(rule));
        var user=UserDataManager.Instance;
        var rating=user==null?RankedRules.Get(null,rule):user.GetRating(rule);
        rankText.text=RankedRules.RankCaption(rating);
        pointsText.text=RankedRules.ScoreCaption(rating);
        progress.transform.parent.gameObject.SetActive(RankedRules.IsGrade(rating.rule));
        progress.fillAmount=RankedRules.Progress(rating);
        foreach(var entry in entries)entry.RefreshMask();
    }
}
