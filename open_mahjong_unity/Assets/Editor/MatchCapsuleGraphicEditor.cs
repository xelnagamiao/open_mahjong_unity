using UnityEditor;
using UnityEditor.UI;
using UnityEngine;

[CustomEditor(typeof(MatchCapsuleGraphic)), CanEditMultipleObjects]
public sealed class MatchCapsuleGraphicEditor : GraphicEditor {
    public override void OnInspectorGUI() {
        base.OnInspectorGUI();
        serializedObject.Update();
        EditorGUILayout.PropertyField(serializedObject.FindProperty("outlineColor"),new GUIContent("描边颜色"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("outlineWidth"),new GUIContent("描边宽度"));
        serializedObject.ApplyModifiedProperties();
    }
}
