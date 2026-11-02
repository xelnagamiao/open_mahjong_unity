using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public sealed class MailListItem : MonoBehaviour
{
    public Button button;
    public Image surface, stripe, unread;
    public TMP_Text title, date;
    public MailEntry Entry { get; private set; }
    public void Bind(MailEntry entry, Action select)
    {
        Entry = entry; title.text = entry.title; date.text = MailPanel.Date(entry.created_at);
        unread.gameObject.SetActive(!entry.is_read);
        button.onClick.RemoveAllListeners(); button.onClick.AddListener(() => select());
    }
    public void Apply(MailPalette p, bool selected)
    {
        surface.color = selected ? p.selected : p.card;
        stripe.color = unread.color = p.accent; stripe.gameObject.SetActive(selected);
        title.color = p.ink; date.color = p.muted;
    }
}
