using System;
using System.Collections.Generic;

/// <summary>
/// 声明式建房选项表单引擎：根据 RuleManifest.RoomOptionSchema 管理草稿值、默认装配与联动显隐，
/// 替代在 CreatePanel 上为每个麻将规则新增 partial 文件和静态 SerializeField。
/// </summary>
public sealed class RoomOptionFormRenderer {
    public RoomOptionDraft Draft { get; private set; } = new RoomOptionDraft();
    public IReadOnlyList<RoomOptionField> ActiveSchema { get; private set; } = Array.Empty<RoomOptionField>();

    public void BindRule(RuleManifest manifest, string subRule) {
        Draft = new RoomOptionDraft {
            RuleId = manifest?.RuleId ?? "",
            SubRule = subRule ?? manifest?.DefaultSubRule ?? "",
        };
        ActiveSchema = manifest?.RoomOptionSchema ?? Array.Empty<RoomOptionField>();
        if (manifest?.CreateRoomDefaults != null) {
            foreach (var kv in manifest.CreateRoomDefaults) {
                Draft.Values[kv.Key] = kv.Value;
            }
        }
        for (int i = 0; i < ActiveSchema.Count; i++) {
            RoomOptionField field = ActiveSchema[i];
            if (field == null || string.IsNullOrEmpty(field.Key)) continue;
            if (!Draft.Values.ContainsKey(field.Key) && field.DefaultValue != null) {
                Draft.Values[field.Key] = field.DefaultValue;
            }
        }
    }

    public void SetValue(string key, object value) {
        if (string.IsNullOrEmpty(key)) return;
        Draft.Values[key] = value;
    }

    public List<RoomOptionField> EvaluateVisibleFields() {
        var visible = new List<RoomOptionField>(ActiveSchema.Count);
        for (int i = 0; i < ActiveSchema.Count; i++) {
            RoomOptionField field = ActiveSchema[i];
            if (field == null) continue;
            if (field.VisibleWhen == null || field.VisibleWhen(Draft)) {
                visible.Add(field);
            }
        }
        return visible;
    }

    public void ExportVisibleValues(IDictionary<string, object> target) {
        if (target == null) return;
        List<RoomOptionField> visible = EvaluateVisibleFields();
        for (int i = 0; i < visible.Count; i++) {
            RoomOptionField field = visible[i];
            if (string.IsNullOrEmpty(field.Key)) continue;
            if (Draft.Values.TryGetValue(field.Key, out object val)) {
                target[field.Key] = val;
            }
        }
    }
}
