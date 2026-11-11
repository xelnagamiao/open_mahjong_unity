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
            Check(Field<Toggle>(panel, "TianDiRenHeToggle").isOn && !Field<Toggle>(panel, "TianDiRenHeToggle").interactable, "启用金锣赛自动勾选并锁定天地人和");
            var tips = Field<Toggle>(panel, "tipsToggle");
            Check(tips.interactable, "金锣赛允许编辑番数提示");
            if (!tips.isOn) Click(tips.gameObject);
            Check(tips.isOn && Field<Toggle>(panel, "CuoHeheToggle").isOn, "金锣赛提示与错和可同时启用");
            Click(tips.gameObject);
            Check(!tips.isOn && Field<Toggle>(panel, "CuoHeheToggle").isOn, "关闭提示不影响金锣赛错和");
            Click(tips.gameObject);
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
                Check(config.TianDiRenHe, "八种天气建房均启用天地人和：" + index);
                Check(config.Tips, "八种天气建房均保留番数提示：" + index);
            } catch (Exception error) { Fail(error); yield break; }
            yield return null;
        }
        try {
            var toggle = Field<Toggle>(panel, "JinluoModeToggle");
            Click(toggle.gameObject);
            Check(!Field<GameObject>(panel, "JinluoWeatherPanel").activeSelf, "再次关闭会收起天气栏");
            Check(!Field<Toggle>(panel, "tipsToggle").isOn, "退出金锣赛恢复普通规则提示和错和互斥");
            Check(Field<Toggle>(panel, "TianDiRenHeToggle").isOn && Field<Toggle>(panel, "TianDiRenHeToggle").interactable, "关闭金锣赛后保留天地人和选择并恢复编辑");
            Click(toggle.gameObject);
            var subRule = Field<TMP_Dropdown>(panel, "SubRuleDropdown");
            subRule.value = 1;
            Check(!toggle.gameObject.activeSelf && !toggle.isOn && !Field<GameObject>(panel, "JinluoWeatherPanel").activeSelf, "切换子规则不会带入金锣赛配置");
            panel.ApplyGuobiaoTierPreset(0);
            Click(toggle.gameObject);
            var subRules = CreateRoomRuleTextConfigCatalog.GetRule("guobiao").SubRules;
            subRule.value = Enumerable.Range(0, subRules.Count).First(index => subRules[index].Key == GuobiaoGameState.BloodBattleSubRule);
            var bloodRequest = (RoomCreationRequest)panel.GetType().GetMethod("BuildRoomRequest", Flags).Invoke(panel, null);
            var bloodConfig = bloodRequest.Configuration as GB_Create_RoomConfig;
            Check(bloodRequest.IsValid && bloodConfig != null && bloodConfig.Tips && !bloodConfig.CuoHe && !bloodConfig.JinluoMode,
                "从金锣切到血战保留提示且关闭不适用的错和");
            panel.ApplyGuobiaoTierPreset(0);
            Click(toggle.gameObject);
            dropdown.value = 5;
            Canvas.ForceUpdateCanvases();
            var rect = (RectTransform)dropdown.transform;
            Check(rect.rect.width > 0 && rect.rect.height > 0 && dropdown.gameObject.activeInHierarchy, "真实天气下拉框具有可交互尺寸");
            Check(dropdown.options.Select(option => option.text).SequenceEqual(JinluoRules.WeatherLabels), "天气选项完整且顺序正确");
            Check(toggle.GetComponentsInChildren<TMP_Text>(true).All(label => (label.fontStyle & FontStyles.Bold) == 0), "新增中文控件保持正常字重");
        } catch (Exception error) { Fail(error); yield break; }
        foreach (bool reversed in new[] { false, true }) {
            var preset = new JObject {
                ["sub_rule"] = "guobiao/standard", ["tips"] = true, ["open_cuohe"] = true,
                ["jinluo_mode"] = true, ["jinluo_weather"] = "dragon_pairs", ["tian_di_ren_he"] = false
            };
            if (reversed) preset = new JObject(preset.Properties().Reverse().Select(property => new JProperty(property.Name, property.Value.DeepClone())));
            panel.OpenForEventPreset("jinluo-validation", "guobiao", preset, (_, __) => { });
            for (int frame = 0; frame < 60 && Field<bool>(panel, "eventPresetLoading"); frame++) yield return null;
            try {
                Check(!Field<bool>(panel, "eventPresetLoading"), "活动预设完成加载：" + reversed);
                var request = (RoomCreationRequest)panel.GetType().GetMethod("BuildRoomRequest", Flags).Invoke(panel, null);
                var config = request.Configuration as GB_Create_RoomConfig;
                Check(request.IsValid && config != null && config.JinluoMode && config.Tips && config.CuoHe && config.TianDiRenHe,
                    "活动预设不受字段顺序影响：" + reversed);
                panel.CloseEventPreset();
                panel.PrepareForRoom();
                panel.gameObject.SetActive(true);
                Check(Field<Toggle>(panel, "JinluoModeToggle").isOn && Field<Toggle>(panel, "tipsToggle").isOn && dropdown.value == 5,
                    "关闭活动预设恢复金锣提示与天气草稿：" + reversed);
            } catch (Exception error) { Fail(error); yield break; }
        }
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
        ValidateRuleBoundaries();
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
    /// <summary>无需场景或外部牌例文件的规则回归；返回通过的断言数，失败时保留具体牌例。</summary>
    public static int ValidateRuleBoundaries() {
        int count = 0;
        void Require(bool condition, string message) {
            if (!condition) throw new InvalidOperationException("Jinluo rule boundary: " + message);
            count++;
        }
        var missingSuitCases = new[] {
            new { Pairs = new[] { 12, 14, 17, 22, 24, 26, 28 }, Score = 27, Allowed = true, Name = "断幺加缺门" },
            new { Pairs = new[] { 11, 14, 17, 22, 24, 26, 28 }, Score = 26, Allowed = true, Name = "含幺九的无字加缺门" },
            new { Pairs = new[] { 12, 14, 17, 22, 24, 26, 45 }, Score = 25, Allowed = false, Name = "缺门但含字" },
            new { Pairs = new[] { 12, 14, 17, 22, 24, 26, 35 }, Score = 26, Allowed = false, Name = "断幺但不缺门" }
        };
        foreach (var item in missingSuitCases) {
            var hand = item.Pairs.SelectMany(tile => new[] { tile, tile }).ToList();
            var result = JinluoRules.Check(hand, new List<string>(), new List<string>(), hand.Last(), "missing_suit", 41, 41);
            Require(result.Item1 == item.Score, item.Name + "的实际国标番数");
            Require(JinluoRules.MatchesWeather(item.Pairs.ToList(), "missing_suit", 41, 41) == item.Allowed, item.Name + "的天气资格");
            Require(JinluoRules.IsAllowed(result, hand, new List<string>(), "missing_suit", 41, 41) == item.Allowed, item.Name + "的最终和牌资格");
            if (item.Name == "断幺加缺门") {
                Require(result.Item2.Contains("断幺") && result.Item2.Contains("缺一门"), "断幺加缺门的实际番型");
                Require(!result.Item2.Contains("无字"), "断幺不再计无字时仍可满足无字加缺门天气");
            }
        }
        var weatherCases = new[] {
            new { Weather = "dragon_pairs", Yes = new[] { 11, 14, 17, 22, 35, 45, 46 }, No = new[] { 11, 14, 17, 22, 35, 41, 45 } },
            new { Weather = "seat_round_winds", Yes = new[] { 11, 14, 17, 22, 35, 41, 42 }, No = new[] { 11, 14, 17, 22, 35, 41, 43 } },
            new { Weather = "terminal_pairs", Yes = new[] { 11, 19, 21, 24, 35, 41, 45 }, No = new[] { 11, 14, 21, 24, 35, 41, 45 } },
            new { Weather = "all_simples", Yes = new[] { 12, 14, 17, 22, 26, 35, 38 }, No = new[] { 11, 14, 17, 22, 26, 35, 38 } },
            new { Weather = "missing_suit", Yes = new[] { 12, 14, 17, 22, 24, 26, 28 }, No = new[] { 12, 14, 17, 22, 24, 26, 35 } },
            new { Weather = "consecutive_pairs", Yes = new[] { 11, 12, 17, 22, 26, 35, 45 }, No = new[] { 19, 21, 24, 27, 35, 41, 45 } },
            new { Weather = "same_rank_pairs", Yes = new[] { 11, 14, 17, 21, 26, 35, 45 }, No = new[] { 11, 11, 17, 22, 26, 35, 45 } },
            new { Weather = "four_of_a_kind", Yes = new[] { 11, 11, 17, 22, 26, 35, 45 }, No = new[] { 11, 14, 17, 22, 26, 35, 45 } }
        };
        foreach (var item in weatherCases) {
            Require(JinluoRules.MatchesWeather(item.Yes.ToList(), item.Weather, 42, 41), item.Weather + "成立牌例");
            Require(!JinluoRules.MatchesWeather(item.No.ToList(), item.Weather, 42, 41), item.Weather + "不成立牌例");
        }
        var thresholdHand = missingSuitCases[0].Pairs.SelectMany(tile => new[] { tile, tile }).ToList();
        var thresholdCases = new[] {
            new { Way = new string[0], Score = 27, Allowed = false, Name = "27素番" },
            new { Way = new[] { "花牌" }, Score = 28, Allowed = false, Name = "27素番加一花" },
            new { Way = new[] { "自摸" }, Score = 28, Allowed = true, Name = "28素番" },
            new { Way = new[] { "自摸", "花牌" }, Score = 29, Allowed = true, Name = "28素番加一花" }
        };
        foreach (var item in thresholdCases) {
            // 本手不符合箭牌天气或特殊七对，直接验证28素番门槛及不计花牌。
            var result = JinluoRules.Check(thresholdHand, new List<string>(), item.Way.ToList(), thresholdHand.Last(), "dragon_pairs", 41, 41);
            Require(result.Item1 == item.Score, item.Name + "的实际国标番数");
            Require(JinluoRules.IsAllowed(result, thresholdHand, new List<string>(), "dragon_pairs", 41, 41) == item.Allowed, item.Name + "的最终和牌资格");
        }
        return count;
    }
}
#endif
