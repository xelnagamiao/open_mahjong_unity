using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

[Serializable] public sealed class MailPalette
{
    public string name;
    public Color surface, header, list, card, selected, ink, muted, accent, line, actionInk;
}

public sealed class MailPanel : MonoBehaviour
{
    public const int PaletteCount = 1;
    public Salasasa.Home.CompleteLobbyController owner;
    public RectTransform frame, listContent, rewardContent;
    public CanvasGroup contentFade;
    public GameObject detail, rewardArea;
    public MailListItem rowTemplate;
    public MailRewardItem rewardTemplate;
    public TMP_Text heading, subject, body, sender, date, emptyList, emptyDetail, status, claimLabel;
    public Button close, refresh, claim, backdrop;
    public ScrollRect bodyScroll;
    public Image surface, header, listSurface, bodySurface, footer, actionSurface;
    public Image[] dividers;
    public Image[] secondarySurfaces;
    public TMP_Text[] secondaryLabels;
    public MailPalette[] palettes;
    public int defaultPalette;
    public int PaletteIndex { get; private set; }
    public bool IsOpen => gameObject.activeSelf;
    readonly List<MailListItem> rows = new List<MailListItem>();
    readonly List<MailRewardItem> rewards = new List<MailRewardItem>();
    MailEntry[] entries = Array.Empty<MailEntry>();
    MailEntry selected;
    NetworkManager network;
    Coroutine timeout, fade;
    string pending;
    int openedUser;
    bool busy;
    public MailPalette Palette => palettes[Mathf.Clamp(PaletteIndex, 0, palettes.Length - 1)];
    void LateUpdate() => RefreshLayout();

    public void RefreshLayout()
    {
        if (!owner) return;
        var root=(RectTransform)transform;
        var inset=owner.headerNavigation.Insets * CanvasReferenceLayout.EffectiveScale(owner.artwork.canvas)
            / CanvasReferenceLayout.PixelsPerUnit(root);
        var shade=(RectTransform)backdrop.transform;
        shade.anchorMin=Vector2.zero; shade.anchorMax=Vector2.one;
        shade.offsetMin=new Vector2(inset.x,inset.w);
        shade.offsetMax=new Vector2(-inset.z,-inset.y);
        var available=root.rect.size-new Vector2(inset.x+inset.z,inset.y+inset.w);
        float scale=Mathf.Min(1,(available.x-48)/1180,(available.y-48)/664);
        frame.localScale=Vector3.one*Mathf.Max(.1f,scale);
        frame.anchoredPosition=new Vector2((inset.x-inset.z)*.5f,(inset.w-inset.y)*.5f);
    }

