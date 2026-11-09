using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Salasasa.Home;
using UnityEngine;

public sealed class HeaderPresentationValidation : MonoBehaviour
{
    readonly List<string> checks = new List<string>(), errors = new List<string>();
    string output;
    HeaderPresentation presentation;
    int oldSide;
    AppLanguage oldLanguage;
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        string path = CompleteLobbyEnvironment.Value("-header-capture");
        if (string.IsNullOrEmpty(path)) return;
        var runner = new GameObject("HeaderPresentationValidation").AddComponent<HeaderPresentationValidation>();
        runner.output = path; runner.StartCoroutine(runner.Run());
    }
    IEnumerator Run()
    {
        Directory.CreateDirectory(output); Application.logMessageReceived += Log;
        var stack = new Stack<IEnumerator>(); stack.Push(Validate());
        while (stack.Count > 0)
        {
            bool more; object value = null;
            try { more = stack.Peek().MoveNext(); if (more) value = stack.Peek().Current; }
            catch (Exception ex) { errors.Add(ex.ToString()); break; }
            if (!more) (stack.Pop() as IDisposable)?.Dispose();
            else if (value is IEnumerator nested) stack.Push(nested); else yield return value;
        }
        while (stack.Count > 0) (stack.Pop() as IDisposable)?.Dispose();
        if (presentation)
        {
            ConfigManager.Instance.SetLanguageMode((int)oldLanguage);
            presentation.navigation.SelectSide(oldSide); presentation.navigation.owner.Open("community");
        }
        yield return Settle();
        Application.logMessageReceived -= Log;
        File.WriteAllText(Path.Combine(output, "result.json"), JsonConvert.SerializeObject(new { success = errors.Count == 0, width = Screen.width, height = Screen.height, checks, errors }, Formatting.Indented));
        Debug.Log("Header presentation validation: " + checks.Count + " checks; " + errors.Count + " errors");
        if (CompleteLobbyEnvironment.Has("-capture-quit")) Application.Quit(errors.Count == 0 ? 0 : 2);
    }
    IEnumerator Validate()
    {
        float deadline = Time.realtimeSinceStartup + 90;
        while ((!WindowsManager.Instance || WindowsManager.Instance.GetCurrentWindow() != "menu") && Time.realtimeSinceStartup < deadline) yield return null;
        Check(WindowsManager.Instance && WindowsManager.Instance.GetCurrentWindow() == "menu", "real MainScene guest login");
        presentation = FindObjectsByType<HeaderPresentation>(FindObjectsInactive.Include, UnityEngine.FindObjectsSortMode.None).Single();
        var nav = presentation.navigation; var config = ConfigManager.Instance;
        oldSide = nav.Side; oldLanguage = ConfigManager.CurrentLanguage;
        Check(presentation.buttons.Length == nav.tabs.Length + 2, "all visible navigation entries have independent presentation");
        Check(presentation.buttons.Select(x => x.button.route).Distinct().Count() == presentation.buttons.Length, "routes are unique");
        Check(nav.ButtonFor("community") && nav.owner.pageRoot.Find("CommunityPanel"), "community is baked into MainScene");
        foreach (AppLanguage language in Enum.GetValues(typeof(AppLanguage)))
        {
            config.SetLanguageMode((int)language);
            foreach (int side in new[] {0,1,2,3})
            {
                nav.SelectSide(side); nav.owner.Open("community"); yield return Settle();
                foreach (var binding in presentation.buttons)
                {
                    var entry = binding.Entry; Check(entry != null, "profile: " + binding.button.route);
                    if (!binding.caption) continue;
                    Check(binding.caption.text == entry.Label(language), "localized label: " + language + "/" + side + "/" + entry.route);
                    Check(!binding.caption.isTextOverflowing, "label fits: " + language + "/" + side + "/" + entry.route);
                    Check(binding.outline == null || binding.outline.text == binding.caption.text, "outline follows label: " + entry.route);
                    Check(binding.shadow == null || binding.shadow.text == binding.caption.text, "shadow follows label: " + entry.route);
                    foreach (char character in binding.caption.text) Check(presentation.profile.font.HasCharacter(character, true), "font glyph " + (int)character);
                    Check(OnScreen(binding.caption.rectTransform), "caption stays on screen: " + entry.route);
                }
                var list = presentation.buttons.Where(x => x.button.gameObject.activeInHierarchy).ToArray();
                for (int i=0; i<list.Length; i++) for (int j=i+1; j<list.Length; j++)
                    Check(!ScreenRect((RectTransform)list[i].button.transform).Overlaps(ScreenRect((RectTransform)list[j].button.transform)), "entry hit areas separate: " + side + "/" + list[i].button.route + "/" + list[j].button.route);
                if (side == 0 || language == AppLanguage.English) yield return Snap(language + "-side-" + side);
            }
        }
        config.SetLanguageMode(0); nav.SelectSide(0); yield return Settle();
        var community = presentation.buttons.Single(x=>x.button.route=="community");
        var entryToEdit = community.Entry;
        float size = entryToEdit.fontSize; Vector2 iconSize = entryToEdit.iconSize;
        entryToEdit.fontSize = size + 3; presentation.Refresh(); yield return Settle();
        Check(community.icon.rectTransform.sizeDelta == iconSize, "editing text size leaves icon size unchanged");
        Check(community.caption.fontSizeMax == size + 3, "text size is independently applied");
        entryToEdit.fontSize = size; entryToEdit.iconSize = iconSize + new Vector2(4,4); presentation.Refresh(); yield return Settle();
        Check(community.caption.fontSizeMax == size, "editing icon size leaves text size unchanged");
        Check(community.icon.rectTransform.sizeDelta == iconSize + new Vector2(4,4), "icon size is independently applied");
        entryToEdit.iconSize = iconSize; presentation.Refresh(); yield return Settle();
        nav.Execute("utilities"); yield return Settle(); Check(nav.UtilityOpen, "utility drawer opens");
        nav.Execute("community"); yield return Settle(); Check(!nav.UtilityOpen, "header click closes utility drawer and routes directly");
        nav.Execute("community"); yield return Settle();
        if (WindowsManager.Instance.GetCurrentWindow() != "menu") { nav.Execute("community"); yield return Settle(); }
        Check(WindowsManager.Instance.GetCurrentWindow() == "menu", "repeated community click returns to home");
        yield return Snap("home-chinese"); nav.Execute("community"); yield return Settle(); yield return Snap("community-final");
    }
    Rect ScreenRect(RectTransform rect)
    {
        var corners = new Vector3[4]; rect.GetWorldCorners(corners); var canvas = rect.GetComponentInParent<Canvas>();
        var camera = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
        var a = RectTransformUtility.WorldToScreenPoint(camera,corners[0]); var b = RectTransformUtility.WorldToScreenPoint(camera,corners[2]);
        return new Rect(a,b-a);
    }
    bool OnScreen(RectTransform rect) { var r = ScreenRect(rect); return r.xMin>=-1 && r.yMin>=-1 && r.xMax<=Screen.width+1 && r.yMax<=Screen.height+1; }
    IEnumerator Settle() { yield return new WaitForSecondsRealtime(.4f); yield return new WaitForEndOfFrame(); }
    IEnumerator Snap(string name) { yield return new WaitForEndOfFrame(); ScreenCapture.CaptureScreenshot(Path.Combine(output,name+".png")); yield return null; }
    void Check(bool valid,string name) { if (!valid) throw new InvalidOperationException(name); checks.Add(name); }
    void Log(string message,string trace,LogType type) { if (type==LogType.Error || type==LogType.Exception || type==LogType.Assert) errors.Add(message+"\n"+trace); }
}
