# 安卓应用内更新

安卓客户端在“版本检查失败”时依次显示“应用内更新”“去下载”和“关闭”三个按钮。“应用内更新”检查安装包并显示下载百分比、大小，下载后校验大小和 SHA-256，再由玩家点击“安装”调用安卓系统安装界面。“去下载”保留原来的浏览器下载页面，“关闭”关闭提示。失败可重试，关闭下载窗口取消下载。授权安装后返回应用会继续安装；系统安装仍检查 APK 签名。

## 发布

每次 Unity APK 构建会在 APK 旁生成 `android-update.json`，包含该包的实际构建版本、兼容版本、包名、大小及 SHA-256。AAB 和导出的 Gradle 工程不生成此清单。

1. 保留原包名和签名，每次发布递增 Android Bundle Version Code。`ConfigManager.releaseVersion` 是游戏协议兼容版本，与游戏服 `Config.release_version` 对齐；它和 Android versionCode 分别维护。
2. 将 **APK 和本次构建生成的 android-update.json 一起上传**，不要手工把旧包的版本号改成新版。保留 APK 文件名，清单的 `file` 字段与它对应。
3. 默认开发目录为 `client/public/android-game/`，生产目录为 `client/dist/android-game/`。可设置 Node 环境变量 `ANDROID_UPDATE_DIR` 指向实际发布目录（推荐使用独立持久目录，避免前端重新构建清空包）。设置或更改该环境变量需重启 Node，后续上传新包无需重启。
4. 推荐先上传临时文件，完整上传后原子改名替换 APK，最后替换清单。两份文件先后顺序不一致时接口也会拒绝提供下载，等待它们匹配。

`/mobile-download` 网页每次点击都会以 `cache: no-store` 和新的请求标识读取 `/api/client-updates/android/latest`，服务端验证 APK 后返回实际版本和 `/api/client-updates/android/apk/<sha256>`。网页不写死发行版号，也不回退到可能缓存的同名旧包；下载文件名自动带实际 `versionName` 与 `versionCode`。应用内更新继续使用原来的兼容版本检查和同一哈希下载接口，不放宽降级或版本限制。

### 一次配置，后续自动跟随发布

将 Web 项目根目录的 `nginx-android-download.conf` 在站点 `server {}` 内包含一次（按实际部署调整 Node 端口），执行 `nginx -t` 后平滑重载。旧地址 `/android-game/open_mahjong_unity.apk` 会交给 `/api/client-updates/android/download`，用 `no-store` 的临时跳转解析当前已验证的哈希地址；原始 `android-update.json` 强制重验证。保留现有 `/api` 反向代理，不要回退到 SPA HTML。

后续只需保持原有发布顺序：先原子替换 APK，最后发布该包自动生成的清单；无需修改网页版本、Nginx 配置或重启 Node。使用独立 `ANDROID_UPDATE_DIR` 时由 Node 读取该目录，网页不再依赖 Nginx 静态 APK 的副本。

CDN 必须遵循这些 API 的 `Cache-Control: no-store`，不要给 `/api/client-updates/android/` 设置强制缓存。首次部署此修复时，可以清除 CDN 中旧静态 APK 的缓存；已经保存在用户手机里的旧文件/缓存无法由服务器统一删除，重新打开更新后的下载页会使用全新的哈希地址，避开旧 URL。

## 版本与上传延迟

游戏服的版本不符消息新增可选 `message_info.required_release_version`，旧客户端仍能正常读取原提示。新版客户端只接受协议版本恰好匹配且 Android versionCode 更高的安装包。

接口 `GET /api/client-updates/android?requiredReleaseVersion=31&installedVersionCode=30`：

- `status: available`：实际 APK 已存在，大小和 SHA-256 与清单一致，且符合客户端版本要求；返回版本、包名、校验信息和带哈希的相对下载地址。
- `status: pending`：APK/清单缺失、上传未完成、校验不符、包仍是旧版、包过新或没有可安装的新版本。客户端提示稍后重试，不会下载旧包。
- 下载时若文件已替换，旧哈希地址返回 409；客户端重新检查清单并重试。

服务端按文件状态缓存校验结果，文件或清单变化会重新校验。客户端下载后再次进行 SHA-256 校验；安卓桥接在安装前核对 APK 实际包名和 versionCode，不接受错包或降级。

服务器先更新但 APK 尚未上传时，原来的兼容版本限制继续生效；更新功能不会让不兼容的旧客户端进入对局。客户端比游戏服新时保留等待服务器更新的提示，不进行降级。

首次发布此功能时，玩家仍需通过现有网页安装包含更新器的版本。必须同时部署新的 Python 版本消息和 Node 更新路由；旧游戏服未提供兼容版本信息时，新客户端不会猜测下载版本。

## 验证

在 Web 项目运行 `node --test server/routes/androidUpdate.test.js`，覆盖先更新平台后上传包、上传顺序、截断和损坏文件、重试、旧包/过新包、并发校验和下载地址变化。
网页缓存回归：`node --test client/tests/mobileDownload.test.mjs`。服务端测试还覆盖同一进程中发布下一版后网页清单、临时跳转、下载文件名自动更新，以及旧哈希拒绝和失败不回退旧包。

编辑器可通过 `Tools → 安卓应用内更新 → 重新开始本地预览` 启动或重新打开预览。停止播放后菜单仍可用，会自动进入 Play Mode；首次选择本地预览配置 JSON 后，本次编辑器会话会记住配置，包括脚本重载以后。预览使用本机更新 API 和真实 APK，安卓授权和安装界面仅作模拟。

Unity 的 `AndroidAppUpdateValidation.Run()` 覆盖清单和文件校验；运行时验证入口见 `AndroidAppUpdateValidation`。设备验收需至少包括未知来源授权允许/拒绝、系统安装取消/重试、相同签名覆盖安装、不同签名拒绝、更新后本地设置保留。安装必须离开对局进行。
