#if UNITY_EDITOR
using System.Linq;
using Salasasa.Home;
using UnityEditor;
using UnityEngine;

public sealed class HeaderPresentationWindow : EditorWindow
{
    HeaderPresentationProfile profile;
    SerializedObject serialized;
    Vector2 scroll;
    int selected;
    int previewRevision = -1;
    bool fonts;
    [MenuItem("Tools/Salasasa/Header/设计配置")]
    public static void Open() => GetWindow<HeaderPresentationWindow>("导航栏设计");

    void OnInspectorUpdate()
    {
        if (!profile || previewRevision == profile.revision || EditorApplication.isCompiling || EditorApplication.isUpdating || BuildPipeline.isBuildingPlayer) return;
        Preview(); Repaint();
    }

    void Preview()
    {
        foreach (var controller in Object.FindObjectsByType<HeaderPresentation>(FindObjectsInactive.Include, UnityEngine.FindObjectsSortMode.None))
        {
            if (controller.profile != profile) continue;
            controller.Refresh();
            if (!EditorApplication.isPlaying) controller.navigation.BakeLayout(controller.navigation.Side);
        }
        previewRevision = profile.revision;
        SceneView.RepaintAll();
    }

    void OnGUI()
    {
        if (!profile) profile = AssetDatabase.LoadAssetAtPath<HeaderPresentationProfile>(HeaderPresentationBaker.ProfilePath);
        EditorGUILayout.HelpBox("每个入口的文字与图标独立调整。配置保存在项目资源中；应用到 MainScene 后，场景中也会保存完整显示结果。", MessageType.Info);
        if (!profile)
        {
            if (GUILayout.Button("为 MainScene 建立配置")) HeaderPresentationBaker.Bake();
            return;
        }
        if (serialized == null || serialized.targetObject != profile) serialized = new SerializedObject(profile);
        serialized.Update();
        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("定位配置资源")) { Selection.activeObject = profile; EditorGUIUtility.PingObject(profile); }
            if (GUILayout.Button("保存并应用到 MainScene")) { serialized.ApplyModifiedProperties(); HeaderPresentationBaker.Bake(); }
        }
        if (GUILayout.Button("补充当前翻译用到的字体字符")) HeaderPresentationBaker.RebuildFontMenu();
        scroll = EditorGUILayout.BeginScrollView(scroll);
        var entries = serialized.FindProperty("entries");
        selected = Mathf.Clamp(selected, 0, entries.arraySize - 1);
        selected = EditorGUILayout.Popup("入口", selected, profile.entries.Select(x => x.route + "  ·  " + x.simplifiedChinese).ToArray());
        if (entries.arraySize > 0)
        {
            var entry = entries.GetArrayElementAtIndex(selected);
            void Field(string key, string label) => EditorGUILayout.PropertyField(entry.FindPropertyRelative(key), new GUIContent(label));
            EditorGUILayout.LabelField("文字 / 多语言", EditorStyles.boldLabel);
            using (new EditorGUI.DisabledScope(true)) Field("route", "稳定路由 ID");
            Field("simplifiedChinese", "简体中文"); Field("traditionalChinese", "繁體中文"); Field("english", "English");
            Field("fontSize", "文字字号"); Field("minimumFontSize", "长文字最小字号"); Field("characterSpacing", "字间距");
            Field("captionSize", "文字区域尺寸"); Field("captionOffset", "文字位置"); Field("shadowOffset", "文字阴影偏移");
            Field("textColor", "文字颜色"); Field("selectedTextColor", "选中文字颜色");
            EditorGUILayout.Space(); EditorGUILayout.LabelField("图标", EditorStyles.boldLabel);
            Field("iconSize", "图标尺寸"); Field("iconOffset", "图标位置"); Field("iconShadowOffset", "图标阴影偏移"); Field("iconColor", "图标颜色"); Field("selectedIconColor", "选中图标颜色");
            EditorGUILayout.Space(); EditorGUILayout.LabelField("空间分配", EditorStyles.boldLabel);
            Field("minimumWidth", "入口最小宽度"); Field("maximumWidth", "入口最大宽度"); Field("growForTranslation", "随翻译长度扩宽");
            if (profile.entries[selected].route == "home") Field("useHomePalette", "沿用主菜单调色方案");
        }
        fonts = EditorGUILayout.Foldout(fonts, "全局字体与间距");
        if (fonts) foreach (string key in new[] { "font", "faceMaterial", "outlineMaterial", "shadowMaterial", "labelGap", "utilityGap" }) EditorGUILayout.PropertyField(serialized.FindProperty(key));
        EditorGUILayout.EndScrollView();
        if (serialized.ApplyModifiedProperties())
        {
            profile.Changed();
            Preview();
        }
    }
}
#endif
