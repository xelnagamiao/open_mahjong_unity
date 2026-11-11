此目录用于 iOS 发布文件。下载页为 `/ios-download`；通用 `/mobile-download` 入口会将 iPhone/iPad 浏览器引导到该页。显式 `?platform=android` 可查看 Android 页。

Node 优先读取 `IOS_UPDATE_DIR` 指定的目录；未设置时，开发环境读取 `client/public/ios-game`，生产环境读取 `client/dist/ios-game`。生产建议使用独立持久目录，避免重建 Web 时覆盖发布文件。

安装版本元数据名为 `ios-update.json`（UTF-8，最多 32 KiB）。共同字段为：

- `schemaVersion`: `1`；`platform`: `"ios"`。
- `bundleId`: 与实际安装包一致；当前项目为 `com.Salasasa-Studio.open-mahjong-unity`。
- `versionName`: 安装包的三段数字应用版本，例如 `0.5.89`；不用于判断游戏是否需要升级。
- `releaseVersion`: 游戏发行版本，与 Unity 的 `ConfigManager.releaseVersion` 一致，例如 `33`、`34`；必填正整数。
- `buildNumber`: 可省略，默认取 `releaseVersion`，与当前 iOS 导出默认的 `CFBundleVersion` 一致。未来 TestFlight/App Store 重复提交同一发行版本时，可显式填写该包实际使用的独立正整数构建号；构建号不作为游戏升级条件。
- `channel`: `sideload`、`testflight` 或 `appstore`；`notes` 为可选更新说明。

`sideload` 另需 `file`（本目录 IPA 文件名）、`sizeBytes`（实际字节数）、`sha256`（64 位小写摘要）。服务端校验文件大小和 SHA-256 后才会提供下载，IPA 上限 2 GiB。IPA 下载不等于签名安装；免费个人签名仍需玩家通过电脑侧载、定期刷新。

`testflight` / `appstore` 改用 `installUrl`，分别只接受 `https://testflight.apple.com/join/...` 与 `https://apps.apple.com/.../id...`。

日常游戏发版只维护 Unity 中的 `ConfigManager.releaseVersion`，发布清单使用同一数值。同一发行版本可以重新打包、替换 IPA，并更新对应文件名、大小和摘要；旧 App 不会因此收到游戏升级提示，也不会被强制升级。只有服务端公布更高的 `releaseVersion` 才触发游戏更新，例如从 `33` 升到 `34`。更换 IPA、应用版本或独立构建号都不替代发行号升级；免费签名的到期续签仍需单独处理。

资源更新使用独立的 `ios-content.json` 和 `content-<sha256>.bundle`，由 Unity 的 `Tools/Salasasa/iOS/Build Selected Resource Content` 生成，不能手工把 ZIP/APK 改名为 bundle。生成目录为工作区 `.om_workspace/ios-releases/<contentVersion>/`，工具不会自动上传。

资源包仅支持所接入加载点的图片、音频、JSON/TXT；必须与目标安装包的 Unity 版本、iOS 平台、`playerVersion`、`releaseVersion` 匹配。每一版为完整覆盖集合：前一版改 A、下一版改 B 时，下一版必须同时选入 A 和 B，遗漏的 A 将恢复安装包内原版。当前仅激活一个包，上限 256 MiB、1024 个资源键、清单 32 KiB。程序、场景、预制体和字体变更需要新安装包。

发布时先完整放置新产物，再原子替换元数据文件；不要原地改写正在下载的产物。安装包与资源包可以独立发布。缺文件、校验失败或没有有效元数据时，页面/API 保持“准备中”。

站点须将 `/api/client-updates/ios/*` 代理到 Node，元数据禁止缓存，保留 Range/HEAD；`/ios-download` 需要 SPA 路由回退。实际部署、上传及云构建须另行执行。
