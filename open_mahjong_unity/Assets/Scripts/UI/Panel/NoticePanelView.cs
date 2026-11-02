using System.Collections.Generic;
using TMPro;
using UnityEngine;

public sealed class NoticePanelView : MonoBehaviour
{
    public NoticePanel owner;
    public ActivityItem[] entries;
    public UnityEngine.UI.Button[] filters;
    public TMP_Text[] filterLabels;
    public UnityEngine.UI.Button previousPage, nextPage, previousDocument, nextDocument, close, retry;
    public TMP_Text countLabel, pageLabel, stateTitle, stateBody;
    public GameObject listRoot, articleRoot, stateRoot;
    public UnityEngine.UI.ScrollRect listScroll;
    public Color accent, ink, muted;
    [SerializeField] private bool compactLayout;
    public int PageSize => entries.Length;
    public void Filter(int value) => owner.SetFilter(value);
    public void PreviousPage() => owner.ChangePage(-1);
    public void NextPage() => owner.ChangePage(1);
    public void PreviousDocument() => owner.ChangeDocument(-1);
    public void NextDocument() => owner.ChangeDocument(1);
    public void Reload() => owner.Reload();
    public void Close() => owner.Close();

    public void Render(IReadOnlyList<ActivityIndexItem> items, string selected, int page, int filter)
    {
        stateRoot.SetActive(items.Count == 0); listRoot.SetActive(items.Count > 0); articleRoot.SetActive(items.Count > 0);
        if (items.Count == 0) {
            stateTitle.text = filter == 1 ? "没有未读通知" : "暂无通知";
            ClearSupplementaryLabels();
            retry.gameObject.SetActive(false);
        }
        for (int i = 0; i < entries.Length; i++) {
            int index = page * PageSize + i;
            entries[i].gameObject.SetActive(index < items.Count);
            if (index >= items.Count) continue;
            entries[i].Bind(items[index], owner.OpenDetail); entries[i].SetSelected(items[index].id == selected);
        }
        for (int i = 0; i < filters.Length; i++) {
            filters[i].interactable = true; filters[i].targetGraphic.color = i == filter ? accent : Color.clear;
            filterLabels[i].color = i == filter ? ink : muted;
        }
        int pages = Mathf.Max(1, Mathf.CeilToInt(items.Count / (float)PageSize));
        ClearSupplementaryLabels();
        pageLabel.text = (page + 1).ToString("00") + " / " + pages.ToString("00");
        SetPaginationVisible(items.Count > 0 && pages > 1);
        previousPage.interactable = page > 0; nextPage.interactable = page + 1 < pages;
        previousDocument.interactable = nextDocument.interactable = items.Count > 1;
    }
    public void Select(string id) { foreach (var entry in entries) entry.SetSelected(entry.ActivityId == id); }
    public void ShowState(bool failed)
    {
        listRoot.SetActive(false); articleRoot.SetActive(false); stateRoot.SetActive(true);
        stateTitle.text = failed ? "通知暂时无法加载" : "正在读取通知";
        ClearSupplementaryLabels();
        retry.gameObject.SetActive(failed); pageLabel.text = "";
        SetPaginationVisible(false);
        foreach (var filter in filters) filter.interactable = false;
        previousPage.interactable = nextPage.interactable = previousDocument.interactable = nextDocument.interactable = false;
    }

    private void ClearSupplementaryLabels()
    {
        if (countLabel != null) { countLabel.text = ""; countLabel.gameObject.SetActive(false); }
        if (stateBody != null) { stateBody.text = ""; stateBody.gameObject.SetActive(false); }
    }

    private void SetPaginationVisible(bool visible)
    {
        if (!compactLayout) return;
        previousPage.gameObject.SetActive(visible); nextPage.gameObject.SetActive(visible); pageLabel.gameObject.SetActive(visible);
        var rect = (RectTransform)listScroll.transform;
        rect.offsetMin = new Vector2(rect.offsetMin.x, visible ? 64 : 24);
    }
}
