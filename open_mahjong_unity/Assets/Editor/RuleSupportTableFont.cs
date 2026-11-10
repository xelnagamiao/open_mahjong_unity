using System;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;

public static class RuleSupportTableFont
{
    public static TMP_FontAsset Prepare(RuleSupportTableBaker.TableData data, bool allowWrite)
    {
        const string fontPath = "Assets/font/思源黑体/SourceHanSansSC-Regular SDF.asset";
        var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(fontPath);
        if (!font) throw new BuildFailedException("缺少规则支持表的思源黑体。" );
        if (allowWrite) FontFallbackRepairBaker.RepairFont(font);
        string characters = "麻将规则支持表关闭" + string.Concat(data.Headers) + string.Concat(data.Rows.SelectMany(row => row));
        if (!font.HasCharacters(characters, out uint[] missing, true, true))
            throw new InvalidOperationException("支持表字体缺少字符：" + string.Join(",", missing));
        return font;
    }
}

public sealed class RuleSupportTableFontBuild : IPreprocessBuildWithReport
{
    public int callbackOrder => -10;
    public void OnPreprocessBuild(BuildReport report) {
        if (!AndroidContentBuild.BuildingBootstrapPlayer)
            RuleSupportTableFont.Prepare(RuleSupportTableBaker.ReadWorkbook(), true);
    }
}
