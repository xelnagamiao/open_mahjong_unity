using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Salasasa.Venues
{
    public sealed class VenueLobbyPresentation : MonoBehaviour
    {
        public EventLobbyPanel owner;
        public VenuePageView[] pages;
        public VenueCreateView[] forms;
        public GameObject modalLayer, confirmation;
        public TMP_Text confirmationTitle, confirmationBody, confirmationStatus;
        public Button confirmationBack, confirmationClose, submit;
        public VenuePageView ActivePage { get; private set; }
        public IReadOnlyList<VenueEntry> VisibleRows => visible;
        public bool Submitting => submitting;
        public VenueCreateView OpenForm => openForm;

        readonly string[] searches = { "", "" };
        readonly int[] filters = { 0, 0 }, offsets = { 0, 0 }, sorts = { 0, 0 };
        readonly VenueApplication[] drafts = { new VenueApplication { kind = "base" }, new VenueApplication { kind = "event" } };
        readonly VenueApplicationClient applicationClient = new VenueApplicationClient();
        List<VenueEntry> source = new List<VenueEntry>(), visible = new List<VenueEntry>();
        VenueKind kind;
        VenueCreateView openForm;
        bool wired, submitting, loading;
        string listError = "", applicationError = "";
        int requestEpoch;

        void Awake() { Initialize(); }
        public void Initialize()
        {
            if (wired) return;
            wired = true;
            foreach (var page in pages)
            {
                var p = page;
                p.search.onValueChanged.AddListener(value => { if (ActivePage == p) SetSearch(value); });
                if (p.sort) p.sort.onValueChanged.AddListener(value => { if (ActivePage == p) SetSort(value); });
                p.clearSearch.onClick.AddListener(() => SetSearch(""));
                p.previous.onClick.AddListener(() => MovePage(-1));
                p.next.onClick.AddListener(() => MovePage(1));
                p.create.onClick.AddListener(OpenApplication);
                for (int i = 0; i < p.filters.Length; i++) { int index = i; p.filters[i].onClick.AddListener(() => SetFilter(index)); }
                foreach (var card in p.cards)
                {
                    var item = card;
                    item.openButton.onClick.AddListener(() => { if (!string.IsNullOrEmpty(item.entryId)) owner.OpenDetail(item.entryId); });
                }
            }
            foreach (var form in forms)
            {
                form.close.onClick.AddListener(CloseApplication);
                form.cancel.onClick.AddListener(CloseApplication);
                form.preview.onClick.AddListener(PreviewApplication);
            }
            confirmationBack.onClick.AddListener(CloseConfirmation);
            confirmationClose.onClick.AddListener(CloseConfirmation);
            submit.onClick.AddListener(SubmitApplication);
            modalLayer.SetActive(false);
        }
        void OnDisable()
        {
            if (submitting) applicationError = "请求未确认，请到个人中心核对申请结果，避免重复提交";
            requestEpoch++;
            StopAllCoroutines();
            applicationClient.Dispose();
            submitting = false;
            CloseApplication();
        }
        public void RefreshFromHost(IEnumerable<EventListEntry> rows, string venueKind)
        {
            Initialize();
            var next = venueKind == "base" ? VenueKind.Base : VenueKind.Event;
            if (ActivePage && next != kind) CloseApplication();
            kind = next;
            source = (rows ?? Array.Empty<EventListEntry>()).Where(r => r != null).Select(Adapt).ToList();
            loading = false;
            listError = "";
            foreach (var page in pages) page.gameObject.SetActive(page.kind == kind);
            ActivePage = Array.Find(pages, p => p.kind == kind);
            Render();
        }
        public static VenueEntry Adapt(EventListEntry value)
        {
            var summary = value.entry_summary ?? new EventEntrySummary();
            return new VenueEntry {
                event_id = value.event_id, name = value.name, description = value.description, status = value.status, kind = value.kind,
                created_at = value.created_at, planned_start_at = value.planned_start_at, planned_end_at = value.planned_end_at,
                member_count = value.member_count ?? -1, is_registered = value.is_registered || !string.IsNullOrEmpty(value.role),
                auto_approve = summary.auto_approve, has_join_code = summary.has_join_code, forbid_tourist = summary.forbid_tourist,
                min_rank = summary.min_rank, max_rank = summary.max_rank, create_room_permission = summary.ResolvedCreateRoomPermission()
            };
        }
        public void BeginLoading() { loading = true; listError = ""; Render(); }
        public void SetListError(string value) { loading = false; listError = value ?? "加载失败，请重试"; Render(); }
        public void SetSearch(string value) { searches[(int)kind] = value ?? ""; offsets[(int)kind] = 0; Render(); }
        public void SetFilter(int value) { filters[(int)kind] = value; offsets[(int)kind] = 0; Render(); }
        public void SetSort(int value) { sorts[(int)kind] = Mathf.Clamp(value, 0, 2); offsets[(int)kind] = 0; Render(); }
        public void MovePage(int direction)
        {
            if (!ActivePage) return;
            offsets[(int)kind] = Mathf.Clamp(offsets[(int)kind] + direction * ActivePage.cards.Length, 0,
                Math.Max(0, (visible.Count - 1) / ActivePage.cards.Length * ActivePage.cards.Length));
            Render();
        }
        void Render()
        {
            if (!ActivePage) return;
            int index = (int)kind, capacity = ActivePage.cards.Length;
            var now = DateTime.Now;
            visible = VenueQuery.Select(source, kind, searches[index], filters[index], now, (VenueSort)sorts[index]);
            int last = Math.Max(0, (visible.Count - 1) / capacity * capacity);
            offsets[index] = Mathf.Clamp(offsets[index], 0, last);
            int offset = offsets[index];
            ActivePage.search.SetTextWithoutNotify(searches[index]);
            ActivePage.ShowFilter(filters[index]);
            ActivePage.ShowSort(sorts[index]);
            ActivePage.resultCount.text = string.IsNullOrEmpty(listError)
                ? (kind == VenueKind.Base ? "基地 " : "赛事 ") + visible.Count : listError;
            ActivePage.pagination.text = (offset / capacity + 1).ToString("00") + " / " + Math.Max(1, (visible.Count + capacity - 1) / capacity).ToString("00");
            ActivePage.previous.interactable = offset > 0;
            ActivePage.next.interactable = offset + capacity < visible.Count;
            ActivePage.clearSearch.gameObject.SetActive(searches[index].Length > 0);
            for (int i = 0; i < capacity; i++)
            {
                var card = ActivePage.cards[i];
                bool show = offset + i < visible.Count;
                card.gameObject.SetActive(show);
                if (show) card.Bind(visible[offset + i], now, false);
                else card.entryId = null;
            }
            ActivePage.emptyState.SetActive(visible.Count == 0);
            ActivePage.emptyTitle.text = !string.IsNullOrEmpty(listError) ? listError : loading ? "正在加载…"
                : searches[index].Length > 0 || filters[index] > 0 ? "没有符合条件的结果" : kind == VenueKind.Base ? "暂无基地" : "暂无赛事";
        }
        public void OpenApplication()
        {
            Initialize();
            if (submitting) return;
            foreach (var form in forms) form.gameObject.SetActive(form.kind == kind);
            openForm = Array.Find(forms, f => f.kind == kind);
            openForm.Write(drafts[(int)kind]);
            openForm.validation.text = applicationError;
            confirmation.SetActive(false);
            modalLayer.SetActive(true);
        }
        public void CloseApplication()
        {
            if (submitting) return;
            if (openForm) drafts[(int)openForm.kind] = openForm.Read();
            foreach (var form in forms) form.gameObject.SetActive(false);
            confirmation.SetActive(false);
            modalLayer.SetActive(false);
            openForm = null;
        }
        public void PreviewApplication()
        {
            if (!openForm || submitting) return;
            var value = openForm.Read();
            var error = value.Validate();
            openForm.validation.text = error ?? "";
            if (error != null) return;
            drafts[(int)kind] = value;
            confirmationTitle.text = kind == VenueKind.Base ? "基地申请预览" : "办赛申请预览";
            confirmationBody.text = "名称\n" + value.name + "\n\n拟定日期\n" + (string.IsNullOrEmpty(value.planned_start_at) ? "未设置" : value.planned_start_at)
                + (string.IsNullOrEmpty(value.planned_end_at) ? "" : "  —  " + value.planned_end_at)
                + "\n\n介绍\n" + value.description + "\n\n负责人\n" + value.organizer_name + "   " + value.organizer_phone
                + "\n\n备注\n" + (string.IsNullOrEmpty(value.remark) ? "无" : value.remark);
            confirmationStatus.text = applicationError;
            confirmation.SetActive(true);
            SetSubmitting(false);
            var scroll = confirmation.GetComponentInChildren<ScrollRect>();
            if (scroll) scroll.verticalNormalizedPosition = 1;
        }
        void CloseConfirmation() { if (!submitting) confirmation.SetActive(false); }
        void SetSubmitting(bool value)
        {
            submitting = value;
            submit.interactable = confirmationBack.interactable = confirmationClose.interactable = !value;
            foreach (var form in forms) form.close.interactable = form.cancel.interactable = form.preview.interactable = !value;
        }
        public void SubmitApplication()
        {
            if (!confirmation.activeSelf || submitting) return;
            var value = drafts[(int)kind];
            var error = value.Validate();
            if (error != null) { confirmationStatus.text = error; return; }
            StartCoroutine(Submit(value, ++requestEpoch));
        }
        IEnumerator Submit(VenueApplication value, int epoch)
        {
            SetSubmitting(true);
            confirmationStatus.text = "正在提交…";
            VenueApplicationResult result = null;
            yield return applicationClient.Submit(value, r => result = r);
            if (epoch != requestEpoch) yield break;
            SetSubmitting(false);
            if (result == null || !result.success)
            {
                applicationError = result?.message ?? "请求未确认，请到个人中心核对申请结果";
                confirmationStatus.text = applicationError;
                yield break;
            }
            applicationError = "";
            drafts[(int)kind] = new VenueApplication { kind = value.kind };
            openForm.Write(drafts[(int)kind]);
            openForm.validation.text = "申请已提交，等待审核";
            confirmation.SetActive(false);
        }
    }
}
