using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(PlatformBaselineRow))]
public sealed class PlatformBaselineRowEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();
        var row = (PlatformBaselineRow)target;
        EditorGUILayout.LabelField("本行子级文字");
        using (new EditorGUI.DisabledScope(true))
        {
            foreach (var member in row.GetComponentsInChildren<PlatformTextBaseline>(true))
                if (member.row == row && member.mode == PlatformTextBaseline.LayoutMode.Row)
                    EditorGUILayout.ObjectField(member.name, member, typeof(PlatformTextBaseline), true);
        }
    }

    void OnSceneGUI()
    {
        var row = (PlatformBaselineRow)target;
        var anchor = row.Anchor;
        if (!anchor) return;

        float y = row.BaselineY;
        var left = anchor.TransformPoint(new Vector3(anchor.rect.xMin, y));
        var right = anchor.TransformPoint(new Vector3(anchor.rect.xMax, y));
        using (new Handles.DrawingScope(new Color(0.15f, 0.85f, 0.95f)))
        {
            Handles.DrawAAPolyLine(2, left, right);
            Handles.Label(right, string.IsNullOrEmpty(row.rowName) ? row.name : row.rowName);
            EditorGUI.BeginChangeCheck();
            var next = Handles.Slider(left, anchor.TransformVector(Vector3.up).normalized,
                HandleUtility.GetHandleSize(left) * 0.06f, Handles.DotHandleCap, 0f);
            if (!EditorGUI.EndChangeCheck()) return;
            serializedObject.Update();
            serializedObject.FindProperty("verticalOffset").floatValue += anchor.InverseTransformPoint(next).y - y;
            serializedObject.ApplyModifiedProperties();
        }
    }
}
