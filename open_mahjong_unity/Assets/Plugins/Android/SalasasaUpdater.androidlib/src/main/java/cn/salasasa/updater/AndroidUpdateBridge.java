package cn.salasasa.updater;

import android.content.Context;
import android.content.Intent;
import android.content.pm.PackageInfo;
import android.content.pm.PackageManager;
import android.net.Uri;
import android.os.Build;
import android.provider.Settings;
import java.io.File;
import java.io.IOException;

/** Self-update bridge. Android's installer remains responsible for signature verification. */
public final class AndroidUpdateBridge {
    private AndroidUpdateBridge() {}

    public static long installedVersionCode(Context context) throws PackageManager.NameNotFoundException {
        return versionCode(context.getPackageManager().getPackageInfo(context.getPackageName(), 0));
    }

    private static long versionCode(PackageInfo info) {
        return Build.VERSION.SDK_INT >= 28 ? info.getLongVersionCode() : info.versionCode;
    }

    public static String downloadDirectory(Context context) throws IOException {
        File directory = new File(context.getCacheDir(), "app-update");
        if (!directory.isDirectory() && !directory.mkdirs()) throw new IOException("Cannot create update cache");
        return directory.getCanonicalPath();
    }

    static File apkFile(Context context) throws IOException {
        return new File(downloadDirectory(context), "update.apk");
    }

    public static boolean canInstall(Context context) {
        return Build.VERSION.SDK_INT < 26 || context.getPackageManager().canRequestPackageInstalls();
    }

    public static void openPermissionSettings(Context context) {
        Intent intent;
        if (Build.VERSION.SDK_INT >= 26) {
            intent = new Intent(Settings.ACTION_MANAGE_UNKNOWN_APP_SOURCES, Uri.parse("package:" + context.getPackageName()));
        } else {
            intent = new Intent(Settings.ACTION_SECURITY_SETTINGS);
        }
        intent.addFlags(Intent.FLAG_ACTIVITY_NEW_TASK);
        context.startActivity(intent);
    }

    public static void install(Context context, long expectedVersionCode) throws Exception {
        if (!canInstall(context)) throw new SecurityException("Installation permission required");
        File file = apkFile(context);
        PackageInfo info = context.getPackageManager().getPackageArchiveInfo(file.getAbsolutePath(), 0);
        if (info == null || !context.getPackageName().equals(info.packageName)
                || versionCode(info) != expectedVersionCode || versionCode(info) <= installedVersionCode(context)) {
            throw new SecurityException("APK package or version does not match the update");
        }
        Uri uri = new Uri.Builder().scheme("content").authority(context.getPackageName() + ".updates")
                .appendPath("update.apk").build();
        Intent intent = new Intent(Intent.ACTION_VIEW);
        intent.setDataAndType(uri, "application/vnd.android.package-archive");
        intent.addFlags(Intent.FLAG_GRANT_READ_URI_PERMISSION | Intent.FLAG_ACTIVITY_NEW_TASK);
        context.startActivity(intent);
    }
}
