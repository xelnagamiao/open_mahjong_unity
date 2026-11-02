using System;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;

/// <summary>Rebuildable, scoped HUD font assets; never changes shared fonts.</summary>
public static class TableHudTypographyBaker {
    private const string Folder = "Assets/Resources/image/UI/TableHud/Fonts";
    [MenuItem("Tools/Mahjong/Table HUD/Prepare Sharp Fonts")]
    public static void Prepare() {
        if (!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder("Assets/Resources/image/UI/TableHud", "Fonts");
        Create("ControlsMedium", PlatformFontAutomation.FullSource("Medium"));
        Create("ControlsBold", PlatformFontAutomation.FullSource("Bold"));
        AssetDatabase.SaveAssets();
    }
    private static void Create(string name, string sourcePath) {
        string path = Folder + "/" + name + ".asset";
        var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(path);
        bool created = !font;
        var source = AssetDatabase.LoadAssetAtPath<Font>(sourcePath);
        if (!source) throw new InvalidOperationException("Missing project font: " + sourcePath);
        if (created) font = TMP_FontAsset.CreateFontAsset(source, 72, 8, GlyphRenderMode.SDF16, 2048, 2048, AtlasPopulationMode.Dynamic, false);
        font.atlasPopulationMode = AtlasPopulationMode.Dynamic;
        // Static assets omit their runtime source reference. Restore it explicitly
        // for repeatable headless baking after an Editor domain reload.
        var serializedFont = new SerializedObject(font);
        serializedFont.FindProperty("m_SourceFontFile").objectReferenceValue = source;
        serializedFont.FindProperty("m_SourceFontFileGUID").stringValue = AssetDatabase.AssetPathToGUID(sourcePath);
        serializedFont.ApplyModifiedPropertiesWithoutUndo();
        font.name = name;
        // TMP caches fallback characters in this font's runtime lookup. Rebuild
        // from its serialized character table before deciding what still needs
        // baking, otherwise TryAddCharacters silently skips cached fallbacks.
        font.ReadFontAssetDefinition();
        string labels = "牌山东南西北一二三四五六七八九十百零局本场第巡目选择小暂无可选内容上下一步自动播放暂停继续大厅退出规则计分结束对战投票重开中断返回游戏立直麻将标准国标初级中级高级王座友人自定义场次未标注·／「」〈〉【】（）隐藏恢复界面方案仅切换配色尺寸和位置保持一致晶蓝原图青瓷紫石墨琥珀夜雾铜棕实景更多收起示例当前记录合计比分局制切视下拉回到牌桌折箭头示例牌局中途退出确认";
        labels += "风風東谱工具帮助按钮取消托管开始完成速率倍倒序隐藏显示历史详情设置重置滚轮鼠标左右键巡目背景玻璃冰蓝瓷白细描边";
        labels += "提示开关复现等待对室赛务闭室非同步副差分比赛结束";
        labels += "直播观战模式牌谱阅览模式回到直播直播中回放癞皮条筒饼万金宝花指示红蓝队杠补牌打出过点击查看随机种子福州厦门泉漳武汉";
        var hudLabels = UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects()
            .SelectMany(root => root.GetComponentsInChildren<PlatformFontBinding>(true))
            .Where(binding => binding.authoredFont && (binding.authoredFont.name == "ControlsMedium" || binding.authoredFont.name == "ControlsBold"));
        foreach (var binding in hudLabels) {
            var label = binding.GetComponent<TMP_Text>();
            if (!label) continue;
            var localized = label.GetComponent<LocalizedPlatformLabel>();
            string text = localized ? localized.key : label.text;
            foreach (AppLanguage language in Enum.GetValues(typeof(AppLanguage))) labels += PlatformText.Get(text, language);
        }
        foreach (var title in RuleNameDictionary.WholeName.Values.Concat(RuleNameDictionary.ShortName.Values))
            foreach (AppLanguage language in Enum.GetValues(typeof(AppLanguage))) labels += PlatformText.Get(title, language);
        foreach (var title in new[] { "直播观战模式", "牌谱阅览模式", "回到直播", "初级场", "中级场", "高级场", "东南战", "东风战" })
            foreach (AppLanguage language in Enum.GetValues(typeof(AppLanguage))) labels += PlatformText.Get(title, language);
        labels += new string(Enumerable.Range(32, 95).Select(x => (char)x).ToArray());
        labels = new string(labels.Distinct().ToArray());
        try {
            var baked = font.characterTable.Select(c => c.unicode).ToHashSet();
            string pending = new string(labels.Where(c => !baked.Contains(c)).ToArray());
            // TMP can return false when all requested characters already exist
            // or share existing glyphs. The persisted character table is the
            // authoritative result, not that return value or the fallback cache.
            if (pending.Length > 0) font.TryAddCharacters(pending, out _);
            baked = font.characterTable.Select(c => c.unicode).ToHashSet();
            if (labels.Any(c => !baked.Contains(c)))
                throw new InvalidOperationException("HUD font contains unbaked fallback characters: " + new string(labels.Where(c => !baked.Contains(c)).ToArray()));
        } finally { font.atlasPopulationMode = AtlasPopulationMode.Static; }
        PlatformTypographyBaker.EnsureProfile();
        FontFallbackRepairBaker.RepairFont(font);
        font.normalStyle = 0; font.boldStyle = 0;
        font.material.SetFloat("_FaceDilate", 0); font.material.SetFloat("_OutlineWidth", 0);
        font.material.SetFloat("_WeightNormal", 0); font.material.SetFloat("_WeightBold", 0);
        if (created) {
            AssetDatabase.CreateAsset(font, path);
            AssetDatabase.AddObjectToAsset(font.material, font);
            foreach (var texture in font.atlasTextures) { texture.filterMode = FilterMode.Bilinear; AssetDatabase.AddObjectToAsset(texture, font); }
        }
        EditorUtility.SetDirty(font);
        foreach (var texture in font.atlasTextures) EditorUtility.SetDirty(texture);
    }
}
