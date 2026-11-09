using System;
using Salasasa.Home;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>The party panel authored inside the matching page for its team queue.</summary>
[DefaultExecutionOrder(500)]
public sealed class PartyLobbyView : MonoBehaviour {
    public MatchLobbyView matchLobby;
    public GameObject bar, memberMenu;
    public GameObject[] leaderCrowns;
    public Button[] memberButtons;
    public Image[] avatars;
    public TMP_Text[] memberLabels;
    public TMP_Text heading, menuHeading;
    public Button invite, leave, transfer, kick, closeMenu;
    int target;
    float nextRefresh;
    readonly Vector3[] cardCorners = new Vector3[4];
    RectTransform cardAnchor;
    int anchoredRule = -1;
    void Awake() {
        invite.onClick.AddListener(() => PartyNetworkManager.Instance.ShowFriends());
        leave.onClick.AddListener(() => { PartyNetworkManager.Instance.Send("leave"); memberMenu.SetActive(false); });
        transfer.onClick.AddListener(() => { PartyNetworkManager.Instance.MemberAction("transfer", target); memberMenu.SetActive(false); });
        kick.onClick.AddListener(() => { PartyNetworkManager.Instance.MemberAction("kick", target); memberMenu.SetActive(false); });
        closeMenu.onClick.AddListener(() => memberMenu.SetActive(false));
        for (int i = 0; i < memberButtons.Length; i++) { int index = i; memberButtons[i].onClick.AddListener(() => OpenMember(index)); }
    }
    void OnEnable() {
        if (PartyNetworkManager.Instance) PartyNetworkManager.Instance.Changed += Refresh;
        if (matchLobby) matchLobby.RuleChanged += OnRuleChanged;
        Refresh();
    }
    void OnDisable() {
        if (PartyNetworkManager.Instance) PartyNetworkManager.Instance.Changed -= Refresh;
        if (matchLobby) matchLobby.RuleChanged -= OnRuleChanged;
    }
    void OnRuleChanged(int rule) => Refresh();
    void Update() { if (Time.unscaledTime >= nextRefresh) { nextRefresh = Time.unscaledTime + .5f; Refresh(); } }
    void LateUpdate() { if (bar && bar.activeSelf) ApplyCompactLayout(); }
    public void ApplyCompactLayout() {
        if (!bar) return;
        var r = (RectTransform)bar.transform;
        int shown = 0;
        foreach (var member in memberButtons) if (member && member.gameObject.activeSelf) shown++;
        if (invite && invite.gameObject.activeSelf) shown++;
        r.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, Mathf.Max(320, 40 + shown * 86));
        r.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, 148);
        if (heading) Place(heading.rectTransform, 20, 10, r.rect.width - 40, 30);
        for (int i = 0; i < memberButtons.Length; i++) {
            if (!memberButtons[i]) continue;
            Place((RectTransform)memberButtons[i].transform, 20 + i * 86, 48, 64, 64);
            if (i < memberLabels.Length && memberLabels[i]) Place(memberLabels[i].rectTransform, -8, 66, 80, 24);
            if (leaderCrowns != null && i < leaderCrowns.Length && leaderCrowns[i]) Place((RectTransform)leaderCrowns[i].transform, 52, -5, 24, 20);
        }
        if (invite) Place((RectTransform)invite.transform, 20 + Mathf.Max(0, shown - 1) * 86, 48, 64, 64);
        if (!matchLobby || !(r.parent is RectTransform parent)) return;
        if (!cardAnchor || anchoredRule != matchLobby.ActiveRule) {
            anchoredRule = matchLobby.ActiveRule; cardAnchor = matchLobby.ActiveCardAnchor;
        }
        if (!cardAnchor) return;
        cardAnchor.GetWorldCorners(cardCorners);
        Vector2 bottomLeft = parent.InverseTransformPoint(cardCorners[0]);
        r.anchorMin = r.anchorMax = r.pivot = Vector2.up;
        r.anchoredPosition = bottomLeft - new Vector2(parent.rect.xMin, parent.rect.yMax) + new Vector2(0, -48);
    }
    static void Place(RectTransform r, float x, float y, float width, float height) {
        r.anchorMin = r.anchorMax = r.pivot = Vector2.up;
        r.anchoredPosition = new Vector2(x, -y); r.sizeDelta = new Vector2(width, height);
    }
    void OpenMember(int index) {
        var n = PartyNetworkManager.Instance;
        if (!n.HasParty) { n.ShowFriends(); return; }
        if (index >= n.Party.members.Length) return;
        var member = n.Party.members[index]; target = member.user_id;
        bool canManage = n.IsLeader && target != UserDataManager.Instance.UserId && !n.IsQueued && !n.IsCommitted;
        menuHeading.text = member.username;
        transfer.gameObject.SetActive(canManage); kick.gameObject.SetActive(canManage);
        leave.interactable = !n.IsCommitted; memberMenu.SetActive(true);
    }
    void Refresh() {
        var n = PartyNetworkManager.Instance;
        string window = WindowsManager.Instance ? WindowsManager.Instance.GetCurrentWindow() : "login";
        bool teamRule = matchLobby && matchLobby.isActiveAndEnabled && matchLobby.ActiveRule < RankedRules.Ids.Length
            && RankedRules.Ids[matchLobby.ActiveRule] == RankedRules.DoublesRule;
        bool visible = UserDataManager.Instance && UserDataManager.Instance.UserId > 0 && !UserDataManager.Instance.IsTourist
            && window == "match" && teamRule && !LobbyStateGuard.IsInRoom;
        bar.SetActive(visible);
        if (!visible || !n) { memberMenu.SetActive(false); return; }
        var p = n.Party;
        int count = n.MemberCount;
        heading.text = PlatformText.Get("我的队伍") + " · " + count + "/" + (n.HasParty ? p.capacity : 2);
        int slots = n.HasParty ? p.members.Length : 1;
        for (int i = 0; i < memberButtons.Length; i++) {
            memberButtons[i].gameObject.SetActive(i < slots);
            if (i >= slots) continue;
            LobbyPartyMember m = n.HasParty ? p.members[i] : null;
            int profile = m != null ? m.profile_image_id : UserDataManager.Instance.ProfileImageId;
            avatars[i].sprite = ConfigManager.GetProfileSprite(profile);
            avatars[i].color = m == null || m.online ? Color.white : new Color(.5f, .5f, .5f);
            bool leader = m == null || m.user_id == p.leader_user_id;
            if (leaderCrowns != null && i < leaderCrowns.Length && leaderCrowns[i]) leaderCrowns[i].SetActive(leader);
            string label = leader ? "队长" : "队员";
            if (m != null && !m.online) label = "离线";
            memberLabels[i].text = PlatformText.Get(label);
        }
        invite.gameObject.SetActive(!n.HasParty || n.IsLeader && count < p.capacity);
        invite.interactable = !n.IsQueued && !n.IsCommitted;
        ApplyCompactLayout();
        if (!n.HasParty) memberMenu.SetActive(false);
        else if (memberMenu.activeSelf) {
            if (!Array.Exists(p.members, m => m.user_id == target)) memberMenu.SetActive(false);
            bool manage = n.IsLeader && target != UserDataManager.Instance.UserId && !n.IsQueued && !n.IsCommitted;
            transfer.interactable = kick.interactable = manage; leave.interactable = !n.IsCommitted;
        }
    }
}
