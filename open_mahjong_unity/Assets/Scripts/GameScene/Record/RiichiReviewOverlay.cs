using System;
using System.Collections;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;
using TMPro;

/// <summary>A child of the existing record HUD. Its graphics never participate in hand or table layout.</summary>
internal sealed partial class RiichiReviewOverlay : MonoBehaviour {
    private const string ModeKey = "RiichiReview.DisplayMode";
    private const string AllKey = "RiichiReview.ShowAllPercentages";
    private enum DisplayMode { B, C }
    private DisplayMode mode;
    private bool showAll;
    private GameRecordManager owner;
    private RectTransform root, handle, panel, markerLayer;
    private string recordId, status;
    private GameRecord record;
    private RiichiReviewReport report;
    private UnityWebRequest request;
    private bool needsLoad;
    private int round = -1, node = -1, seat = -1;
    private List<RiichiReviewReport.Decision> atNode;
    private int phaseIndex;
    private RiichiReviewReport.Decision decision;
    private RiichiReviewReport.Candidate selected;
    private bool drawerOpen;

    private static readonly Color Paper = new Color32(237, 244, 246, 255);
    private static readonly Color Muted = new Color32(175, 195, 203, 255);
    private static readonly Color Ink = new Color32(20, 45, 43, 255);
    private static readonly Color Mint = new Color32(172, 242, 207, 255);
    private static readonly Color Gold = new Color32(255, 217, 157, 255);
    private static readonly Color Slate = new Color32(35, 53, 64, 250);
    private static readonly Color Edge = new Color32(94, 118, 126, 255);
    private static readonly Color Deep = new Color32(19, 34, 43, 249);

    internal static RiichiReviewOverlay Create(GameRecordManager manager) {
        var rect = RectChild(manager.transform, "RiichiReview");
        Stretch(rect);
        var view = rect.gameObject.AddComponent<RiichiReviewOverlay>();
        view.root = rect;
        view.owner = manager;
        var canvas = manager.GetComponentInParent<Canvas>().rootCanvas;
        view.eventCamera = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
        view.mode = PlayerPrefs.GetInt(ModeKey) == 1 ? DisplayMode.C : DisplayMode.B;
        view.showAll = PlayerPrefs.GetInt(AllKey) == 1;
        view.markerLayer = RectChild(rect, "HandProbabilities");
        Stretch(view.markerLayer);
        view.BuildHandle();
        return view;
    }

    internal void OpenRecord(string id) {
        gameObject.SetActive(true);
        if (record == owner.gameRecord && id == recordId && (report != null || request != null)) return;
        CancelLoad();
        record = owner.gameRecord;
        recordId = id;
        report = null;
        needsLoad = !string.IsNullOrEmpty(id);
        status = needsLoad ? "正在读取复盘…" : "这份牌谱暂无分析";
        round = -1;
        atNode = null;
        SetDecision(null);
        SetDrawer(false);
    }

    internal void ReleaseRecord() {
        CancelLoad();
        record = null;
        report = null;
        recordId = null;
        status = null;
        needsLoad = false;
        round = -1;
        node = -1;
        seat = -1;
        atNode = null;
        phaseIndex = 0;
        drawerOpen = false;
        SetDecision(null);
        if (markerLayer) markerLayer.gameObject.SetActive(false);
        if (panel) panel.gameObject.SetActive(false);
        if (gameObject.activeSelf) gameObject.SetActive(false);
    }

    private void OnEnable() =>
        GameSceneMouseInputController.Instance?.RegisterAdditionalUiRoot((RectTransform)transform);

    private void OnDisable() {
        CancelLoad();
        GameSceneMouseInputController.Instance?.UnregisterAdditionalUiRoot((RectTransform)transform);
        if (markerLayer) markerLayer.gameObject.SetActive(false);
        pressStartedOnHand = false;
    }

    private void CancelLoad() {
        if (request == null) return;
        StopAllCoroutines();
        request.Abort();
        request.Dispose();
        request = null;
        needsLoad = true;
    }