    void Awake()
    {
        rowTemplate.gameObject.SetActive(false); rewardTemplate.gameObject.SetActive(false);
        close.onClick.AddListener(Close); backdrop.onClick.AddListener(Close);
        refresh.onClick.AddListener(RefreshMail); claim.onClick.AddListener(Claim);
        PaletteIndex = Mathf.Clamp(PlayerPrefs.GetInt("MailPalette", defaultPalette), 0, palettes.Length - 1);
        ApplyPalette();
    }
    void OnEnable()
    {
        RefreshLayout();
        network = NetworkManager.Instance;
        if (network) network.MailResponseReceived += Receive;
        ConfigManager.OnLanguageChanged += RefreshLanguage;
    }
    void OnDisable()
    {
        if (network) network.MailResponseReceived -= Receive;
        ConfigManager.OnLanguageChanged -= RefreshLanguage;
        StopAllCoroutines(); timeout = fade = null; pending = null; busy = false;
    }
    public void Open()
    {
        gameObject.SetActive(true);
        int user = UserDataManager.Instance ? UserDataManager.Instance.UserId : 0;
        if (openedUser != user) { entries = Array.Empty<MailEntry>(); selected = null; openedUser = user; }
        Render(); RefreshMail();
        if (fade != null) StopCoroutine(fade);
        fade = StartCoroutine(FadeIn());
    }
    IEnumerator FadeIn()
    {
        contentFade.alpha = 0;
        for (float t = 0; t < .16f; t += Time.unscaledDeltaTime)
        { contentFade.alpha = Mathf.SmoothStep(0, 1, t / .16f); yield return null; }
        contentFade.alpha = 1; fade = null;
    }
    public void Close() => gameObject.SetActive(false);
    public void SelectPalette(int index)
    {
        PaletteIndex = Mathf.Clamp(index, 0, palettes.Length - 1);
        PlayerPrefs.SetInt("MailPalette", PaletteIndex); PlayerPrefs.Save(); ApplyPalette();
    }
    public void ApplyPalette()
    {
        var p = Palette;
        surface.color = p.surface; header.color = p.header; listSurface.color = p.list;
        bodySurface.color = p.card; footer.color = p.surface; actionSurface.color = p.accent;
        heading.color = subject.color = body.color = p.ink;
        sender.color = date.color = emptyList.color = emptyDetail.color = status.color = p.muted;
        claimLabel.color = p.actionInk;
        foreach (var line in dividers) line.color = p.line;
        foreach (var image in secondarySurfaces) image.color = p.selected;
        foreach (var text in secondaryLabels) text.color = p.ink;
        foreach (var row in rows) row.Apply(p, selected != null && row.Entry.mail_id == selected.mail_id);
        foreach (var reward in rewards) reward.Apply(p);
    }
    void RefreshLanguage() { RenderDetail(); }
    public void RefreshMail()
    {
        if (busy) return;
        Send("mail/list", 0);
    }
    void Send(string type, long id)
    {
        if (!network) { status.text = PlatformText.Get("网络未连接"); return; }
        busy = true; pending = Guid.NewGuid().ToString("N");
        refresh.interactable = false; claim.interactable = false;
        status.text = PlatformText.Get("加载中…");
        if (timeout != null) StopCoroutine(timeout);
        timeout = StartCoroutine(Timeout(pending));
        network.RequestMail(new MailCommand { type = type, request_id = pending, mail_id = id });
    }
    IEnumerator Timeout(string request)
    {
        yield return new WaitForSecondsRealtime(12);
        if (pending == request) { busy = false; pending = null; refresh.interactable = true; RenderDetail(); status.text = PlatformText.Get("连接超时，请重试"); }
        timeout = null;
    }
    void Receive(Response response)
    {
        if (response.type == "mail/changed") { if (!busy) RefreshMail(); return; }
        if (response.request_id != pending) return;
        busy = false; pending = null; refresh.interactable = true;
        if (timeout != null) StopCoroutine(timeout); timeout = null;
        if (!response.success) { RenderDetail(); status.text = PlatformText.Get(response.message ?? "加载失败，请重试"); return; }
        if (response.mails != null)
        {
            long id = selected?.mail_id ?? 0; entries = response.mails;
            selected = entries.FirstOrDefault(m => m.mail_id == id) ?? entries.FirstOrDefault();
            Render();
            if (selected != null && !selected.is_read) Send("mail/read", selected.mail_id);
        }
        else if (response.mail != null)
        {
            int index = Array.FindIndex(entries, m => m.mail_id == response.mail.mail_id);
            if (index >= 0) entries[index] = response.mail;
            if (selected?.mail_id == response.mail.mail_id) selected = response.mail;
            Render();
        }
        else RenderDetail();
    }
    void Select(MailEntry entry)
    {
        if (busy) return;
        selected = entry; bodyScroll.verticalNormalizedPosition = 1; RenderDetail(); ApplyPalette();
        if (!entry.is_read) Send("mail/read", entry.mail_id);
    }
    void Claim()
    {
        if (busy || selected == null || selected.claimed || selected.expired || (selected.rewards?.Length ?? 0) == 0) return;
        Send("mail/claim", selected.mail_id);
    }
    void Render()
    {
        foreach (var row in rows) Destroy(row.gameObject); rows.Clear();
        foreach (var entry in entries)
        {
            var row = Instantiate(rowTemplate, listContent); row.gameObject.SetActive(true);
            row.Bind(entry, () => Select(entry)); rows.Add(row);
        }
        emptyList.gameObject.SetActive(entries.Length == 0);
        RenderDetail(); ApplyPalette();
    }
    void RenderDetail()
    {
        detail.SetActive(selected != null); emptyDetail.gameObject.SetActive(selected == null);
        foreach (var reward in rewards) Destroy(reward.gameObject); rewards.Clear();
        status.text = "";
        if (selected == null) return;
        subject.text = selected.title; body.text = selected.content; sender.text = selected.sender;
        date.text = Date(selected.created_at);
        foreach (var attachment in selected.rewards ?? Array.Empty<MailReward>())
        {
            var item = Instantiate(rewardTemplate, rewardContent); item.gameObject.SetActive(true);
            item.Bind(attachment); item.Apply(Palette); rewards.Add(item);
        }
        bool attached = rewards.Count > 0;
        rewardArea.SetActive(attached); claim.gameObject.SetActive(attached);
        var bodyRect = (RectTransform)bodyScroll.transform;
        bodyRect.offsetMin = new Vector2(bodyRect.offsetMin.x, attached ? 168 : 30);
        claim.interactable = attached && !busy && !selected.claimed && !selected.expired;
        claimLabel.text = PlatformText.Get(selected.claimed ? "已领取" : selected.expired ? "已过期" : "领取");
    }
    public static string Date(string value) => DateTimeOffset.TryParse(value, out var time)
        ? time.ToLocalTime().ToString("yyyy.MM.dd  HH:mm") : value ?? "";
}
