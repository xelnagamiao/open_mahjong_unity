using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public partial class CreatePanel {
    [SerializeField, HideInInspector] private bool deferredPanelsBaked;
    private const string DeferredResourceRoot = "CreateRoomPanels/";
    private readonly Dictionary<string, GameObject> deferredViews = new Dictionary<string, GameObject>();
    private readonly Dictionary<string, List<Action<GameObject>>> deferredRequests = new Dictionary<string, List<Action<GameObject>>>();
    private readonly Queue<string> deferredQueue = new Queue<string>();
    private static bool IsInlinePanel(string key) => key == "HongKongSettings" || key == "FuzhouSettings";
    private static string DeferredRule(string key) => key == "HongKongSettings" ? "hongkong" : key == "FuzhouSettings" ? FuzhouGameState.RuleId : key;
    private bool deferredLoading;
    private int deferredOpenVersion;

    private void PrewarmSelectedDetails() {
        if (!Application.isPlaying || !isActiveAndEnabled || !initialized) return;
        if (Array.IndexOf(DetailedRules, _ruleState) < 0) return;
        if (_ruleState == "hongque" && GetSelectedSubRule() != HongqueWScoring.SubRule) return;
        RequestDeferredPanel(_ruleState, null);
    }

    private void RequestDeferredPanel(string key, Action<GameObject> ready) {
        if (deferredViews.TryGetValue(key, out var cached) && cached) { ready?.Invoke(cached); return; }
        if (deferredRequests.TryGetValue(key, out var waiting)) { if (ready != null) waiting.Add(ready); return; }
        waiting = new List<Action<GameObject>>();
        if (ready != null) waiting.Add(ready);
        deferredRequests.Add(key, waiting);
        deferredQueue.Enqueue(key);
        if (!deferredLoading) {
            deferredLoading = true;
            CoroutineManager.Instance.Run(LoadDeferredPanels());
        }
    }

    // The shared runner survives closing the page. A completed load only opens a dialog
    // when its original request is still current; otherwise its inactive view is cached.
    private IEnumerator LoadDeferredPanels() {
        while (this && deferredQueue.Count > 0) {
            yield return null;
            if (!this) yield break;
            var key = deferredQueue.Dequeue();
            if (deferredRequests[key].Count == 0 && (!isActiveAndEnabled || _ruleState != DeferredRule(key))) { deferredRequests.Remove(key); continue; }
            var load = Resources.LoadAsync<GameObject>(DeferredResourceRoot + key);
            yield return load;
            if (!this) yield break;
            GameObject instance = null;
            if (load.asset is GameObject prefab) {
                var operation = InstantiateAsync(prefab, IsInlinePanel(key) ? RoomContent : transform);
                yield return operation;
                if (!this) {
                    foreach (var orphan in operation.Result) if (orphan) Destroy(orphan);
                    yield break;
                }
                instance = operation.Result[0];
                instance.name = IsInlinePanel(key) ? key : key == "RuleSupportTable" ? "RuleSupportTablePanel"
                    : key == "TaiwanFanTable" ? "FanTablePanel" : "DetailedConfigPanel_" + key;
                instance.SetActive(false);
                // InstantiateAsync(parent) preserves world space. Restore the authored
                // UI anchors before activating the view under either creation context.
                var rect = (RectTransform)instance.transform;
                var authored = (RectTransform)prefab.transform;
                rect.localScale = authored.localScale;
                rect.localRotation = authored.localRotation;
                rect.anchorMin = authored.anchorMin;
                rect.anchorMax = authored.anchorMax;
                rect.pivot = authored.pivot;
                rect.sizeDelta = authored.sizeDelta;
                rect.anchoredPosition3D = authored.anchoredPosition3D;
                if (IsInlinePanel(key)) instance.transform.SetSiblingIndex(RoomContent.Find("ToggleContainer").GetSiblingIndex() + 1);
                try {
                    foreach (var help in instance.GetComponentsInChildren<CreateRoomHelpTrigger>(true)) help.BindOwner(this);
                    if (key == "HongKongSettings") BindHongKongMainControls(instance);
                    else if (key == "FuzhouSettings") BindFuzhouMainControls(instance);
                    else if (key == "RuleSupportTable") BindRuleSupportView(instance);
                    else if (key == "TaiwanFanTable") BindDetailedConfigFanView(GetDetailedConfigState("taiwan"), instance);
                    else BindDetailedConfigControls(key, instance);
                    deferredViews[key] = instance;
                    ReflowRoomPresentation();
                } catch (Exception error) {
                    Destroy(instance);
                    instance = null;
                    Debug.LogException(error, this);
                }
            } else Debug.LogError("Missing create-room panel: " + DeferredResourceRoot + key, this);
            var callbacks = deferredRequests[key];
            deferredRequests.Remove(key);
            foreach (var callback in callbacks) {
                try { callback(instance); } catch (Exception error) { Debug.LogException(error, this); }
            }
        }
        if (this) deferredLoading = false;
    }
}
