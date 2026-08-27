using System;
using System.IO;
using UnityEngine;

/// <summary>
/// 原生端选文件：安卓/iOS 走系统文件选择器（SAF / UIDocumentPicker），
/// 读出字节后与桌面一样写入 persistentDataPath。WebGL 仍走 IndexedDB，不经过这里。
/// </summary>
public static class LocalAssetPick {
    public static string[] ZipFileTypes {
        get {
#if UNITY_IOS && !UNITY_EDITOR
            return new[] {
                "public.zip-archive",
                NativeFilePicker.ConvertExtensionToFileType("zip"),
            };
#else
            return new[] {
                "application/zip",
                "application/x-zip-compressed",
                NativeFilePicker.ConvertExtensionToFileType("zip"),
            };
#endif
        }
    }

    public static string[] ImageAndZipFileTypes {
        get {
#if UNITY_IOS && !UNITY_EDITOR
            return new[] {
                "public.image",
                "public.zip-archive",
                NativeFilePicker.ConvertExtensionToFileType("zip"),
            };
#else
            return new[] {
                "image/*",
                "application/zip",
                "application/x-zip-compressed",
                NativeFilePicker.ConvertExtensionToFileType("zip"),
            };
#endif
        }
    }

    public static string[] ImageFileTypes {
        get {
#if UNITY_IOS && !UNITY_EDITOR
            return new[] { "public.image" };
#else
            return new[] { "image/*" };
#endif
        }
    }

    public static void ReadFile(string[] fileTypes, Action<byte[], string> onPicked, Action<string> onError) {
        if (onPicked == null) {
            return;
        }
#if (UNITY_ANDROID || UNITY_IOS) && !UNITY_EDITOR
        try {
            NativeFilePicker.PickFile(path => {
                if (string.IsNullOrEmpty(path)) {
                    return;
                }
                try {
                    onPicked(File.ReadAllBytes(path), Path.GetFileName(path));
                }
                catch (Exception e) {
                    onError?.Invoke("读取文件失败: " + e.Message);
                }
            }, fileTypes);
        }
        catch (Exception e) {
            onError?.Invoke("无法打开文件选择: " + e.Message);
        }
#else
        onError?.Invoke("当前平台请使用本地文件选择");
#endif
    }

    public static void ReadPath(string[] fileTypes, Action<string> onPicked, Action<string> onError) {
        if (onPicked == null) {
            return;
        }
#if (UNITY_ANDROID || UNITY_IOS) && !UNITY_EDITOR
        try {
            NativeFilePicker.PickFile(path => {
                if (string.IsNullOrEmpty(path)) {
                    return;
                }
                onPicked(path);
            }, fileTypes);
        }
        catch (Exception e) {
            onError?.Invoke("无法打开文件选择: " + e.Message);
        }
#else
        onError?.Invoke("当前平台请使用本地文件选择");
#endif
    }
}