    private IEnumerator LoadReport() {
        needsLoad = false;
        request = UnityWebRequest.Get(SharedRecordLink.RecordApiUrl(recordId) + "/riichi-review");
        request.timeout = 20;
        request.SetRequestHeader("Accept", "application/json");
        yield return request.SendWebRequest();
        try {
            if (request.result != UnityWebRequest.Result.Success) {
                status = "读取分析失败";
            } else {
                var envelope = JObject.Parse(request.downloadHandler.text);
                if ((bool?)envelope["success"] != true) throw new FormatException();
                if (envelope["data"] is JObject data)
                    report = RiichiReviewReport.Read(data, recordId, record);
                status = report == null ? "这份牌谱暂无分析" : "";
            }
        } catch (Exception e) {
            status = "分析数据与这份牌谱不匹配";
            Debug.LogWarning("Riichi review: " + e.Message);
        } finally {
            request.Dispose();
            request = null;
        }
        round = -1;
    }

    private void LateUpdate() {
        if (!owner || owner.IsSpectating || owner.IsSpectatorSession || record != owner.gameRecord) {
            gameObject.SetActive(false);
            return;
        }
        if (needsLoad && request == null) StartCoroutine(LoadReport());
        if (round != owner.currentRoundIndex || node != owner.currentNode || seat != owner.selectedPlayerOriginalIndex) {
            round = owner.currentRoundIndex;
            node = owner.currentNode;
            seat = owner.selectedPlayerOriginalIndex;
            atNode = report?.Perspective == seat ? report.At(round, node) : null;
            phaseIndex = 0;
            SetDecision(atNode != null ? atNode[0] : null);
        }
        bool available = !owner.BlocksRecordNavigation && !(EndGamePanel.Instance && EndGamePanel.Instance.IsRecordResultVisible);
        handle.gameObject.SetActive(available && !drawerOpen);
        if (panel) panel.gameObject.SetActive(available && drawerOpen);
        bool mark = available && decision != null && decision.Discards.Count > 0;
        markerLayer.gameObject.SetActive(mark);
        if (mark) UpdateHandMarkers();
        if (available && drawerOpen) ResizeDrawer();
    }

    private void SetDecision(RiichiReviewReport.Decision next) {
        decision = next;
        selected = next == null ? null : Array.Find(next.Choices, c => c.Key == next.Recommended);
        pressStartedOnHand = false;
        if (drawerOpen) RefreshDrawer(true);
    }

    private void SeekDecision(int direction) {
        if (owner.BlocksRecordNavigation || report == null) return;
        var target = direction > 0
            ? report.Decisions.Find(d => d.Round > round || (d.Round == round && d.Node > node))
            : report.Decisions.FindLast(d => d.Round < round || (d.Round == round && d.Node < node));
        if (target == null) return;
        if (target.Round != owner.currentRoundIndex) owner.GotoSelectRound(target.Round);
        owner.GotoSelectNode(target.Node);
    }

    private void SetMode(DisplayMode value) {
        mode = value;
        pressStartedOnHand = false;
        PlayerPrefs.SetInt(ModeKey, (int)value);
        PlayerPrefs.Save();
        RefreshDrawer(false);
    }

    private void SetDrawer(bool open) {
        drawerOpen = open;
        if (open && !panel) BuildDrawer();
        if (panel) panel.gameObject.SetActive(open);
        handle.gameObject.SetActive(!open);
        if (open) { ResizeDrawer(); RefreshDrawer(true); }
    }

    private static RectTransform RectChild(Transform parent, string name) {
        var go = new GameObject(name, typeof(RectTransform));
        go.layer = parent.gameObject.layer;
        var rect = (RectTransform)go.transform;
        rect.SetParent(parent, false);
        return rect;
    }

    private static void Stretch(RectTransform rect, float inset = 0) {
        rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.one * inset; rect.offsetMax = Vector2.one * -inset;
    }

    private static TableHudSurface Surface(RectTransform rect, Color fill, Color border, bool hit, float radius = 6f) {
        var s = rect.gameObject.AddComponent<TableHudSurface>();
        s.color = s.bottomColor = fill; s.edgeColor = border;
        s.corner = radius; s.chamfer = false; s.edgeWidth = 1;
        s.raycastTarget = hit;
        return s;
    }

    private static TextMeshProUGUI Label(RectTransform parent, string name, string value, float size,
        Color color, TextAlignmentOptions alignment = TextAlignmentOptions.MidlineLeft) {
        var rect = RectChild(parent, name);
        Stretch(rect);
        var text = rect.gameObject.AddComponent<TextMeshProUGUI>();
        text.richText = false;
        text.raycastTarget = false;
        text.text = value;
        text.fontSize = size;
        text.color = color;
        text.alignment = alignment;
        text.textWrappingMode = TextWrappingModes.NoWrap;
        text.gameObject.AddComponent<PlatformFontBinding>();
        TableHudTypography.Apply(text);
        return text;
    }
}
