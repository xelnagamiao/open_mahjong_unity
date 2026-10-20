#if UNITY_EDITOR
using System;
using System.Linq;
using System.Reflection;
using TMPro;
using UnityEngine;

/// <summary>使用 MainScene 的真实字体，检查网格基线、单行、边界和结算样式切换。</summary>
public static class SettlementTotalLayoutValidation {
    public static object Run() {
        if (Application.isPlaying) throw new InvalidOperationException("在 Edit Mode 运行总计栏验证。");
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var source = Resources.FindObjectsOfTypeAll<EndResultPanel>().First(p => p.gameObject.scene.IsValid());
        var sourceRoot = (GameObject)typeof(EndResultPanel).GetField("FanCountTotalPanel", flags).GetValue(source);
        var preview = UnityEditor.SceneManagement.EditorSceneManager.NewPreviewScene();
        int checks = 0, cases = 0;
        float maxBaselineDifference = 0;
        void Check(bool condition, string message) {
            if (!condition) throw new Exception("Settlement total layout: " + message);
            checks++;
        }
        try {
            var holder = new GameObject("SettlementTotalValidation", typeof(RectTransform), typeof(Canvas));
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(holder, preview);
            var clone = UnityEngine.Object.Instantiate(sourceRoot, holder.transform, false);
            var root = (RectTransform)clone.transform;
            Vector2 initialPosition = root.anchoredPosition, initialPivot = root.pivot;
            float initialFontSize = clone.transform.Find("TotalFan").GetComponent<TMP_Text>().fontSize;
            var panel = holder.AddComponent<EndResultPanel>();
            typeof(EndResultPanel).GetField("FanCountTotalPanel", flags).SetValue(panel, clone);
            string[] fields = { "TotalFan", "TotalFu", "TotalScore", "TotalLimitDisplay" };
            foreach (string field in fields)
                typeof(EndResultPanel).GetField(field, flags).SetValue(panel, clone.transform.Find(field).GetComponent<TextMeshProUGUI>());
            var show = typeof(EndResultPanel).GetMethod("ShowTotalPanel", flags, null, new[] { typeof(SettlementTotalDisplay) }, null);
            var riichi = RiichiFanText.SettlementTotal(new SettlementTotalQuery {
                Rule = "riichi/standard", HuScore = 5300, Extras = new RiichiEndResultExtras { Han = 1, Fu = 110 },
            });
            var langyong = RiichiFanText.SettlementTotal(new SettlementTotalQuery {
                Rule = "riichi/langyong", HuScore = 48000, Extras = new RiichiEndResultExtras { Han = 13, Fu = 30, LangyongMultiplier = 4 },
            });
            var classical = ClassicalFanText.SettlementTotal(new SettlementTotalQuery { Rule = "classical", HuScore = 300, HuFan = Array.Empty<string>(), BaseFu = 30 });
            var hongque = HongqueFanText.SettlementTotal(new SettlementTotalQuery { Rule = "hongque/v1.6", HuScore = 32, HuFan = Array.Empty<string>(), BaseFu = 8 });
            var breakdown = new GuizhouSettlementDisplay { HandScore = 9, KongScore = 9, ChickenScore = 24 }.TotalDisplay();
            var samples = new[] {
                new SettlementTotalDisplay { FanText = "8番", ScoreText = "48点" },
                new SettlementTotalDisplay { FanText = "8.93625番", ScoreText = "+108分" },
                new SettlementTotalDisplay { FanText = "番值", ScoreText = "8.93625番" },
                riichi, langyong, classical, hongque,
                new SettlementTotalDisplay { FanText = null, ScoreText = "18台 +108点" },
                new SettlementTotalDisplay { FanText = "和牌得分 123456分", ScoreText = "+999999分" },
                breakdown, riichi,
                new SettlementTotalDisplay { FanText = "0番", ScoreText = "0点" },
                new SettlementTotalDisplay { FanText = "9.5番", ScoreText = "" },
            };
            Check(riichi.Layout == SettlementTotalDisplay.LayoutKind.FanFuScore, "日麻顺序来自显示模型");
            Check(classical.Layout == SettlementTotalDisplay.LayoutKind.FuFanScore, "古典保留副、番、点");
            Check(hongque.Layout == SettlementTotalDisplay.LayoutKind.FuFanScore, "红雀保留底、番、点");
            foreach (float width in new[] { 964.76f, 640f, 320f }) {
                for (int repeat = 0; repeat < 3; repeat++) {
                    foreach (SettlementTotalDisplay sample in samples) {
                        holder.SetActive(false);
                        show.Invoke(panel, new object[] { sample });
                        bool details = sample.Layout == SettlementTotalDisplay.LayoutKind.Breakdown;
                        if (!details) {
                            Check(Vector2.Distance(root.anchoredPosition, initialPosition) < .01f && root.pivot == initialPivot,
                                "切回普通样式恢复区域");
                        }
                        root.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, width);
                        holder.SetActive(true);
                        for (int pass = 0; pass < 3; pass++) {
                            UnityEngine.UI.LayoutRebuilder.ForceRebuildLayoutImmediate(root);
                            foreach (var text in clone.GetComponentsInChildren<TMP_Text>()) text.ForceMeshUpdate(true);
                        }
                        var visible = clone.GetComponentsInChildren<TMP_Text>().OrderBy(t => t.transform.GetSiblingIndex()).ToArray();
                        Check(visible.Length > 0, "存在总计文案");
                        var expected = sample.Layout == SettlementTotalDisplay.LayoutKind.FuFanScore
                            ? new[] { sample.FuText, sample.FanText, sample.ScoreText, sample.LimitText }
                            : new[] { sample.FanText, sample.FuText, sample.ScoreText, sample.LimitText };
                        Check(visible.Select(t => t.text).SequenceEqual(expected.Where(s => !string.IsNullOrEmpty(s))), "可见列按语义顺序排列");
                        float? baseline = null, scale = null;
                        float previousRight = float.NegativeInfinity;
                        foreach (TMP_Text label in visible) {
                            Check(label.textInfo.lineCount == 1, label.text + " 始终为单行");
                            Check(!label.enableAutoSizing, "列不独立缩字");
                            if (!details) Check(label.fontSize == initialFontSize, "样式切换无残留字号");
                            var fitter = label.GetComponent<UnityEngine.UI.ContentSizeFitter>();
                            Check(fitter == null || !fitter.enabled, "只有统一组件驱动列尺寸");
                            float currentScale = label.transform.localScale.x;
                            Check(!scale.HasValue || Mathf.Abs(currentScale - scale.Value) < .001f, "所有列一起缩放");
                            scale = currentScale;
                            float y = root.InverseTransformPoint(label.transform.TransformPoint(new Vector3(0, label.textInfo.characterInfo[0].baseLine, 0))).y;
                            if (!details || label.name != "TotalFu") {
                                if (baseline.HasValue) {
                                    float difference = Mathf.Abs(y - baseline.Value);
                                    maxBaselineDifference = Mathf.Max(maxBaselineDifference, difference);
                                    Check(difference < .02f, label.text + " 实际网格基线一致");
                                }
                                baseline = y;
                            }
                            float left = root.InverseTransformPoint(label.rectTransform.TransformPoint(new Vector3(label.rectTransform.rect.xMin, 0, 0))).x;
                            float right = left + label.rectTransform.rect.width * currentScale;
                            if (!details) Check(left >= previousRight - .02f, "列之间没有重叠");
                            previousRight = right;
                            Check(left >= root.rect.xMin - .1f && right <= root.rect.xMax + .1f, "列在可用宽度内");
                            foreach (var character in label.textInfo.characterInfo.Take(label.textInfo.characterCount).Where(c => c.isVisible)) {
                                Vector3 top = root.InverseTransformPoint(label.transform.TransformPoint(character.topRight));
                                Vector3 bottom = root.InverseTransformPoint(label.transform.TransformPoint(character.bottomLeft));
                                Check(top.y <= root.rect.yMax + .1f && bottom.y >= root.rect.yMin - .1f,
                                    label.text + " / " + character.character + " 回退字体不超出垂直边界");
                            }
                        }
                        cases++;
                    }
                }
            }
            return new { passed = true, checks, cases, maxBaselineDifference };
        } finally {
            UnityEditor.SceneManagement.EditorSceneManager.ClosePreviewScene(preview);
        }
    }
}
#endif
