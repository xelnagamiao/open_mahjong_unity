using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;

public sealed class CardPackAppearance
{
    readonly Dictionary<string, string> values = new Dictionary<string, string>();
    static readonly string[] ColorKeys = { "back", "side", "back_edge", "front_edge", "face" };
    static readonly string[] BrightnessKeys = { "back_brightness", "face_brightness", "front_edge_brightness", "back_edge_brightness" };
    static readonly string[] ImageKeys = { "hand_background", "hand_back", "table_background", "table_back" };
    static readonly string[] ImageNames = { "hand-bg.png", "hand-back.png", "table-bg.png", "table-back.png" };
    public IEnumerable<string> Images {
        get { foreach (string key in ImageKeys) if (values.TryGetValue(key, out string value) && value.Length > 0) yield return value; }
    }
    public static bool IsSurface(string name) => Array.Exists(ImageNames, item => item.Equals(name, StringComparison.OrdinalIgnoreCase));
    static float Number(string value, float min, float max)
    {
        if (!float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out float number)
            || float.IsNaN(number) || float.IsInfinity(number) || number < min || number > max)
            throw new FormatException("外观数值无效: " + value);
        return number;
    }
    static Color ParseColor(string value)
    {
        string[] parts = value.Split(',');
        if (parts.Length != 4) throw new FormatException("颜色需为四个 0–1 数值");
        return new Color(Number(parts[0], 0, 1), Number(parts[1], 0, 1), Number(parts[2], 0, 1), Number(parts[3], 0, 1));
    }
    public void Set(string key, string value)
    {
        if (Array.IndexOf(ColorKeys, key) >= 0) ParseColor(value);
        else if (Array.IndexOf(BrightnessKeys, key) >= 0) Number(value, -100, 100);
        else if (key == "back_edge_mode" || key == "front_edge_mode") {
            if (value != "0" && value != "1" && value != "2") throw new FormatException("无效的牌边模式");
        }
        else if (key == "use_background" || key == "use_solid" || key == "hand_back_follow") {
            if (value != "true" && value != "false") throw new FormatException("开关需为 true 或 false");
        }
        else {
            int image = Array.IndexOf(ImageKeys, key);
            if (image < 0 || (value.Length > 0 && value != ImageNames[image])) throw new FormatException("无效的外观参数: " + key);
        }
        values[key] = value;
    }
    static string Format(float value) => value.ToString("0.#########", CultureInfo.InvariantCulture);
    static string FormatColor(Color value) => string.Join(",", Format(value.r), Format(value.g), Format(value.b), Format(value.a));
    public static CardPackAppearance Capture(Card3DAppearance appearance, bool tableBack, bool tableBackground)
    {
        var result = new CardPackAppearance();
        Color[] colors = { appearance.back, appearance.side, appearance.backEdge, appearance.frontEdge, appearance.face };
        for (int i = 0; i < colors.Length; i++) result.Set(ColorKeys[i], FormatColor(colors[i]));
        float[] brightness = { appearance.backBrightness, appearance.faceBrightness, appearance.frontEdgeBrightness, appearance.backEdgeBrightness };
        for (int i = 0; i < brightness.Length; i++) result.Set(BrightnessKeys[i], Format(brightness[i] * 100));
        result.Set("back_edge_mode", appearance.backEdgeMode.ToString(CultureInfo.InvariantCulture));
        result.Set("front_edge_mode", appearance.frontEdgeMode.ToString(CultureInfo.InvariantCulture));
        result.Set("use_background", appearance.useBackground ? "true" : "false");
        result.Set("use_solid", appearance.useSolid ? "true" : "false");
        result.Set("hand_back_follow", ConfigManager.Instance.HandBackAutoFollow ? "true" : "false");
        result.Set("hand_background", "hand-bg.png"); result.Set("hand_back", "hand-back.png");
        result.Set("table_background", tableBackground ? "table-bg.png" : "");
        result.Set("table_back", tableBack ? "table-back.png" : "");
        return result;
    }
    public void Append(StringBuilder text)
    {
        text.Append("[appearance]\n");
        foreach (var entry in values) text.Append(entry.Key).Append('=').Append(entry.Value).Append('\n');
    }
    public void Apply(TilePackImporter.Result imported, string name, Action done, Action<string> error)
    {
        var config = ConfigManager.Instance;
        if (config == null) { error?.Invoke("卡牌设置尚未就绪"); return; }
        var appearance = config.CaptureCard3DAppearance();
        Color ColorValue(string key, Color fallback) => values.TryGetValue(key, out string v) ? ParseColor(v) : fallback;
        float Brightness(string key, float fallback) => values.TryGetValue(key, out string v) ? Number(v, -100, 100) / 100 : fallback;
        appearance.back = ColorValue("back", appearance.back); appearance.side = ColorValue("side", appearance.side);
        appearance.backEdge = ColorValue("back_edge", appearance.backEdge); appearance.frontEdge = ColorValue("front_edge", appearance.frontEdge);
        appearance.face = ColorValue("face", appearance.face);
        appearance.backBrightness = Brightness("back_brightness", appearance.backBrightness);
        appearance.faceBrightness = Brightness("face_brightness", appearance.faceBrightness);
        appearance.frontEdgeBrightness = Brightness("front_edge_brightness", appearance.frontEdgeBrightness);
        appearance.backEdgeBrightness = Brightness("back_edge_brightness", appearance.backEdgeBrightness);
        if (values.TryGetValue("back_edge_mode", out var backMode)) appearance.backEdgeMode = int.Parse(backMode);
        if (values.TryGetValue("front_edge_mode", out var frontMode)) appearance.frontEdgeMode = int.Parse(frontMode);
        if (values.TryGetValue("use_background", out var background)) appearance.useBackground = background == "true";
        if (values.TryGetValue("use_solid", out var solid)) appearance.useSolid = solid == "true";
        HandSurfaceLibrary.Entry hand = null, back = null;
        int index = 2;
        void Commit() {
            if (values.TryGetValue("hand_back_follow", out string follow)) config.SetHandBackAutoFollow(follow == "true");
            if (hand != null) CardBackManager.SelectUploadedHandSurface(hand.id, false, true);
            if (back != null) CardBackManager.SelectUploadedHandSurface(back.id, true, true);
            config.ApplyCard3DAppearance(appearance); CardBackManager.RefreshAfterPreset();
            done?.Invoke();
        }
        void Complete() {
            if (hand == null) { Commit(); return; }
            HandSurfaceLibrary.SaveFaceLayout(hand.id, imported.LayoutConfig.HandLayout, Commit, error);
        }
        void Next() {
            if (index >= ImageKeys.Length) { Complete(); return; }
            int current = index++;
            if (!values.TryGetValue(ImageKeys[current], out string filename)) { Next(); return; }
            if (string.IsNullOrEmpty(filename)) {
                if (current == 2) { appearance.backgroundImage = ""; appearance.backgroundImageCustom = false; }
                if (current == 3) { appearance.backImage = ""; appearance.backImageCustom = false; }
                Next(); return;
            }
            if (!imported.Surfaces.TryGetValue(filename, out byte[] png)) { error?.Invoke("缺少图片: " + filename); return; }
            CardDesignImageLibrary.SaveNew(png, CardDesignImageLibrary.SuggestedName(name, current == 3), current == 3,
                entry => { if (current == 2) { appearance.backgroundImage = entry.path; appearance.backgroundImageCustom = true; }
                    else { appearance.backImage = entry.path; appearance.backImageCustom = true; } Next(); }, error);
        }
        HandSurfaceLibrary.EnsureReady(() => {
            byte[] front = null, rear = null;
            if (values.TryGetValue("hand_background", out string frontName) && !string.IsNullOrEmpty(frontName)) imported.Surfaces.TryGetValue(frontName, out front);
            if (values.TryGetValue("hand_back", out string backName) && !string.IsNullOrEmpty(backName)) imported.Surfaces.TryGetValue(backName, out rear);
            if (front == null && rear == null) { Next(); return; }
            HandSurfaceLibrary.SaveImport(front, rear, HandSurfaceLibrary.SuggestedName(name, false, true),
                (backgroundEntry, backEntry) => { hand = backgroundEntry; back = backEntry; Next(); }, error);
        });
    }
}
