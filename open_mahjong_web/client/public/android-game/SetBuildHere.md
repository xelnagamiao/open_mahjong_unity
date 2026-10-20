将 Android 测试包放在本目录（`client/public/android-game/`），构建后会复制到 `/android-game/`。

文件名需与前端配置一致：`open_mahjong_unity.apk`

下载页：`/mobile-download`

应用内更新还需上传 APK 构建时在其旁边自动生成的 `android-update.json`。
必须上传同一次构建的两份文件；仅更新平台或仅上传其中一份时，应用会提示安装包尚未就绪。
详见 `open_mahjong_web/docs/android-update.md`；生产可用 `ANDROID_UPDATE_DIR` 指定独立发布目录。
