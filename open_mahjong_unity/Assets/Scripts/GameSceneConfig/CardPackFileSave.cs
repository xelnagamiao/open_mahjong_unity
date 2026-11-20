using System;
using System.IO;
using System.Runtime.InteropServices;
using UnityEngine;

public sealed class CardPackFileSave : MonoBehaviour
{
#if UNITY_WEBGL && !UNITY_EDITOR
    [DllImport("__Internal")] static extern void DownloadCardPack(string gameObject, string method, string filename, byte[] bytes, int length);
#endif
    public static void Save(byte[] bytes, string filename)
    {
        foreach (char invalid in Path.GetInvalidFileNameChars()) filename = filename.Replace(invalid, '_');
        try {
#if UNITY_WEBGL && !UNITY_EDITOR
            var request = new GameObject("CardPackExport_" + Guid.NewGuid().ToString("N")).AddComponent<CardPackFileSave>();
            DownloadCardPack(request.name, nameof(OnSaved), filename, bytes, bytes.Length);
#elif (UNITY_ANDROID || UNITY_IOS) && !UNITY_EDITOR
            string path = Path.Combine(Application.temporaryCachePath, Guid.NewGuid().ToString("N") + "-" + filename);
            File.WriteAllBytes(path, bytes);
            NativeFilePicker.ExportFile(path, success => {
                if (success) SceneConfigUi.ShowTip("牌包已导出");
                if (File.Exists(path)) File.Delete(path);
            });
#else
            string path = SFB.StandaloneFileBrowser.SaveFilePanel("导出牌包", "", Path.GetFileNameWithoutExtension(filename), "zip");
            if (string.IsNullOrEmpty(path)) return;
            File.WriteAllBytes(path, bytes);
            SceneConfigUi.ShowTip("牌包已导出");
#endif
        } catch (Exception e) { SceneConfigUi.ShowTip("导出失败：" + e.Message); }
    }
    public void OnSaved(string result) { SceneConfigUi.ShowTip("牌包已导出"); Destroy(gameObject); }
}
