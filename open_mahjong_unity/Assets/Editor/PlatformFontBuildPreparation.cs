using TMPro;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;

public sealed class PlatformFontBuildPreparation : IPreprocessBuildWithReport
{
    public int callbackOrder => 1;

    public void OnPreprocessBuild(BuildReport report)
    {
        // TMP's order-0 atlas reset leaves cached fallback characters in other fonts.
        // The public reset also invalidates those lookups and notifies loaded text.
        foreach (string guid in AssetDatabase.FindAssets("t:TMP_FontAsset"))
        {
            var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(AssetDatabase.GUIDToAssetPath(guid));
            if (!font || font.atlasPopulationMode == AtlasPopulationMode.Static) continue;
            var settings = new SerializedObject(font);
            if (settings.FindProperty("m_ClearDynamicDataOnBuild").boolValue)
                font.ClearFontAssetData(true);
        }
    }
}
