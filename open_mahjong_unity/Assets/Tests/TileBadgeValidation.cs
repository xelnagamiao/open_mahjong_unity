#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Exercise physical face binding, pooling, eligibility and authored UI references.</summary>
public static class TileBadgeValidation {
    public static object Run() {
        if (Application.isPlaying) throw new InvalidOperationException("Run in Edit Mode");
        var checks = new List<string>();
        var failures = new List<string>();
        void Check(bool ok, string name) { checks.Add(name); if (!ok) failures.Add(name); }
        void Near(float actual, float expected, string name) => Check(Mathf.Abs(actual - expected) < .001f, name);
        foreach (var pair in new Dictionary<int,string> {{11,"1"},{29,"9"},{39,"9"},{105,"5"},{205,"5"},{305,"5"},
            {41,"E"},{42,"S"},{43,"W"},{44,"N"},{45,"C"},{46,"P"},{47,"F"},{51,"1"},{58,"4"}})
            Check(TileCornerLabel.Caption(pair.Key) == pair.Value, "physical tile " + pair.Key);
        foreach (int tile in new[] { -1, 0, 10, 20, 40, 1001 })
            Check(TileCornerLabel.Caption(tile) == null, "hidden/custom face has no false label " + tile);
        foreach (string tier in new[] { "intermediate", "advanced", "mcrpl" }) {
            Check(TileCornerLabel.IsRestricted("match", tier, null), "ranked tier " + tier);
            Check(TileCornerLabel.IsRestricted("match", null, tier + "_quanzhuang"), "legacy queue " + tier);
            Check(!TileCornerLabel.IsRestricted("custom", tier, tier + "_quanzhuang"), "custom room ignores player tier " + tier);
        }
        Check(!TileCornerLabel.IsRestricted("match", "beginner", "beginner_quanzhuang"), "beginner labels available");
        Check(!TileCornerLabel.IsRestricted("match", null, "elo_sanma"), "ordinary matching available");
        Check(ConfigManager.ShouldOfferTileLabels(AppLanguage.English, false, true, "menu"), "first English login offers choice");
        Check(ConfigManager.ShouldOfferTileLabels(AppLanguage.English, false, true, "game"), "first English login may resume directly into game");
        Check(!ConfigManager.ShouldOfferTileLabels(AppLanguage.English, false, true, "login"), "login language switch defers choice");
        Check(!ConfigManager.ShouldOfferTileLabels(AppLanguage.English, false, false, "menu"), "authentication required");
        Check(!ConfigManager.ShouldOfferTileLabels(AppLanguage.English, true, true, "menu"), "confirmed choice not repeated");
        Check(!ConfigManager.ShouldOfferTileLabels(AppLanguage.TraditionalChinese, false, true, "menu"), "Chinese does not trigger onboarding");
        var originalRule = RuleRegistry.Current;
        var current = typeof(RuleRegistry).GetProperty("Current");
        var scene = EditorSceneManager.NewPreviewScene();
        var root = new GameObject("Tile badge regression", typeof(RectTransform));
        root.SetActive(false);
        UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(root, scene);
        current.SetValue(null, new RuleManifest { RuleId = "badge-validation", TileBadgeText = t => t == 11 ? "金" : null });
        try {
            foreach (bool hand in new[] { false, true }) {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(hand ? "Assets/Prefabs/GameScene/TileCard.prefab" : "Assets/Prefabs/GameScene/StaticTileCard.prefab");
                var card = UnityEngine.Object.Instantiate(prefab, root.transform);
                foreach (float width in new[] { 28f, 54f, 97.3438f, 150f }) {
                    ((RectTransform)card.transform).sizeDelta = new Vector2(width, width * 1.4f);
                    if (hand) { card.GetComponent<TileCard>().tileId = 11; card.GetComponent<TileCard>().RefreshVisual(); }
                    else card.GetComponent<StaticCard>().SetTileOnlyImage(11);
                    TileCornerLabel.Apply(card.transform, 11, true);
                    var body = hand ? card.GetComponent<TileCard>().BadgeBody : card.GetComponent<StaticCard>().BadgeBody;
                    var overlay = TileFaceOverlay.Get(card.transform);
                    var rule = overlay.Find("RuleTileBadge").GetComponent<TMP_Text>();
                    var helper = overlay.Find("TileCornerLabel").GetComponent<TMP_Text>();
                    Rect drawn = body.GetPixelAdjustedRect();
                    Vector2 fitted = body.sprite.rect.size * Mathf.Min(drawn.width / body.sprite.rect.width, drawn.height / body.sprite.rect.height);
                    float physicalWidth = fitted.x * body.rectTransform.localScale.x;
                    string prefix = (hand ? "hand " : "RoundPanel ") + width;
                    Near(overlay.rect.width * overlay.localScale.x, physicalWidth, prefix + " actual body width");
                    Near(rule.fontSize * overlay.localScale.x / physicalWidth, .25f, prefix + " constant badge ratio");
                    Near(helper.fontSize * overlay.localScale.x / physicalWidth, .26f, prefix + " constant helper ratio");
                    Check(rule.fontStyle == FontStyles.Normal && helper.fontStyle == FontStyles.Normal, prefix + " normal font weight");
                    Check(!PlatformTypography.HeavyFace(rule.font) && PlatformTypography.HeavyFace(helper.font), prefix + " regular Chinese and distinct bold Latin typefaces");
                    Check(helper.rectTransform.anchorMax == Vector2.one && rule.rectTransform.anchorMax == new Vector2(0,1), prefix + " helper right, gold left");
                    TileCornerLabel.Apply(card.transform, 11, false);
                    Check(rule.rectTransform.anchorMax == Vector2.one, prefix + " disabling helpers restores gold to right");
                    TileCornerLabel.Apply(card.transform, 11, true);
                    Vector3 relative = body.transform.InverseTransformPoint(rule.transform.position);
                    body.rectTransform.localPosition += new Vector3(8, 50, 0);
                    card.GetComponent<TileFaceOverlay>().Sync();
                    Check(Vector3.Distance(relative, body.transform.InverseTransformPoint(rule.transform.position)) < .001f, prefix + " follows hover offset");
                    card.transform.localScale = new Vector3(.7f, 1.2f, 1);
                    card.GetComponent<TileFaceOverlay>().Sync();
                    Check(Vector3.Distance(relative, body.transform.InverseTransformPoint(rule.transform.position)) < .001f, prefix + " inherits parent scaling");
                    card.transform.localScale = Vector3.one;
                    TileCornerLabel.Apply(card.transform, 0, true);
                    RuleTileBadge.Apply(card.transform, 0);
                    Check(!helper.gameObject.activeSelf && !rule.gameObject.activeSelf, prefix + " pooled back clears both labels");
                }
                UnityEngine.Object.DestroyImmediate(card);
            }
            var mesh = GameObject.CreatePrimitive(PrimitiveType.Cube);
            mesh.transform.SetParent(root.transform, false);
            var face = mesh.GetComponent<Renderer>();
            face.localBounds = new Bounds(Vector3.zero, new Vector3(1, 1.4f, .2f));
            RuleTileBadge3D.Apply(face, 11, true);
            var marker = face.GetComponentInChildren<TextMeshPro>(true);
            float scale = marker.transform.localScale.x;
            face.localBounds = new Bounds(Vector3.zero, new Vector3(.5f, .7f, .1f));
            RuleTileBadge3D.Apply(face, 11, true);
            Near(marker.transform.localScale.x / scale, .5f, "3D unchanged caption follows resized mesh");
            RuleTileBadge3D.Apply(face, 11, false);
            Check(!marker.gameObject.activeSelf, "concealed 3D face hides rule label");
            RuleTileBadge3D.Apply(face, 12, true);
            Check(!marker.gameObject.activeSelf, "reused 3D face clears former gold badge");
        } finally {
            current.SetValue(null, originalRule);
            UnityEngine.Object.DestroyImmediate(root);
            EditorSceneManager.ClosePreviewScene(scene);
        }
        var settings = Resources.FindObjectsOfTypeAll<Salasasa.Home.SettingsStyleCollection>().Single(s => s.gameObject.scene.IsValid());
        foreach (var page in settings.bakedPages.Where(p => p.category == 2)) {
            var row = page.controls.Single(f => f.key == "TileCornerLabelsRow");
            Check(row.root == row.label.transform.parent && row.options.All(b => b.transform.IsChildOf(row.root)), "theme " + page.variant + " owns its row references");
            Check(row.root.GetComponent<Salasasa.Home.SettingsHintTarget>().description == TileCornerLabel.Restriction, "theme " + page.variant + " restriction note");
            Check(-row.root.anchoredPosition.y + row.root.rect.height <= page.sections[1].root.rect.height + .01f, "theme " + page.variant + " row within section");
        }
        var modal = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Resources/TileCornerLabelChoice.prefab");
        Check(modal && modal.GetComponent<TileCornerLabelChoice>() && modal.GetComponent<GraphicRaycaster>(), "modal prefab is interactive");
        Check(modal.GetComponentsInChildren<Button>(true).Length == 3, "two choices and a close button");
        Check(modal.GetComponentsInChildren<TMP_Text>(true).Any(l => l.text == TileCornerLabel.EnglishRestriction), "English restriction visible in modal");
        Check(modal.GetComponentsInChildren<LocalizedPlatformLabel>(true).Length == 0, "English-only choice has no localized bindings");
        Check(!modal.transform.Find("Panel/Legend") && !modal.transform.Find("Panel/SettingsHint") && !modal.transform.Find("Panel/Explanation"), "choice contains no explanatory footnotes or legend");
        Check(((RectTransform)modal.transform.Find("Panel/WithoutLabels")).anchoredPosition.x < ((RectTransform)modal.transform.Find("Panel/WithLabels")).anchoredPosition.x, "original tiles left, labeled tiles right");
        foreach (int tile in new[] { 11, 19, 45, 51, 54, 105, 205, 305, 2011, 2015, 2045 })
            Check(TileCornerLabel.Ink(tile) == TileCornerLabel.RedInk, "red index " + tile);
        foreach (int tile in new[] { 21, 31, 41, 44, 46, 47, 55, 58, 2021, 2041 })
            Check(TileCornerLabel.Ink(tile) == TileCornerLabel.BlackInk, "black index " + tile);
        foreach (string source in new[] { "排位场", "赛事场", "场次未标注", "自由对局", "初级场", "中级场", "高级场", "友人房", "Elo 匹配" })
            Check(!PlatformText.HasChinese(PlatformText.Get(source, AppLanguage.English)), "translated venue " + source);
        return new { success = failures.Count == 0, count = checks.Count, checks, failures };
    }
}
#endif
