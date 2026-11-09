#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>按显式启动参数运行的金锣赛回归，使用 MainScene 的真实建房面板。</summary>
public sealed class JinluoRuntimeValidation : MonoBehaviour {
    static string folder;
    static readonly BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic;
    static T Field<T>(object owner, string key) => (T)owner.GetType().GetField(key, Flags).GetValue(owner);
    static T Find<T>() where T : Component => Resources.FindObjectsOfTypeAll<T>().First(item => item.gameObject.scene.IsValid());
    readonly List<string> checks = new List<string>();
    void Check(bool condition, string text) {
        if (!condition) throw new InvalidOperationException(text);
        checks.Add(text);
        File.WriteAllText(Path.Combine(folder, "runtime-checks.json"), JsonConvert.SerializeObject(checks, Formatting.Indented));
    }
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Configure() {
        folder = null;
        var args = Environment.GetCommandLineArgs();
        int index = Array.IndexOf(args, "--verify-jinluo");
        if (index >= 0 && index + 1 < args.Length) folder = Path.GetFullPath(args[index + 1]);
#if UNITY_EDITOR
        if (folder == null) folder = UnityEditor.SessionState.GetString("JinluoValidationOutput", "");
        UnityEditor.SessionState.EraseString("JinluoValidationOutput");
#endif
        if (string.IsNullOrEmpty(folder)) { folder = null; return; }
        Directory.CreateDirectory(folder);
        ConfigManager.gameUrl = "ws://localhost:8081/game";
        ConfigManager.chatUrl = "ws://localhost:8083/chat";
        ConfigManager.webApiUrl = "http://localhost:3000";
        Application.runInBackground = true;
    }
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Launch() {
        if (folder == null) return;
        // 界面验收不恢复已缓存的账号或已有对局，也不占用共享测试服务。
        NetworkManager.Instance.enabled = false;
        foreach (var chat in Resources.FindObjectsOfTypeAll<ChatManager>()) chat.enabled = false;
        new GameObject("JinluoValidation").AddComponent<JinluoRuntimeValidation>();
    }
    IEnumerator Start() {
        for (int frame = 0; frame < 20; frame++) yield return null;
        CreatePanel panel = null;
        try {
            Check(ValidateCalculations(Path.Combine(folder, "fixtures.json")) > 0, "客户端与服务端牌例一致");
            WindowsManager.Instance.SwitchWindow("room");
            RoomWindowsManager.Instance.SwitchRoomWindow("createRoom");
            panel = Find<CreatePanel>();
            panel.PrepareForRoom();
            panel.gameObject.SetActive(true);
            panel.ApplyGuobiaoTierPreset(0);
        } catch (Exception error) { Fail(error); yield break; }
        yield return new WaitForSecondsRealtime(.5f);
        try {
            var toggle = Field<Toggle>(panel, "JinluoModeToggle");
            var weatherPanel = Field<GameObject>(panel, "JinluoWeatherPanel");
            Check(toggle.gameObject.activeInHierarchy && !weatherPanel.activeSelf, "关闭金锣赛时天气栏隐藏");
            Click(toggle.gameObject);
            Check(toggle.isOn && weatherPanel.activeSelf, "点击金锣赛后天气栏显示");
            Check(Field<Toggle>(panel, "CuoHeheToggle").isOn && !Field<Toggle>(panel, "CuoHeheToggle").interactable, "启用金锣赛自动启用错和");
            Check(!Field<Toggle>(panel, "tipsToggle").isOn, "沿用提示与错和互斥设置");
            Check(!Field<Toggle>(panel, "InputHepaiLimitToggle").gameObject.activeSelf, "金锣赛使用独立起和条件");
        } catch (Exception error) { Fail(error); yield break; }
        yield return new WaitForSecondsRealtime(.5f);
        var dropdown = Field<TMP_Dropdown>(panel, "JinluoWeatherDropdown");
        for (int index = 0; index < JinluoRules.WeatherKeys.Length; index++) {
            try {
                dropdown.value = index;
                // BuildRoomRequest exercises the real form and room builder; it sends no network message.
                var request = (RoomCreationRequest)panel.GetType().GetMethod("BuildRoomRequest", Flags).Invoke(panel, null);
                var config = request.Configuration as GB_Create_RoomConfig;
                Check(request.IsValid && config != null && config.JinluoMode && config.JinluoWeather == JinluoRules.WeatherKeys[index], "八种天气建房传输：" + index);
                Check(config.CuoHe && config.HepaiLimit == 8, "建房固定错和和基础8番：" + index);
            } catch (Exception error) { Fail(error); yield break; }
            yield return null;
        }
        try {
            var toggle = Field<Toggle>(panel, "JinluoModeToggle");
            Click(toggle.gameObject);
            Check(!Field<GameObject>(panel, "JinluoWeatherPanel").activeSelf, "再次关闭会收起天气栏");
            Click(toggle.gameObject);
            var subRule = Field<TMP_Dropdown>(panel, "SubRuleDropdown");
            subRule.value = 1;
            Check(!toggle.gameObject.activeSelf && !toggle.isOn && !Field<GameObject>(panel, "JinluoWeatherPanel").activeSelf, "切换子规则不会带入金锣赛配置");
            panel.ApplyGuobiaoTierPreset(0);
            Click(toggle.gameObject);
            dropdown.value = 5;
            Canvas.ForceUpdateCanvases();
            var rect = (RectTransform)dropdown.transform;
            Check(rect.rect.width > 0 && rect.rect.height > 0 && dropdown.gameObject.activeInHierarchy, "真实天气下拉框具有可交互尺寸");
            Check(dropdown.options.Select(option => option.text).SequenceEqual(JinluoRules.WeatherLabels), "天气选项完整且顺序正确");
            Check(toggle.GetComponentsInChildren<TMP_Text>(true).All(label => (label.fontStyle & FontStyles.Bold) == 0), "新增中文控件保持正常字重");
        } catch (Exception error) { Fail(error); yield break; }
        yield return new WaitForSecondsRealtime(.5f);
        ScreenCapture.CaptureScreenshot(Path.Combine(folder, "jinluo-controls.png"));
        dropdown.Show();
        yield return new WaitForSecondsRealtime(.5f);
        ScreenCapture.CaptureScreenshot(Path.Combine(folder, "jinluo-weathers.png"));
        yield return new WaitForSecondsRealtime(.5f);
        dropdown.Hide();
        File.WriteAllText(Path.Combine(folder, "runtime-success.json"), JsonConvert.SerializeObject(new { checks = checks.Count, scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().path }));
    }
    static void Click(GameObject target) => ExecuteEvents.Execute(target, new PointerEventData(EventSystem.current) { button = PointerEventData.InputButton.Left }, ExecuteEvents.pointerClickHandler);
    void Fail(Exception error) {
        File.WriteAllText(Path.Combine(folder, "runtime-failure.txt"), error.ToString());
        Debug.LogException(error);
    }
    public static int ValidateCalculations(string file) {
        var fixtures = JArray.Parse(File.ReadAllText(file));
        foreach (JObject fixture in fixtures) {
            var hand = fixture["hand"].ToObject<List<int>>();
            var melds = fixture["melds"].ToObject<List<string>>();
            var way = fixture["way"].ToObject<List<string>>();
            var result = JinluoRules.Check(hand, melds, way, (int)fixture["tile"], (string)fixture["weather"], (int)fixture["seatWind"], (int)fixture["roundWind"]);
            bool allowed = JinluoRules.IsAllowed(result, hand, melds, (string)fixture["weather"], (int)fixture["seatWind"], (int)fixture["roundWind"]);
            if (result.Item1 != (int)fixture["score"] || allowed != (bool)fixture["allowed"])
                throw new Exception("Jinluo Python/C# mismatch: " + fixture.ToString(Formatting.None) + " -> " + result.Item1 + "," + allowed);
        }
        return fixtures.Count;
    }
}
#endif
