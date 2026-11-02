using System;
using System.Linq;
using Salasasa.ChatLab;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Salasasa.Chat
{
    public sealed partial class GraphiteChatBridge
    {
        public ScrollRect friendListScroll;
        public RectTransform friendListContent;
        public float friendRowHeight = 56;
        public bool FriendsVisible => SidebarTab == 2;
        FriendInfo[] friendList = Array.Empty<FriendInfo>();
        readonly Texture[] channelPortraits = new Texture[2];
        readonly Rect[] channelPortraitRects = new Rect[2];
        readonly Color[] channelPortraitColors = new Color[2];
        readonly string[] channelPortraitInitials = new string[2];
        RectMask2D friendListMask;
        float nextFriendRefresh;
        bool renderingFriends;

        void InitializeFriendList()
        {
            if (!friendListContent && view.rows.Length > 0) friendListContent = view.rows[0].transform.parent as RectTransform;
            if (!friendListScroll && friendListContent) friendListScroll = friendListContent.GetComponentInParent<ScrollRect>(true);
            if (friendListScroll && friendListScroll.viewport) friendListMask = friendListScroll.viewport.GetComponent<RectMask2D>();
            for (int i = 0; i < view.rows.Length; i++)
            {
                var row = view.rows[i];
                row.button.onClick.AddListener(() => ClickListRow(row));
                row.avatarButton.onClick.AddListener(() => ClickListAvatar(row));
                if (row.deleteButton) row.deleteButton.onClick.AddListener(() => RemoveConversation(row.boundId));
                if (i < 2)
                {
                    channelPortraits[i] = row.portrait.texture;
                    channelPortraitRects[i] = row.portrait.uvRect;
                    channelPortraitColors[i] = row.portrait.color;
                    channelPortraitInitials[i] = row.initials && row.initials.gameObject.activeSelf ? row.initials.text : "";
                }
            }
            if (friendListScroll) friendListScroll.onValueChanged.AddListener(FriendListScrolled);
            FriendRelationCache.OnChanged += FriendListChanged;
            ConfigManager.OnLanguageChanged += FriendLanguageChanged;
        }

        void DisposeFriendList()
        {
            if (friendListScroll) friendListScroll.onValueChanged.RemoveListener(FriendListScrolled);
            FriendRelationCache.OnChanged -= FriendListChanged;
            ConfigManager.OnLanguageChanged -= FriendLanguageChanged;
        }

        public void ShowFriends() { nextFriendRefresh = 0; SetSidebar(2); }
        void FriendLanguageChanged() { if (view) Refresh(); }
        void FriendListChanged() { RefreshList(); RefreshConversationHeader(); }

        void ResetFriendScroll()
        {
            if (!friendListScroll) return;
            friendListScroll.StopMovement(); friendListScroll.verticalNormalizedPosition = 1;
            if (friendListContent) friendListContent.anchoredPosition = new Vector2(friendListContent.anchoredPosition.x, 0);
        }

        void FriendListScrolled(Vector2 value)
        {
            if (renderingFriends) return;
            if (FriendsVisible) RenderFriendRows(); else RenderConversationRows();
        }

        void PollFriends(bool visible)
        {
            if (!visible) { nextFriendRefresh = 0; return; }
            var user = UserDataManager.Instance;
            if (!user || user.UserId <= 0 || user.IsTourist || !FriendNetworkManager.Instance) return;
            if (Time.unscaledTime < nextFriendRefresh) return;
            nextFriendRefresh = Time.unscaledTime + 5f;
            FriendNetworkManager.Instance.ListFriends();
        }

        void RefreshFriends()
        {
            renderingFriends = true;
            friendList = ChatFriendListModel.Filter(FriendRelationCache.Friends, query);
            if (friendListContent) friendListContent.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, friendList.Length * friendRowHeight);
            view.empty.gameObject.SetActive(friendList.Length == 0);
            bool waiting = !FriendRelationCache.IsLoaded && UserDataManager.Instance && !UserDataManager.Instance.IsTourist;
            view.empty.text = PlatformText.Get(waiting ? "加载中…" : string.IsNullOrWhiteSpace(query) ? "暂无好友" : "没有找到匹配的好友");
            renderingFriends = false;
            RenderFriendRows();
        }

        void RenderFriendRows()
        {
            int start = ChatFriendListModel.WindowStart(friendList.Length, view.rows.Length, friendRowHeight,
                friendListContent ? friendListContent.anchoredPosition.y : 0);
            for (int i = 0; i < view.rows.Length; i++)
            {
                var row = view.rows[i]; int index = start + i;
                row.gameObject.SetActive(index < friendList.Length);
                if (index >= friendList.Length) { row.boundId = ""; continue; }
                var friend = friendList[index]; ResetListRow(row, index, friend.user_id.ToString());
                row.title.text = friend.username ?? friend.user_id.ToString();
                row.detail.text = PlatformText.Get(ChatFriendListModel.PresenceText(friend.state));
                int presence = ChatFriendListModel.PresenceOrder(friend.state);
                row.detail.color = presence == 0 ? new Color32(126, 207, 159, 255) : presence == 1 ? new Color32(139, 191, 235, 255) : style.secondary;
                row.mark.gameObject.SetActive(true); row.mark.color = row.detail.color;
                string key = ChatConversationListModel.DirectKey(UserDataManager.Instance.UserId, friend.user_id);
                row.fill.color = pendingOpenKey == key || SelectedConversationKey == key ? style.listSelection : Color.clear;
                SetPortrait(row, friend.profile_image_id, FirstCharacter(friend.username), presence == 2 ? .65f : 1f);
                RefreshFriendTextClipping(row.title); RefreshFriendTextClipping(row.detail);
            }
            if (friendListMask) friendListMask.PerformClipping();
        }

        static void RefreshFriendTextClipping(TMP_Text label)
        {
            label.maskable = true;
            var fontBinding = label.GetComponent<PlatformFontBinding>(); if (fontBinding) fontBinding.Apply();
            if (label.havePropertiesChanged) label.ForceMeshUpdate();
            label.RecalculateClipping();
            foreach (var subMesh in label.GetComponentsInChildren<TMP_SubMeshUI>(true))
            {
                subMesh.maskable = true; subMesh.RecalculateClipping();
                subMesh.canvasRenderer.cull = label.canvasRenderer.cull;
            }
        }

        void PlaceListRow(ChatListRow row, int index)
        {
            var rect = (RectTransform)row.transform;
            rect.anchorMin = new Vector2(0, 1); rect.anchorMax = new Vector2(1, 1); rect.pivot = new Vector2(0, 1);
            rect.anchoredPosition = new Vector2(0, -index * friendRowHeight); rect.sizeDelta = new Vector2(0, friendRowHeight);
        }

        void ResetListRow(ChatListRow row, int index, string key)
        {
            row.boundIndex = index; row.boundId = key; PlaceListRow(row, index);
            row.title.richText = row.detail.richText = false;
            row.title.fontStyle = row.detail.fontStyle = FontStyles.Normal;
            row.title.fontWeight = row.detail.fontWeight = FontWeight.Regular;
            row.detail.maxVisibleLines = 1; row.detail.overflowMode = TextOverflowModes.Ellipsis;
            row.detail.color = style.secondary;
            row.time.gameObject.SetActive(false); row.badgeRoot.SetActive(false); row.mark.gameObject.SetActive(false);
            row.fill.color = Color.clear;
            var titleRect=row.title.rectTransform;titleRect.offsetMax=new Vector2(-8,titleRect.offsetMax.y);
            if (row.initials) row.initials.gameObject.SetActive(false);
            if (row.deleteButton) row.deleteButton.gameObject.SetActive(false);
        }

        void SetPortrait(ChatListRow row, int profileId, string initial, float alpha = 1f)
        {
            var portrait = profileId > 0 ? ConfigManager.GetProfileSprite(profileId) : null;
            row.portrait.texture = portrait ? portrait.texture : null;
            row.portrait.uvRect = new Rect(0, 0, 1, 1);
            if (portrait)
            {
                Rect pixels = portrait.textureRect; Texture texture = portrait.texture;
                row.portrait.uvRect = new Rect(pixels.x / texture.width, pixels.y / texture.height, pixels.width / texture.width, pixels.height / texture.height);
            }
            row.portrait.color = portrait ? new Color(1, 1, 1, alpha) : style.row;
            if (row.initials)
            {
                row.initials.text = initial ?? "";
                row.initials.gameObject.SetActive(!portrait && !string.IsNullOrEmpty(initial));
            }
        }

        void RestoreChannelPortrait(ChatListRow row, int channel)
        {
            row.portrait.texture = channelPortraits[channel]; row.portrait.uvRect = channelPortraitRects[channel];
            row.portrait.color = channelPortraitColors[channel];
            if (row.initials) {
                row.initials.text = string.IsNullOrEmpty(channelPortraitInitials[channel]) ? ""
                    : PlatformText.Get(channel == 0 ? "chat.channel.lobby.initial" : "chat.channel.room.initial");
                row.initials.gameObject.SetActive(!string.IsNullOrEmpty(row.initials.text));
            }
        }

        void ClickListRow(ChatListRow row)
        {
            if (FriendsVisible)
            {
                if (int.TryParse(row.boundId, out int uid)) OpenPrivateConversation(uid);
            }
            else if (SidebarTab == 1) OpenVenueConversation(row.boundId);
            else SelectConversation(row.boundId);
        }

        void ClickListAvatar(ChatListRow row)
        {
            if (FriendsVisible) { ClickListRow(row); return; }
            int uid = 0;
            if (SidebarTab == 0) uid = source.Conversations.FirstOrDefault(item => item.key == row.boundId)?.peer_user_id ?? 0;
            if (uid <= 0) { ClickListRow(row); return; }
            var friend = FriendRelationCache.Friends.FirstOrDefault(item => item.user_id == uid);
            popupMessage = null; ProfileUserId = uid; PopupKind = "profile"; popup.SetActive(true);
            popupTitle.gameObject.SetActive(true); popupDetail.gameObject.SetActive(true); popupClose.gameObject.SetActive(true);
            popupTitle.text = friend?.username ?? row.title.text; popupTitle.richText = false;
            popupTitle.fontStyle = popupDetail.fontStyle = FontStyles.Normal;
            popupTitle.fontWeight = popupDetail.fontWeight = FontWeight.Regular;
            popupDetail.text = "UID: " + uid + (friend != null ? "\n" + PlatformText.Get(ChatFriendListModel.PresenceText(friend.state)) : "");
            profileButton.gameObject.SetActive(true); copyButton.gameObject.SetActive(false);
            popupCard.sizeDelta = new Vector2(320, 150); PlacePopup(row.title.rectTransform);
        }
    }
}
