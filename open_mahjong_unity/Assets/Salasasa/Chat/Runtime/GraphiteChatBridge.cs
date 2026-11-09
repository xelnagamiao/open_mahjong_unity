using System;
using System.Collections.Generic;
using System.Linq;
using Salasasa.ChatLab;
using Salasasa.Home;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Salasasa.Chat
{
    [DefaultExecutionOrder(1000)]
    public sealed partial class GraphiteChatBridge : MonoBehaviour
    {
        public const string Preference = "Salasasa.Chat.Graphite.PlainText";
        public CompleteLobbyController lobby;
        public ChatMessageStore source;
        public RectTransform[] lobbyButtons;
        public TableChatLauncher tableLauncher;
        public ChatView view;
        public ChatArtStyle style;
        public CanvasGroup visibility;
        public Button plainButton, bubbleButton, popupDismiss, popupClose, copyButton, profileButton;
        public GameObject popup;
        public RectTransform popupCard;
        public TMP_Text popupTitle, popupDetail;
        public TMP_Text[] avatarInitials;
        public bool plainTextMessages = true;
        public bool PlainText => plainTextMessages;
        public int SelectedChannel => SelectedConversationKey == ChatConversationListModel.LobbyKey ? 0 : SelectedConversationKey == ChatConversationListModel.RoomKey ? 1 : 2;
        public string PopupKind { get; private set; } = "";
        public int VisibleMessageCount { get; private set; }
        public int ProfileUserId { get; private set; }
        public bool IsVisible => lobby.ChatOpen && visibility.alpha > .9f;
        public Action<string,int> ValidationSend;
        public int ValidationRoom;
        public bool ValidationMode => Array.IndexOf(Environment.GetCommandLineArgs(), "-graphite-validation") >= 0 || CompleteLobbyEnvironment.Has("-chat-hand-only");
        string PreferenceKey => ValidationMode ? Preference + ".Validation" : Preference;

        public sealed class Message
        {
            public string author, text, time, type, sourceContent, conversationKey;
            public int room, senderUserId;
            public bool mine;
            public bool HasPlayerSender => type == "Chat" || type == "Sercet" || type == "Conversation" && senderUserId > 0;
        }
        readonly List<Message> history = new List<Message>();
        readonly Dictionary<string,string> drafts = new Dictionary<string,string>(StringComparer.Ordinal);
        readonly List<Message> shown = new List<Message>();
        string query = "", observedRoom = "";
        int observedUser, layoutSide = -1;
        Vector3 layoutButtonPosition;
        Vector2 layoutSize;
        float messageWidth;
        bool refresh, previousVisible;
        bool visibilityTarget;
        float visibilityFrom, visibilityElapsed, visibilityDuration;
        Message popupMessage;
        Canvas layer;
        bool layoutAtTable;
        GameSceneMouseInputController registeredInput;
        public bool AtTable {get{string w=WindowsManager.Instance?.GetCurrentWindow();return w=="game"||w=="recordscene";}}
        public bool Available => !StreamerModeHelper.IsEnabled && !CreatePanel.IsAnyCreationPanelOpen;
        public Camera UiCamera => layer && layer.rootCanvas.renderMode != RenderMode.ScreenSpaceOverlay ? layer.rootCanvas.worldCamera : null;

        void Awake()
        {
            layer=GetComponent<Canvas>();
            plainTextMessages = PlayerPrefs.GetInt(PreferenceKey, plainTextMessages ? 1 : 0) == 1;
            source.MessageReceived += Receive;
            source.Cleared += ClearHistory;
            InitializeFriendList();
            InitializeConversations();
            foreach(var entry in source.ReceivedMessages)Receive(entry.type,entry.room,entry.content);
            plainButton.onClick.AddListener(()=>SetMessageStyle(true));
            bubbleButton.onClick.AddListener(()=>SetMessageStyle(false));
            view.close.onClick.AddListener(lobby.CloseChat);
            view.tabs[0].onClick.AddListener(ShowChats);
            view.tabs[1].onClick.AddListener(ShowVenues);
            view.tabs[2].onClick.AddListener(ShowFriends);
            view.search.onValueChanged.AddListener(value=>{query=value;ResetFriendScroll();RefreshList();});
            view.composer.onValueChanged.AddListener(value=>{if(!refresh){drafts[DraftKey]=value;UpdateComposer();}});
            view.send.onClick.AddListener(Send);
            view.composer.onSubmit.AddListener(_=>{Send();StartCoroutine(FocusComposer());});
            foreach(var row in view.messages){var r=row;r.button.onClick.AddListener(()=>ShowMessage(r));r.avatarButton.onClick.AddListener(()=>ShowProfile(r));r.nameButton.onClick.AddListener(()=>ShowProfile(r));}
            popupDismiss.onClick.AddListener(HidePopup);popupClose.onClick.AddListener(HidePopup);
            copyButton.onClick.AddListener(()=>{if(popupMessage!=null)GUIUtility.systemCopyBuffer=popupMessage.text;HidePopup();});
            profileButton.onClick.AddListener(()=>{
                if(ProfileUserId>0)ProfileOnClick.OpenPlayerInfo(ProfileUserId);
                else if(popupMessage?.HasPlayerSender==true)ProfileOnClick.OpenPlayerInfoByName(popupMessage.author);
                HidePopup();
            });
            HidePopup();Refresh();
        }
        void OnDestroy()
        {
            DisposeFriendList();
            DisposeConversations();
            if(source){source.MessageReceived-=Receive;source.Cleared-=ClearHistory;}
            if(registeredInput)registeredInput.UnregisterAdditionalUiRoot((RectTransform)view.transform);
        }
        void ClearHistory(){history.Clear();drafts.Clear();ClearConversationState();view.composer.SetTextWithoutNotify("");tableLauncher?.Clear();Refresh();}
        void UpdateVisibility(bool visible)
        {
            if(visibilityTarget!=visible){
                visibilityTarget=visible;
                visibilityFrom=visibility.alpha;
                visibilityElapsed=0;
                visibilityDuration=WindowFadeTransition.DurationSeconds*Mathf.Abs((visible?1:0)-visibilityFrom);
            }
            visibilityElapsed+=Time.unscaledDeltaTime;
            float t=visibilityDuration>0?Mathf.Clamp01(visibilityElapsed/visibilityDuration):1;
            float eased=t*t*(3-2*t);
            visibility.alpha=Mathf.Lerp(visibilityFrom,visible?1:0,eased);
            visibility.interactable=visible||visibility.alpha>0;
            visibility.blocksRaycasts=visible;
        }
        void OnDisable()
        {
            visibilityTarget=previousVisible=false;
            visibilityFrom=visibilityElapsed=visibilityDuration=0;
            if(visibility){visibility.alpha=0;visibility.interactable=visibility.blocksRaycasts=false;}
        }
        void LateUpdate()
        {
            string window=WindowsManager.Instance?.GetCurrentWindow();
            bool visible=lobby.ChatOpen && window!="login" && Available;
            UpdateVisibility(visible);
            if(AtTable && GameSceneMouseInputController.Instance && registeredInput!=GameSceneMouseInputController.Instance){
                registeredInput=GameSceneMouseInputController.Instance;
                registeredInput.RegisterAdditionalUiRoot((RectTransform)view.transform);
            }
            var user=UserDataManager.Instance;
            if(user && user.UserId!=observedUser){if(observedUser!=0)source.Clear();observedUser=user.UserId;FriendRelationCache.ResetForUser(user.UserId);Refresh();}
            string room=user?.ChatRoomId??UserDataManager.ROOM_ID_NONE;
            if(room!=observedRoom){RoomChanged(observedRoom);observedRoom=room;Refresh();}
            PollFriends(visible);
            PollConversations(visible);
            if(!visible){
                if(previousVisible){
                    HidePopup();view.composer.DeactivateInputField();
                    var events=UnityEngine.EventSystems.EventSystem.current;
                    if(events && events.currentSelectedGameObject && events.currentSelectedGameObject.transform.IsChildOf(view.transform))events.SetSelectedGameObject(null);
                }
                previousVisible=false;return;
            }
            var canvasRect=(RectTransform)lobby.artwork.canvas.transform;
            int side=lobby.headerNavigation?lobby.headerNavigation.Side:0;
            var button=LobbyButton;
            Vector3 buttonPosition=button?button.position:Vector3.zero;
            if(!previousVisible||layoutAtTable!=AtTable||layoutSize!=canvasRect.rect.size||layoutSide!=side||layoutButtonPosition!=buttonPosition){layoutAtTable=AtTable;layoutSize=canvasRect.rect.size;layoutSide=side;layoutButtonPosition=buttonPosition;Layout();Canvas.ForceUpdateCanvases();UpdatePresencePosition();RenderMessages(false);HidePopup();}
            if(!previousVisible){
                Refresh();MarkSelectedRead();
                // On mobile, opening chat should not summon the keyboard; wait for a tap on the composer.
                if(!Application.isMobilePlatform)view.composer.ActivateInputField();
            }
            if(Mathf.Abs(view.messageContent.rect.width-messageWidth)>.5f)RenderMessages(false);
            UpdateComposer();previousVisible=true;
        }
        public RectTransform LobbyButton => lobbyButtons!=null && lobbyButtons.Length>0 ? lobbyButtons[0] : null;
        public void Layout()
        {
            var root=(RectTransform)view.transform;
            var parent=(RectTransform)root.parent;
            var bounds=parent.rect;
            if(AtTable){
                root.anchorMin=root.anchorMax=root.pivot=Vector2.zero;
                root.anchoredPosition=new Vector2(16,76);
                root.sizeDelta=new Vector2(Mathf.Min(900,bounds.width-32),Mathf.Min(620,bounds.height-140));
                return;
            }
            var inset=lobby.headerNavigation?lobby.headerNavigation.Insets:Vector4.zero;
            float left=bounds.xMin+Mathf.Max(28,inset.x+16),right=bounds.xMax-Mathf.Max(28,inset.z+16);
            float top=bounds.yMax-Mathf.Max(16,inset.y+16),bottom=bounds.yMin+Mathf.Max(102,inset.w+16);
            float center=right;
            var button=LobbyButton;
            if(button){
                var corners=new Vector3[4];button.GetWorldCorners(corners);
                var points=corners.Select(p=>(Vector2)parent.InverseTransformPoint(p)).ToArray();
                bottom=Mathf.Max(bounds.yMin+inset.w+16,points.Max(p=>p.y)+12);
                center=(points.Min(p=>p.x)+points.Max(p=>p.x))*.5f;
            }
            float width=Mathf.Max(1,Mathf.Min(1040,right-left));
            center=Mathf.Clamp(center,left+width*.5f,right-width*.5f);
            root.anchorMin=root.anchorMax=Vector2.one*.5f;root.pivot=new Vector2(.5f,0);
            root.sizeDelta=new Vector2(width,Mathf.Max(1,top-bottom));
            root.anchoredPosition=new Vector2(center,bottom)-bounds.center;
        }
        void UpdatePresencePosition()
        {
            if (!view.status || !view.status.gameObject.activeSelf) return;
            var title = view.title.rectTransform;
            float nameWidth = view.title.GetPreferredValues(view.title.text).x;
            view.status.rectTransform.anchoredPosition = new Vector2(Mathf.Min(nameWidth + 4, Mathf.Max(0, title.rect.width - 14)), -2);
        }
        public void SetMessageStyle(bool plain)
        {
            plainTextMessages=plain;PlayerPrefs.SetInt(PreferenceKey,plain?1:0);PlayerPrefs.Save();
            HidePopup();Refresh();
        }
        public void SelectChannel(int channel)
        {
            SelectConversation(channel == 0 ? ChatConversationListModel.LobbyKey : ChatConversationListModel.RoomKey);
        }
        public int RoomChannel => ValidationMode&&ValidationRoom>0?ValidationRoom:int.TryParse(UserDataManager.Instance?.ChatRoomId,out int id)?id:0;
        public async void Send()
        {
            string text=view.composer.text;
            if(string.IsNullOrWhiteSpace(text)||!SelectedCanCompose||WaitingForSend)return;
            if(SelectedChannel==1 && !SelectedConnected){
                source.ShowChatMessage("RoomSendFailed",RoomChannel,PlatformText.Get(RoomChannel<=0?"发送失败：未进入房间":"发送失败：房间聊天尚未连接，请稍后重试"));
                RenderMessages(true);return;
            }
            if(SelectedChannel<2 && ValidationMode&&ValidationSend!=null)ValidationSend(text,SelectedChannel==0?0:RoomChannel);
            else {
                if(!ChatManager.Instance || !SelectedConnected)return;
                if(SelectedChannel<2) {
                    int userId=UserDataManager.Instance.UserId;
                    string draftKey=DraftKey;
                    int channel=SelectedChannel==0?0:RoomChannel;
                    waitingLegacySend=true;UpdateComposer();
                    bool sent=await ChatManager.Instance.SendChatMessageAsync(text,channel);
                    if(!this || !UserDataManager.Instance || UserDataManager.Instance.UserId!=userId)return;
                    waitingLegacySend=false;
                    if(sent) {
                        if(drafts.TryGetValue(draftKey,out var saved)&&saved==text)drafts[draftKey]="";
                        if(DraftKey==draftKey&&view.composer.text==text)view.composer.SetTextWithoutNotify("");
                    } else GameHost.Current.ShowTip(PlatformText.Get("聊天"),false,PlatformText.Get("消息发送未确认，请核对聊天记录后重试"));
                    UpdateComposer();return;
                }
                else {
                    BeginConversationSend(text);
                    ChatManager.Instance.SendConversationMessage(SelectedConversationKey,text);
                    UpdateComposer();return;
                }
            }
            drafts[DraftKey]="";view.composer.SetTextWithoutNotify("");view.composer.ActivateInputField();UpdateComposer();
        }
        System.Collections.IEnumerator FocusComposer(){yield return null;if(IsVisible)view.composer.ActivateInputField();}
        public void Receive(string type,int room,string content)
        {
            if(type=="sendChatOk"||string.IsNullOrEmpty(content))return;
            string name=type=="serverMessage"?PlatformText.Get("服务器公告"):"系统",body=content.Trim();
            if(type=="serverMessage")body=PlatformText.Get(body);
            if(type=="RoomJoined"||type=="RoomLeft")body=PlatformText.Get(type=="RoomJoined"?"加入房间 {id}":"退出房间 {id}").Replace("{id}",room.ToString());
            if(type=="Chat"||type=="Sercet"){
                int split=body.IndexOf(": ",StringComparison.Ordinal);
                if(split>0){name=body.Substring(0,split);body=body.Substring(split+2);}
            }
            var message=new Message{author=name,text=body,type=type,room=room,time=DateTime.Now.ToString("HH:mm"),mine=name==UserDataManager.Instance?.Username,sourceContent=content.Trim(),conversationKey=room==0&&type!="RoomSendFailed"?ChatConversationListModel.LobbyKey:ChatConversationListModel.RoomKey};
            history.Add(message);
            if(tableLauncher)tableLauncher.Receive(message);
            while(history.Count(m=>m.conversationKey==message.conversationKey)>50)history.RemoveAt(history.FindIndex(m=>m.conversationKey==message.conversationKey));
            while(history.Count>150)history.RemoveAt(0);
            ReceiveLegacyUnread(message);
            RenderMessages(view.messageScroll.verticalNormalizedPosition<.08f);RefreshList();
        }
        public void Refresh()
        {
            refresh=true;
            RefreshConversationHeader();
            for(int i=0;i<view.tabs.Length;i++){view.tabIndicators[i].gameObject.SetActive(i==SidebarTab);view.tabLabels[i].fontStyle=FontStyles.Normal;view.tabLabels[i].fontWeight=FontWeight.Regular;}
            if(view.sectionTitle)view.sectionTitle.text=PlatformText.Get(SidebarTab==2?"好友":SidebarTab==1?"基地":"聊天");
            plainButton.targetGraphic.color=PlainText?style.listSelection:Color.clear;
            bubbleButton.targetGraphic.color=!PlainText?style.listSelection:Color.clear;
            RefreshList();RenderMessages(false);UpdateComposer();refresh=false;
        }
        void RefreshList()
        {
            if(FriendsVisible){RefreshFriends();return;}
            RefreshConversationList();
        }
        void UpdateComposer()
        {
            bool channel=SelectedCanCompose;
            bool connected=SelectedConnected;
            view.composer.interactable=channel;
            view.send.interactable=channel&&(SelectedChannel==1||connected)&&!WaitingForSend&&!string.IsNullOrWhiteSpace(view.composer.text);
            view.sendFill.color=view.send.interactable?style.accent:style.row;
            view.send.GetComponentInChildren<TMP_Text>().color=view.send.interactable?style.accentText:style.secondary;
            ((TMP_Text)view.composer.placeholder).text=PlatformText.Get(!channel?"此会话仅供阅读":SelectedChannel==1?"输入消息…":!connected?"未连接聊天服务器":"输入消息…");
        }
        public Color SenderColor(string author,bool mine=false)
        {
            if(mine)return style.ownSender;uint hash=2166136261;
            unchecked{foreach(char c in author??"")hash=(hash^c)*16777619;}
            return style.senderColors[hash%(uint)style.senderColors.Length];
        }
        void ShowMessage(ChatMessageRow row)
        {
            if(row.boundIndex<0||row.boundIndex>=shown.Count)return;
            popupMessage=shown[row.boundIndex];PopupKind="message";popup.SetActive(true);
            popupTitle.gameObject.SetActive(false);popupDetail.gameObject.SetActive(false);popupClose.gameObject.SetActive(false);
            profileButton.gameObject.SetActive(false);copyButton.gameObject.SetActive(true);
            popupCard.sizeDelta=new Vector2(152,46);PlacePopup(row.bubbleRect);Place((RectTransform)copyButton.transform,6,6,140,34);
        }
        void ShowProfile(ChatMessageRow row)
        {
            if(row.boundIndex<0||row.boundIndex>=shown.Count)return;
            if(!shown[row.boundIndex].HasPlayerSender)return;
            popupMessage=shown[row.boundIndex];ProfileUserId=popupMessage.senderUserId>0?popupMessage.senderUserId:popupMessage.mine?UserDataManager.Instance.UserId:0;
            PopupKind="profile";popup.SetActive(true);popupTitle.gameObject.SetActive(true);popupDetail.gameObject.SetActive(true);popupClose.gameObject.SetActive(true);
            popupTitle.text=popupMessage.author;popupDetail.text=ProfileUserId>0?"UID: "+ProfileUserId:"";
            profileButton.gameObject.SetActive(true);copyButton.gameObject.SetActive(false);
            popupCard.sizeDelta=new Vector2(320,150);PlacePopup(row.author.rectTransform);
        }
        void PlacePopup(RectTransform anchor)
        {
            var bounds=(RectTransform)popup.transform;var corners=new Vector3[4];anchor.GetWorldCorners(corners);
            Vector2 p=bounds.InverseTransformPoint(corners[2]);
            popupCard.anchorMin=popupCard.anchorMax=bounds.pivot;popupCard.pivot=new Vector2(0,1);
            popupCard.anchoredPosition=new Vector2(Mathf.Clamp(p.x+8,bounds.rect.xMin+8,bounds.rect.xMax-popupCard.rect.width-8),Mathf.Clamp(p.y,bounds.rect.yMin+popupCard.rect.height+8,bounds.rect.yMax-8));
        }
        public void HidePopup(){popup.SetActive(false);PopupKind="";}
        static void Place(RectTransform r,float x,float y,float w,float h){r.anchorMin=r.anchorMax=r.pivot=new Vector2(0,1);r.anchoredPosition=new Vector2(x,-y);r.sizeDelta=new Vector2(w,h);}
        public void RenderMessages(bool bottom)
        {
            float scroll=view.messageScroll.verticalNormalizedPosition;
            shown.Clear();
            if(SelectedChannel<2)shown.AddRange(history.Where(m=>m.conversationKey==SelectedConversationKey));
            else shown.AddRange(source.GetConversationMessages(SelectedConversationKey).Select(ToDisplayMessage));
            var v=view;var messages=shown;int start=Mathf.Max(0,messages.Count-v.messages.Length);float y=6;
            float width=v.messageContent.rect.width;messageWidth=width;VisibleMessageCount=Mathf.Min(messages.Count,v.messages.Length);
            for(int i=0;i<v.messages.Length;i++){
                var r=v.messages[i];int n=i+start;r.gameObject.SetActive(n<messages.Count);if(n>=messages.Count)continue;
                var m=messages[n];r.boundIndex=n;r.author.gameObject.SetActive(true);r.button.interactable=true;r.author.text=m.author;r.time.gameObject.SetActive(true);r.body.text=m.text;r.body.richText=false;r.body.alignment=TextAlignmentOptions.TopLeft;
                r.avatarButton.enabled=r.nameButton.enabled=m.HasPlayerSender;
                r.portrait.raycastTarget=r.author.raycastTarget=m.HasPlayerSender;
                if(m.type=="LoginTime"||m.type=="RoomJoined"||m.type=="RoomLeft"){
                    var line=(RectTransform)r.transform;line.anchoredPosition=new Vector2(0,-y);line.sizeDelta=new Vector2(0,v.messageLineHeight);
                    r.author.gameObject.SetActive(false);r.time.gameObject.SetActive(false);r.portrait.gameObject.SetActive(false);r.button.interactable=false;
                    r.bubbleRect.anchoredPosition=Vector2.zero;r.bubbleRect.sizeDelta=new Vector2(width,v.messageLineHeight);r.bubble.color=Color.clear;
                    r.body.rectTransform.offsetMin=r.body.rectTransform.offsetMax=Vector2.zero;r.body.alignment=TextAlignmentOptions.Center;
                    r.body.fontSize=v.timestampFontSize;r.body.fontStyle=FontStyles.Normal;r.body.fontWeight=FontWeight.Regular;r.body.color=v.muted;
                    r.body.text="────────  "+m.text+"  ────────";y+=v.messageLineHeight+v.messageGap;continue;
                }
                r.portrait.gameObject.SetActive(!PlainText);avatarInitials[i].text=m.author.Length>0?m.author.Substring(0,1):"";
                r.author.richText=false;r.author.overflowMode=TextOverflowModes.Ellipsis;
                r.author.font=v.senderFont;r.author.fontSize=PlainText?v.senderFontSize:13;r.author.fontStyle=FontStyles.Normal;r.author.fontWeight=FontWeight.Regular;r.author.textWrappingMode=TextWrappingModes.NoWrap;
                r.author.color=m.author=="系统"?style.secondary:SenderColor(m.author,m.mine);
                r.body.color=m.type=="False"||m.type=="RoomSendFailed"?new Color32(236,166,166,255):m.type=="serverMessage"?new Color32(235,143,211,255):v.text;r.body.fontSize=v.messageFontSize;r.body.fontStyle=FontStyles.Normal;r.body.fontWeight=FontWeight.Regular;
                var face=r.body.font.faceInfo;r.body.lineSpacing=(v.messageLineHeight-face.lineHeight*v.messageFontSize/face.pointSize*face.scale)/(v.messageFontSize*.01f);r.body.paragraphSpacing=0;
                r.time.text=m.time;r.time.fontSize=v.timestampFontSize;r.time.color=v.muted;r.time.fontStyle=FontStyles.Normal;
                var tr=r.time.rectTransform;tr.anchorMin=tr.anchorMax=tr.pivot=new Vector2(0,1);tr.sizeDelta=new Vector2(48,20);
                if(PlainText){
                    r.author.text=m.author+"：";r.author.alignment=TextAlignmentOptions.TopLeft;
                    float nw=Mathf.Clamp(r.author.GetPreferredValues(r.author.text).x+3,37,126),bx=nw+6,bw=Mathf.Max(100,width-bx-56);
                    float h=Mathf.Max(v.messageLineHeight,Mathf.Ceil(r.body.GetPreferredValues(m.text,bw,0).y));
                    var rect=(RectTransform)r.transform;rect.anchoredPosition=new Vector2(0,-y);rect.sizeDelta=new Vector2(0,h);
                    r.author.rectTransform.anchoredPosition=Vector2.zero;r.author.rectTransform.sizeDelta=new Vector2(nw,v.messageLineHeight);
                    r.bubbleRect.anchoredPosition=new Vector2(bx,0);r.bubbleRect.sizeDelta=new Vector2(bw,h);r.bubble.color=Color.clear;
                    r.body.rectTransform.offsetMin=r.body.rectTransform.offsetMax=Vector2.zero;
                    r.time.alignment=TextAlignmentOptions.TopRight;tr.anchoredPosition=new Vector2(width-48,-4);y+=h+v.messageGap;
                }else{
                    r.body.rectTransform.offsetMin=new Vector2(10,6);r.body.rectTransform.offsetMax=new Vector2(-10,-6);
                    float max=Mathf.Max(96,Mathf.Min(460,width-66)),wanted=r.body.GetPreferredValues(m.text,max-20,0).x+21,bw=Mathf.Clamp(wanted,56,max),bh=Mathf.Max(34,r.body.GetPreferredValues(m.text,bw-20,0).y+14);
                    var rect=(RectTransform)r.transform;rect.anchoredPosition=new Vector2(0,-y);rect.sizeDelta=new Vector2(0,bh+19);
                    r.bubbleRect.sizeDelta=new Vector2(bw,bh);r.bubbleRect.anchoredPosition=new Vector2(m.mine?width-bw-46:46,-19);r.bubble.color=m.mine?v.selected:v.row;
                    r.portrait.rectTransform.anchoredPosition=new Vector2(m.mine?width-36:0,-19);
                    float aw=Mathf.Min(max-58,r.author.GetPreferredValues(m.author).x+2);
                    r.author.rectTransform.sizeDelta=new Vector2(aw,19);r.author.rectTransform.anchoredPosition=new Vector2(m.mine?width-aw-104:46,0);r.author.alignment=TextAlignmentOptions.TopLeft;
                    r.time.alignment=m.mine?TextAlignmentOptions.TopRight:TextAlignmentOptions.TopLeft;tr.anchoredPosition=new Vector2(m.mine?width-94:54+aw,-1);y+=bh+27;
                }
            }
            v.messageContent.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical,y+4);Canvas.ForceUpdateCanvases();v.messageScroll.verticalNormalizedPosition=bottom?0:scroll;
        }
    }
}

