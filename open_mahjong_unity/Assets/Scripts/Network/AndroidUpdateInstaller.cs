using System;
using UnityEngine;

internal interface IAndroidUpdateInstaller {
    long InstalledVersionCode { get; }
    string PackageName { get; }
    string DownloadDirectory { get; }
    bool CanInstall { get; }
    void OpenPermissionSettings();
    void Install(long expectedVersionCode);
}

internal sealed class AndroidUpdateInstaller : IAndroidUpdateInstaller {
    private const string BridgeClass = "cn.salasasa.updater.AndroidUpdateBridge";

    private static T Call<T>(string method) {
#if UNITY_ANDROID && !UNITY_EDITOR
        using (var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
        using (var activity = player.GetStatic<AndroidJavaObject>("currentActivity"))
        using (var bridge = new AndroidJavaClass(BridgeClass)) {
            return bridge.CallStatic<T>(method, activity);
        }
#else
        throw new PlatformNotSupportedException("应用内安装仅支持安卓客户端。");
#endif
    }

    public long InstalledVersionCode => Call<long>("installedVersionCode");
    public string PackageName => Application.identifier;
    public string DownloadDirectory => Call<string>("downloadDirectory");
    public bool CanInstall => Call<bool>("canInstall");

    public void OpenPermissionSettings() => Run("openPermissionSettings", 0);
    public void Install(long expectedVersionCode) => Run("install", expectedVersionCode);

    private static void Run(string method, long version) {
#if UNITY_ANDROID && !UNITY_EDITOR
        using (var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
        using (var activity = player.GetStatic<AndroidJavaObject>("currentActivity"))
        using (var bridge = new AndroidJavaClass(BridgeClass)) {
            if (method == "install") bridge.CallStatic(method, activity, version);
            else bridge.CallStatic(method, activity);
        }
#else
        throw new PlatformNotSupportedException("应用内安装仅支持安卓客户端。");
#endif
    }
}
