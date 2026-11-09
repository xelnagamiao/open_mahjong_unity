#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Salasasa.Home;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>Opt-in MainScene integration checks with a real Fuzhou replay; leaves an interactive preview.</summary>
public sealed class TileBadgeRuntimeValidation : MonoBehaviour {
    private static string folder;
    private readonly List<string> checks = new List<string>();
    private readonly Dictionary<string, int?> preferences = new Dictionary<string, int?>();
    private ConfigManager config;
    private AppLanguage originalLanguage;
    private bool originalLabels;
    private readonly List<string> errors = new List<string>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Configure() {
        folder = null;
        var args = Environment.GetCommandLineArgs();
        int index = Array.IndexOf(args, "-tile-badge-validation");
        if (index >= 0 && index + 1 < args.Length) folder = args[index + 1];
#if UNITY_EDITOR
        if (folder == null) folder = UnityEditor.SessionState.GetString("TileBadgeValidationFolder", "");
#endif
        if (string.IsNullOrEmpty(folder)) return;
        ConfigManager.BuildForSteam = false;
        Application.runInBackground = true;
    }
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Launch() {
        if (string.IsNullOrEmpty(folder)) return;
        NetworkManager.Instance.enabled = false;
        foreach (var chat in Resources.FindObjectsOfTypeAll<ChatManager>()) chat.enabled = false;
        new GameObject(nameof(TileBadgeRuntimeValidation)).AddComponent<TileBadgeRuntimeValidation>();
    }
    private void Log(string message, string stack, LogType kind) {
        if (kind == LogType.Error || kind == LogType.Exception) errors.Add(message + "\n" + stack);
    }
    private IEnumerator Start() {
        Directory.CreateDirectory(folder);
        config = ConfigManager.Instance;
        originalLanguage = ConfigManager.CurrentLanguage;
        originalLabels = config.TileCornerLabelsEnabled;
        foreach (string key in new[] { "AppLanguage", ConfigManager.TileCornerLabelsKey, ConfigManager.TileCornerIntroKey, SettingsStyleCollection.StylePreference })
            preferences[key] = PlayerPrefs.HasKey(key) ? PlayerPrefs.GetInt(key) : (int?)null;
        Application.logMessageReceived += Log;
        var stack = new Stack<IEnumerator>(); stack.Push(Verify());
        Exception failure = null;
        while (stack.Count > 0) {
            object yielded = null; bool more = false;
            try { more = stack.Peek().MoveNext(); if (more) yielded = stack.Peek().Current; }
            catch (Exception error) { failure = error; break; }
            if (!more) stack.Pop();
            else if (yielded is IEnumerator nested) stack.Push(nested);
            else yield return yielded;
        }
        Application.logMessageReceived -= Log;
        RestorePreferences(failure == null && errors.Count == 0);
        File.WriteAllText(Path.Combine(folder, "runtime-results.json"), JsonConvert.SerializeObject(new {
            success = failure == null && errors.Count == 0, count = checks.Count, checks, errors, exception = failure?.ToString()
        }, Formatting.Indented));
        if (failure != null) Debug.LogException(failure);
        else if (errors.Count == 0) {
            Debug.Log("Tile badge runtime validation passed: " + checks.Count);
            TileCornerLabelChoice.Show(config);
        }
    }
    private void RestorePreferences(bool leaveEnglishPreview) {
        if (!config) return;
        PlayerPrefs.SetInt(ConfigManager.TileCornerIntroKey, 1); // Restoring language must not schedule another onboarding.
        config.SetLanguageMode((int)(leaveEnglishPreview ? AppLanguage.English : originalLanguage));
        config.SetTileCornerLabelsEnabled(originalLabels);
        foreach (var preference in preferences) {
            if (preference.Value.HasValue) PlayerPrefs.SetInt(preference.Key, preference.Value.Value);
            else PlayerPrefs.DeleteKey(preference.Key);
        }
        PlayerPrefs.Save();
    }
    private void Check(bool condition, string description) {
        if (!condition) throw new InvalidOperationException(description);
        checks.Add(description);
    }
    private IEnumerator Settle() { yield return new WaitForSecondsRealtime(.5f); Canvas.ForceUpdateCanvases(); yield return null; }
    private IEnumerator Capture(string name) {
        yield return new WaitForEndOfFrame();
        ScreenCapture.CaptureScreenshot(Path.Combine(folder, name + ".png"));
        yield return new WaitForEndOfFrame(); yield return null;
    }
    private static TMP_Text Badge(Component card, string name) => card.transform.Find("TileFaceOverlay/" + name)?.GetComponent<TMP_Text>();
    private IEnumerator Verify() {
        yield return null;
        var windows = WindowsManager.Instance;
        windows.ResetToLoginUI();
        config.SetLanguageMode((int)AppLanguage.SimplifiedChinese);
        PlayerPrefs.DeleteKey(ConfigManager.TileCornerIntroKey);
        config.SetLanguageMode((int)AppLanguage.English);
        yield return Settle();
        Check(!TileCornerLabelChoice.Current, "switching to English on login defers the choice");
        var data = JObject.Parse(File.ReadAllText(Path.Combine(folder, "FuzhouRewindReplay.json")));
        var players = data["players"].ToObject<PlayerRecordInfo[]>();
        typeof(UserDataManager).GetProperty("UserId").SetValue(UserDataManager.Instance, players[0].user_id);
        windows.SwitchWindow("menu");
        config.OfferEnglishTileLabels(); config.OfferEnglishTileLabels();
        yield return Settle();
        var choice = TileCornerLabelChoice.Current;
        Check(choice && FindObjectsByType<TileCornerLabelChoice>(UnityEngine.FindObjectsSortMode.None).Length == 1, "first English login opens exactly one choice");
        var enabled = choice.transform.Find("Panel/WithLabels/Examples").GetComponentsInChildren<StaticCard>();
        var disabled = choice.transform.Find("Panel/WithoutLabels/Examples").GetComponentsInChildren<StaticCard>();
        Check(enabled.Length == 8 && disabled.Length == 8, "examples use sixteen real tile prefab instances");
        Check(enabled.All(c => Badge(c, "TileCornerLabel")?.gameObject.activeInHierarchy == true), "right example shows labels");
        Check(disabled.All(c => !Badge(c, "TileCornerLabel")), "left example has no labels");
        Check(enabled.All(c => Badge(c, "TileCornerLabel").rectTransform.anchorMax == Vector2.one), "all identification labels use the upper right corner");
        Check(enabled.All(c => PlatformTypography.HeavyFace(Badge(c, "TileCornerLabel").font)), "examples use the bold Latin index typeface");
        Check(Badge(enabled.First(c => c.TileId == 11), "TileCornerLabel").color == TileCornerLabel.RedInk
            && Badge(enabled.First(c => c.TileId == 41), "TileCornerLabel").color == TileCornerLabel.BlackInk, "examples show both red and black indices");
        yield return Capture("choice-english");
        choice.transform.Find("Panel/WithoutLabels/Choose").GetComponent<Button>().onClick.Invoke();
        yield return Settle();
        Check(!TileCornerLabelChoice.Current && config.HasChosenEnglishTileLabels && !config.TileCornerLabelsEnabled, "left choice persists disabled preference");
        config.SetLanguageMode((int)AppLanguage.SimplifiedChinese);
        config.SetLanguageMode((int)AppLanguage.English);
        config.OfferEnglishTileLabels();
        yield return Settle();
        Check(!TileCornerLabelChoice.Current, "language switching and another login offer do not repeat confirmed choice");
        PlayerPrefs.DeleteKey(ConfigManager.TileCornerIntroKey);
        config.SetLanguageMode((int)AppLanguage.SimplifiedChinese);
        config.SetLanguageMode((int)AppLanguage.English);
        yield return Settle();
        Check(!TileCornerLabelChoice.Current, "language changes after login do not open the English login prompt");
        config.OfferEnglishTileLabels();
        yield return Settle();
        Check(TileCornerLabelChoice.Current, "next English login opens the unhandled choice");
        TileCornerLabelChoice.Current.transform.Find("Panel/Close").GetComponent<Button>().onClick.Invoke();
        yield return Settle();
        Check(!TileCornerLabelChoice.Current && config.HasChosenEnglishTileLabels && !config.TileCornerLabelsEnabled, "close retains preference and acknowledges the prompt");
        config.OfferEnglishTileLabels(); yield return Settle();
        Check(!TileCornerLabelChoice.Current, "closed prompt is not repeated on another English login");
        PlayerPrefs.DeleteKey(ConfigManager.TileCornerIntroKey);
        config.OfferEnglishTileLabels(); yield return Settle();
        TileCornerLabelChoice.Current.transform.Find("Panel/WithLabels/Choose").GetComponent<Button>().onClick.Invoke();
        yield return Settle();
        Check(config.TileCornerLabelsEnabled && config.HasChosenEnglishTileLabels, "right choice persists enabled preference");
        windows.SwitchWindow("config");
        yield return Settle();
        var settings = FindObjectsByType<SettingsStyleCollection>(UnityEngine.FindObjectsSortMode.None).Single();
        settings.SelectCategory(2);
        foreach (int theme in new[] { 0, 2, 1 }) {
            settings.SelectVariant(theme);
            var row = settings.controls.Single(f => f.key == "TileCornerLabelsRow");
            var page = row.root.GetComponentInParent<SettingsBakedPage>();
            page.GetComponent<SettingsPagePresentation>().EnsureVisible(row.root);
            row.options[0].onClick.Invoke(); yield return null;
            Check(!config.TileCornerLabelsEnabled && row.dropdown.value == 0, "theme " + theme + " disables labels");
            row.options[1].onClick.Invoke(); yield return null;
            Check(config.TileCornerLabelsEnabled && row.dropdown.value == 1, "theme " + theme + " enables labels");
        }
        yield return Settle();
        yield return Capture("settings-english");
        NormalGameStateManager.Instance.ResetForExit();
        windows.SwitchWindow("game"); yield return null;
        var record = GameRecordManager.Instance;
        record.ResetForSessionEnd();
        record.LoadRecord(data["record"].ToString(), players);
        windows.SwitchWindow("recordscene");
        record.GotoSelectNode((int)data["validation"]["round_1_opening"]["node"]);
        yield return Settle();
        int gold = FuzhouTableIndicators.DisplayedInfo.gold_tile;
        Check(RoundPanel.Instance.GetComponentsInChildren<TMP_Text>().Any(t => t.text == "Ranked"), "real replay venue displays Ranked in English");
        Check(gold > 0, "real Fuzhou replay reveals gold");
        var playerWithGold = record.recordPlayer_to_info.Values.First(p => p.tileList.Contains(gold));
        record.SwitchRecordPerspectiveToOriginalPlayerIndex(playerWithGold.originalPlayerIndex);
        yield return Settle();
        var hand = GameCanvas.Instance.HandCardsContainer.GetComponentsInChildren<TileCard>();
        Check(hand.All(c => Badge(c, "TileCornerLabel")?.gameObject.activeInHierarchy == true), "all visible hand tiles receive identification labels");
        var goldCard = hand.First(c => c.tileId == gold);
        var smallGold = RoundPanel.Instance.GetComponentsInChildren<StaticCard>().First(c => c.TileId == gold);
        float Ratio(Component card, UnityEngine.UI.Image body) {
            var label = Badge(card, "RuleTileBadge");
            var overlay = (RectTransform)label.transform.parent;
            float bodyWidth = overlay.rect.width * overlay.localScale.x;
            return label.fontSize * overlay.localScale.x / bodyWidth;
        }
        Check(Mathf.Abs(Ratio(goldCard, goldCard.BadgeBody) - Ratio(smallGold, smallGold.BadgeBody)) < .001f, "RoundPanel and hand gold markers share the same proportion");
        var body = goldCard.BadgeBody.transform;
        var mark = Badge(goldCard, "RuleTileBadge");
        Check(mark.rectTransform.anchorMax == new Vector2(0,1) && Badge(goldCard,"TileCornerLabel").rectTransform.anchorMax == Vector2.one, "gold left and numeric index right coexist on the real hand");
        Vector3 relation = body.InverseTransformPoint(mark.transform.position);
        Vector3 start = mark.transform.position;
        var hover = goldCard.GetComponent<HoverEventTrigger>();
        hover.SetArmedLift(true); yield return Settle();
        Check(Vector3.Distance(start, mark.transform.position) > 1, "hand selection lifts the gold marker");
        Check(Vector3.Distance(relation, body.InverseTransformPoint(mark.transform.position)) < .001f, "marker remains attached during lift");
        yield return Capture("fuzhou-hover");
        hover.SetArmedLift(false); yield return Settle();
        Check(Vector3.Distance(start, mark.transform.position) < .01f, "marker returns with the hand tile");
        var title = record.gameRecord.gameTitle;
        object oldTier = title["match_tier"];
        foreach (string tier in new[] { "intermediate", "advanced", "mcrpl" }) {
            title["match_tier"] = tier; TileCornerLabel.RefreshAll(); yield return null;
            Check(!TileCornerLabel.Enabled && hand.All(c => !Badge(c, "TileCornerLabel").gameObject.activeSelf), "labels suppressed in " + tier + " room");
            Check(mark.gameObject.activeInHierarchy, "gold marker retained in " + tier + " room");
        }
        title["match_tier"] = "beginner"; TileCornerLabel.RefreshAll(); yield return null;
        Check(TileCornerLabel.Enabled && hand.All(c => Badge(c, "TileCornerLabel").gameObject.activeSelf), "beginner room restores labels without losing preference");
        title["match_tier"] = oldTier;
        config.SetTileCornerLabelsEnabled(false); yield return null;
        Check(hand.All(c => !Badge(c, "TileCornerLabel").gameObject.activeSelf) && mark.gameObject.activeInHierarchy, "setting hides only helpers and preserves rule badges");
        Check(mark.rectTransform.anchorMax == Vector2.one, "gold returns to upper right when helpers are disabled");
        config.SetTileCornerLabelsEnabled(true);
        config.SetLanguageMode((int)AppLanguage.SimplifiedChinese);
        yield return Settle();
        yield return Capture("fuzhou-table");
        Check(record.currentNode == (int)data["validation"]["round_1_opening"]["node"], "interactive replay remains at the verified node");
    }
}
#endif
