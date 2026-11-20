#if UNITY_EDITOR
using TMPro;
using UnityEngine;

public partial class SceneConfigPanel
{
    public void BakeCardPackTransfers()
    {
        var tabs = GetComponentInChildren<CardDesignTabs>(true);
        var exports = new UnityEngine.UI.Button[2];
        var downloads = new UnityEngine.UI.Button[2];
        string[] paths = { "FaceDesignPanel/Header", "Card3DDesignPanel" };
        for (int i = 0; i < paths.Length; i++) {
            var root = transform.Find(paths[i]);
            if (root == null) throw new System.InvalidOperationException("缺少卡牌设计标题栏");
            if (i == 1) foreach (string name in new[] { "ExportPackButton", "DownloadPackButton" }) {
                var previous = root.Find("CardPresetHeader/" + name);
                if (previous != null) previous.SetParent(root, false);
            }
            exports[i] = CreateResetButton(root, "ExportPackButton", "导出牌包", null);
            downloads[i] = CreateResetButton(root, "DownloadPackButton", "下载牌包", null);
            var buttons = new[] { downloads[i], exports[i] };
            for (int j = 0; j < buttons.Length; j++) {
                var rect = (RectTransform)buttons[j].transform;
                rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0, 1);
                rect.anchoredPosition = new Vector2(1004 + j * 172, -16); rect.sizeDelta = new Vector2(156, 44);
                var label = buttons[j].GetComponentInChildren<TMP_Text>(true);
                label.fontStyle = FontStyles.Normal; label.fontWeight = FontWeight.Regular; label.richText = false;
                SceneConfigUi.ConfigureButtonFeedback(buttons[j], SceneConfigUi.CardHeaderAction);
            }
        }
        var serialized = new UnityEditor.SerializedObject(tabs);
        var exportArray = serialized.FindProperty("exportPackButtons"); exportArray.arraySize = exports.Length;
        var downloadArray = serialized.FindProperty("downloadPackButtons"); downloadArray.arraySize = downloads.Length;
        for (int i = 0; i < exports.Length; i++) {
            exportArray.GetArrayElementAtIndex(i).objectReferenceValue = exports[i];
            downloadArray.GetArrayElementAtIndex(i).objectReferenceValue = downloads[i];
        }
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }
}
#endif
