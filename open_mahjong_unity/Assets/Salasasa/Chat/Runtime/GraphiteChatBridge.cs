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
        public CompleteLobbyController lobby;
        public ChatMessageStore source;
        public RectTransform[] lobbyButtons;
        public TableChatLauncher tableLauncher;
        public ChatView view;
        public ChatArtStyle style;
        public CanvasGroup visibility;
        public Button popupDismiss, popupClose, copyButton, profileButton;
        public GameObject popup;
        public RectTransform popupCard;
        public TMP_Text popupTitle, popupDetail;
        public TMP_Text[] avatarInitials;
        public int SelectedChannel => SelectedConversationKey == ChatConversationListModel.LobbyKey ? 0 : SelectedConversationKey == ChatConversationListModel.RoomKey ? 1 : 2;
        public string PopupKind { get; private set; } = "";
        public int VisibleMessageCount { get; private set; }
        public int ProfileUserId { get; private set; }
        public bool IsVisible => lobby.ChatOpen && visibility.alpha > .9f;
        public Action<string,int> ValidationSend;
        public int ValidationRoom;
        public bool ValidationMode => Array.IndexOf(Environment.GetCommandLineArgs(), "-graphite-validation") >= 0 || CompleteLobbyEnvironment.Has("-chat-hand-only");

        public sealed class Message
        {
            public string author, text, time, type, sourceContent, conversationKey;
            public int room, senderUserId, profileImageId;
            public bool mine;
            public bool HasPlayerSender => type == "Chat" || type == "Sercet" || type == "Conversation" && senderUserId > 0;
        }
        readonly List<Message> history = new List<Message>();
        readonly Dictionary<string,string> drafts = new Dictionary<string,string>(StringComparer.Ordinal);
        sealed class MessageRowBinding
        {
            public ChatMessageRow row;
            public TMP_Text initial;
            public object source;
            public Message display;
            public float height;
        }
        readonly List<MessageRowBinding> shown = new List<MessageRowBinding>();
        readonly Dictionary<object, MessageRowBinding> boundRows = new Dictionary<object, MessageRowBinding>();
        readonly Stack<MessageRowBinding> freeRows = new Stack<MessageRowBinding>();
        readonly List<object> requestedMessages = new List<object>();
        readonly HashSet<object> requestedKeys = new HashSet<object>();
        bool headerDirty, listDirty, messagesDirty, messageStyleDirty, scrollToEnd, followIncoming;
        string query = "", observedRoom = "";
        int observedUser, layoutSide = -1;
        Vector3 layoutButtonPosition;
        Vector2 layoutSize;
        float messageWidth;
        int renderedSelfPortrait;
        bool previousVisible;
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
            source.MessageReceived += Receive;
            source.Cleared += ClearHistory;
            InitializeFriendList();
            InitializeConversations();
            InitializeMessageRows();
            foreach(var entry in source.ReceivedMessages)Receive(entry,false);
            view.close.onClick.AddListener(lobby.CloseChat);
            view.tabs[0].onClick.AddListener(ShowChats);
            view.tabs[1].onClick.AddListener(ShowVenues);
            view.tabs[2].onClick.AddListener(ShowFriends);
            view.search.onValueChanged.AddListener(value=>{query=value;ResetFriendScroll();RefreshList();});
            view.composer.onValueChanged.AddListener(value=>{drafts[DraftKey]=value;UpdateComposer();});
            view.send.onClick.AddListener(Send);
            view.composer.onSubmit.AddListener(_=>{Send();StartCoroutine(FocusComposer());});
            foreach(var row in view.messages){var r=row;r.button.onClick.AddListener(()=>ShowMessage(r));r.avatarButton.onClick.AddListener(()=>ShowProfile(r));r.nameButton.onClick.AddListener(()=>ShowProfile(r));}
            popupDismiss.onClick.AddListener(HidePopup);popupClose.onClick.AddListener(HidePopup);
            copyButton.onClick.AddListener(()=>{if(popupMessage!=null)ClipboardUtility.Copy(popupMessage.text);HidePopup();});
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
            bool visible=CanReadSelected;
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
            int selfPortrait = user ? user.ProfileImageId : 0;
            if (selfPortrait != renderedSelfPortrait) {
                renderedSelfPortrait = selfPortrait;
                messageStyleDirty = messagesDirty = true;
            }
            if (headerDirty) {
                headerDirty = false;
                RefreshConversationHeader();
                for(int i=0;i<view.tabs.Length;i++){view.tabIndicators[i].gameObject.SetActive(i==SidebarTab);view.tabLabels[i].fontStyle=FontStyles.Normal;view.tabLabels[i].fontWeight=FontWeight.Regular;}
                if(view.sectionTitle)view.sectionTitle.text=PlatformText.Get(SidebarTab==2?"好友":SidebarTab==1?"基地":"聊天");
            }
            if (listDirty) {
                listDirty = false;
                if (FriendsVisible) RefreshFriends(); else RefreshConversationList();
            }
            if (messagesDirty) FlushMessages();
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
            => Receive(new ChatMessageStore.ReceivedMessage{type=type,room=room,content=content},true);
        void Receive(ChatMessageStore.ReceivedMessage entry) => Receive(entry, true);

        void Receive(ChatMessageStore.ReceivedMessage entry,bool notify)
        {
            string type=entry.type,content=entry.content;
            int room=entry.room;
            if(type=="sendChatOk"||string.IsNullOrEmpty(content))return;
            string name=type=="serverMessage"?PlatformText.Get("服务器公告"):"系统",body=content.Trim();
            if(type=="serverMessage")body=PlatformText.Get(body);
            if(type=="RoomJoined"||type=="RoomLeft")body=PlatformText.Get(type=="RoomJoined"?"加入房间 {id}":"退出房间 {id}").Replace("{id}",room.ToString());
            if(type=="Chat"||type=="Sercet"){
                int split=body.IndexOf(": ",StringComparison.Ordinal);
                if(split>0){name=body.Substring(0,split);body=body.Substring(split+2);}
            }
            var user=UserDataManager.Instance;
            bool mine=entry.senderUserId>0?entry.senderUserId==user?.UserId:name==user?.Username;
            var message=new Message{author=name,text=body,type=type,room=room,time=DateTime.Now.ToString("HH:mm"),mine=mine,
                senderUserId=entry.senderUserId,profileImageId=entry.profileImageId,sourceContent=content.Trim(),
                conversationKey=room==0&&type!="RoomSendFailed"?ChatConversationListModel.LobbyKey:ChatConversationListModel.RoomKey};
            history.Add(message);
            if(notify && tableLauncher)tableLauncher.Receive(message);
            while(history.Count(m=>m.conversationKey==message.conversationKey)>50)history.RemoveAt(history.FindIndex(m=>m.conversationKey==message.conversationKey));
            while(history.Count>150)history.RemoveAt(0);
            if(notify)ReceiveLegacyUnread(message);
            if (message.conversationKey == SelectedConversationKey) QueueIncomingMessage();
            RefreshList();
        }
        public void Refresh()
        {
            headerDirty = listDirty = messagesDirty = true;
        }
        void RefreshList() => listDirty = true;
        void UpdateComposer()
        {
            if (!CanReadSelected) return;
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
            popupMessage=shown[row.boundIndex].display;PopupKind="message";popup.SetActive(true);
            popupTitle.gameObject.SetActive(false);popupDetail.gameObject.SetActive(false);popupClose.gameObject.SetActive(false);
            profileButton.gameObject.SetActive(false);copyButton.gameObject.SetActive(true);
            popupCard.sizeDelta=new Vector2(152,46);PlacePopup(row.bubbleRect);Place((RectTransform)copyButton.transform,6,6,140,34);
        }
        void ShowProfile(ChatMessageRow row)
        {
            if(row.boundIndex<0||row.boundIndex>=shown.Count)return;
            if(!shown[row.boundIndex].display.HasPlayerSender)return;
            popupMessage=shown[row.boundIndex].display;ProfileUserId=popupMessage.senderUserId>0?popupMessage.senderUserId:popupMessage.mine?UserDataManager.Instance.UserId:0;
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
            messagesDirty = true;
            scrollToEnd |= bottom;
        }

        void InitializeMessageRows()
        {
            for (int i = view.messages.Length - 1; i >= 0; i--)
                freeRows.Push(new MessageRowBinding { row = view.messages[i], initial = avatarInitials[i] });
        }

        void QueueIncomingMessage()
        {
            messagesDirty = true;
            followIncoming = true;
        }

        void CollectMessages()
        {
            requestedMessages.Clear();
            int capacity = view.messages.Length;
            if (SelectedChannel < 2) {
                for (int i = history.Count - 1; i >= 0 && requestedMessages.Count < capacity; i--)
                    if (history[i].conversationKey == SelectedConversationKey) requestedMessages.Add(history[i]);
                requestedMessages.Reverse();
            } else {
                var entries = source.GetConversationMessages(SelectedConversationKey);
                for (int i = Mathf.Max(0, entries.Count - capacity); i < entries.Count; i++)
                    requestedMessages.Add(entries[i]);
            }
        }

        void FlushMessages()
        {
            messagesDirty = false;
            CollectMessages();
            var content = view.messageContent;
            float width = content.rect.width;
            bool rebind = messageStyleDirty || Mathf.Abs(width - messageWidth) > .5f;
            messageStyleDirty = false;
            messageWidth = width;
            var viewport = view.messageScroll.viewport ? view.messageScroll.viewport : (RectTransform)view.messageScroll.transform;
            float viewportHeight = viewport.rect.height;
            float oldRange = Mathf.Max(0, content.rect.height - viewportHeight);
            float offset = content.anchoredPosition.y;
            bool bottom = scrollToEnd || (followIncoming && (shown.Count == 0 || offset >= oldRange * .92f));
            if (scrollToEnd) view.messageScroll.StopMovement();
            scrollToEnd = followIncoming = false;

            requestedKeys.Clear();
            requestedKeys.UnionWith(requestedMessages);
            MessageRowBinding anchor = null;
            float anchorOffset = 0;
            foreach (var binding in shown) {
                if (!requestedKeys.Contains(binding.source)) {
                    boundRows.Remove(binding.source);
                    binding.source = binding.display = null;
                    freeRows.Push(binding);
                    continue;
                }
                float top = -((RectTransform)binding.row.transform).anchoredPosition.y;
                if (!bottom && anchor == null && top + binding.height > offset) {
                    anchor = binding;
                    anchorOffset = offset - top;
                }
            }

            shown.Clear();
            float y = 6;
            foreach (var key in requestedMessages) {
                bool existing = boundRows.TryGetValue(key, out var binding);
                if (!existing) {
                    binding = freeRows.Pop();
                    binding.source = key;
                    binding.row.gameObject.SetActive(true);
                    boundRows.Add(key, binding);
                }
                if (!existing || rebind) {
                    binding.display = key as Message ?? ToDisplayMessage((ConversationChatMessage)key);
                    binding.height = BindMessage(binding, width, y);
                } else {
                    var rect = (RectTransform)binding.row.transform;
                    if (!Mathf.Approximately(rect.anchoredPosition.y, -y)) rect.anchoredPosition = new Vector2(0, -y);
                }
                if (binding == anchor) offset = y + anchorOffset;
                binding.row.boundIndex = shown.Count;
                shown.Add(binding);
                y += binding.height;
            }
            VisibleMessageCount = shown.Count;
            foreach (var unused in freeRows) {
                if (unused.row.gameObject.activeSelf) unused.row.gameObject.SetActive(false);
                unused.row.boundIndex = -1;
            }
            float height = y + 4;
            if (!Mathf.Approximately(content.rect.height, height))
                content.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, height);
            float range = Mathf.Max(0, height - viewportHeight);
            float target = bottom ? range : Mathf.Clamp(offset, 0, range);
            if (!Mathf.Approximately(target, content.anchoredPosition.y))
                content.anchoredPosition = new Vector2(content.anchoredPosition.x, target);
        }

        float BindMessage(MessageRowBinding binding, float width, float top)
        {
            var row = binding.row;
            var message = binding.display;
            bool divider = message.type == "LoginTime" || message.type == "RoomJoined" || message.type == "RoomLeft";
            row.author.gameObject.SetActive(!divider);
            row.time.gameObject.SetActive(!divider);
            row.portrait.gameObject.SetActive(!divider);
            row.button.interactable = !divider;
            row.avatarButton.enabled = row.nameButton.enabled = message.HasPlayerSender;
            row.portrait.raycastTarget = row.author.raycastTarget = message.HasPlayerSender;
            row.body.richText = false;
            row.body.fontStyle = FontStyles.Normal;
            row.body.fontWeight = FontWeight.Regular;
            if (divider) {
                var line = (RectTransform)row.transform;
                line.anchoredPosition = new Vector2(0, -top);
                line.sizeDelta = new Vector2(0, view.messageLineHeight);
                row.bubbleRect.anchoredPosition = Vector2.zero;
                row.bubbleRect.sizeDelta = new Vector2(width, view.messageLineHeight);
                row.bubble.color = Color.clear;
                row.body.rectTransform.offsetMin = row.body.rectTransform.offsetMax = Vector2.zero;
                row.body.alignment = TextAlignmentOptions.Center;
                row.body.fontSize = view.timestampFontSize;
                row.body.color = view.muted;
                row.body.text = "────────  " + message.text + "  ────────";
                binding.initial.gameObject.SetActive(false);
                return view.messageLineHeight + view.messageGap;
            }

            int profileId = message.profileImageId;
            if (message.HasPlayerSender && profileId <= 0)
                profileId = message.mine && renderedSelfPortrait > 0 ? renderedSelfPortrait : ConfigManager.DefaultAvatarItemId;
            bool hasPortrait = ConfigManager.SetProfilePortrait(row.portrait, profileId, style.row);
            binding.initial.gameObject.SetActive(!hasPortrait);
            if (!hasPortrait) binding.initial.text = FirstCharacter(message.author);
            row.author.text = message.author;
            row.author.richText = false;
            row.author.overflowMode = TextOverflowModes.Ellipsis;
            row.author.font = view.senderFont;
            row.author.fontSize = 13;
            row.author.fontStyle = FontStyles.Normal;
            row.author.fontWeight = FontWeight.Regular;
            row.author.textWrappingMode = TextWrappingModes.NoWrap;
            row.author.color = message.author == "系统" ? style.secondary : SenderColor(message.author, message.mine);

            row.body.text = message.text;
            row.body.alignment = TextAlignmentOptions.TopLeft;
            row.body.color = message.type == "False" || message.type == "RoomSendFailed" ? new Color32(236,166,166,255)
                : message.type == "serverMessage" ? new Color32(235,143,211,255) : view.text;
            row.body.fontSize = view.messageFontSize;
            var face = row.body.font.faceInfo;
            row.body.lineSpacing = (view.messageLineHeight - face.lineHeight * view.messageFontSize / face.pointSize * face.scale) / (view.messageFontSize * .01f);
            row.body.paragraphSpacing = 0;
            row.time.text = message.time;
            row.time.fontSize = view.timestampFontSize;
            row.time.color = view.muted;
            row.time.fontStyle = FontStyles.Normal;
            row.author.alignment = row.time.alignment = TextAlignmentOptions.Left;
            row.bubble.color = message.mine ? view.selected : view.row;
            return row.LayoutBubble(width, top, message.mine) + 8f;
        }
    }
}

