# 安卓应用内更新

## 安卓代码与资源全量热更新工作流

共享入口 `other/UnityBatchBuild/build-all.bat` 提供 Windows、Android APK、WebGL、AndroidHotUpdate 四个选项，可用逗号多选。Android 自动同时生成 APK 和完整内容 ZIP；同时选择 AndroidHotUpdate 不会重复构建。Windows/WebGL 使用源项目的 Unity 版本，Android 独立副本使用 6000.3.28f1 LTS 和固定 HybridCLR 9.0.3。源工程保持 6000.4.7f1。

游戏服在现有 `release_version` 下增加 `base_version`；Unity 保留 `releaseVersion` 并增加 `base_version`。基础版本不匹配需要 APK，基础版本一致才比较小版本。版本文件、哈希和签名自动生成，不维护第三个业务版本。右下角显示当前游戏代码的 `releaseVersion`。

`ConfigManager` 仍在游戏业务程序集中；构建时自动从它提取版本和地址，结合基础包公钥生成启动器元信息。校验前无需加载可能损坏的业务 DLL，也不用再手工维护一份框架配置。

命令示例（在仓库根目录）：

```powershell
& other/UnityBatchBuild/build-all.ps1 -Targets Android
& other/UnityBatchBuild/build-all.ps1 -Targets AndroidHotUpdate
& other/UnityBatchBuild/build-all.ps1 -Targets Windows,WebGL
```

首次安卓原生构建需要缓存盘至少 24GB 可用空间，后续仅内容包需要至少 8GB；脚本会提前检查。缓存可放到其他盘，例如加 `-WorkRoot C:/.om_workspace/open-mahjong-android`。同一基础版本后续构建必须沿用该 WorkRoot，或完整迁移其中的基线、私钥和安装版本序号；不需要修改 outputs.json 或游戏配置。

APK 沿用 outputs.json 的 Android 输出目录；同目录下的 `hotupdate/<base>/<release>/` 包含 `content-update.json` 和 `content-<sha256>.zip`。包内包括业务 DLL、场景、完整资源及与该 APK 对应的 AOT 元数据。默认 APK 内置同一份 ZIP；单独生成热更新包复用基础包基线，跳过 APK/Gradle 构建。构建检测到原生依赖、启动框架或不兼容的 AOT 接口变化时会要求增加基础版本。

同时生成 APK 和 ZIP 时，完整内容 ZIP 只生成一份，并直接嵌入 APK，复用这轮业务 DLL 和资源导出结果。同一基础版本的后续 APK 构建也复用 AOT 基线，省去第一次的干净元数据导出；APK 自身的原生编译和 Gradle 打包仍需运行。两套 Unity 的 Library、IL2CPP 产物和 AssetBundle 不可互换，Windows/WebGL 不使用安卓的 SDK 或资源适配。

安卓副本关闭引擎代码裁剪，并把完整资源包清单交给 APK 构建，保留启动场景本身未使用的 Unity 内置类型。否则外部主场景可能缺少 AudioListener 等组件。该设置只写入安卓编译副本，不修改源工程的 Windows/WebGL 设置。资源包使用持久构建缓存，由 Unity 检查依赖和类型变化；每份发布 ZIP 仍完整包含代码、资源和场景。

固定的 HybridCLR 6000.3 原生源码还应用一处参数反射兼容修正：热更新 DTO 数组的 `IList<T>.RemoveAt` 等注入方法保留 AOT 元数据，按方法定义而非数组的元素程序集查找参数。脚本校验上游文件指纹，只在安卓副本内应用，并将修正指纹纳入基础包基线；不修改机器上的 Unity 安装。更新上游 SDK 时必须重新核对此修正。

首次必须构建 APK 建立基线。`.om_workspace/unity-batch-build/baselines/` 保存基础包元数据和私钥，`apk-version-code.txt` 保存安装版本序号，迁移构建机器时必须安全备份并恢复；它们不提交 Git、不上传下载服务器。还需保留原 APK 的签名密钥，内容包私钥不能替代 APK 签名。新工作流自动递增安装用 versionCode，源项目设置不随之改变。`-Validation` 只用于 x86_64 模拟器，使用独立包名、本地服务和独立私钥，禁止发布验证产物。

发布顺序：

1. 按原来的方式维护两端发行版号；基础环境不兼容时才增加两端基础版本。构建失败不得发布旧目录中残留的产物，以本轮成功日志和 `android-content-build-result.json` 为准。
2. 将完整 `hotupdate/<base>/<release>/` 上传到现有 `ANDROID_UPDATE_DIR` 下，先上传 ZIP、校验后再原子发布清单。已发布的大小版本目录不可覆盖为另一份内容，旧版本目录保留供正在下载的客户端续传。
3. 发布 APK 时将 APK 和自动生成的 `android-update.json` 配对上传。新基础版本启用前必须准备好匹配的 APK。旧客户端迁移期间建议每次使用 Android 选项同时发布两份产物；旧接口仍要求小版本完全匹配，官网下载入口则始终解析最新可用 APK。
4. 部署新的 Node 路由和 Python `/client-version`，重启对应服务。Nginx 在站点 server 块包含 `nginx-android-download.conf` 一次，核实原有 Node 端口后 `nginx -t` 并平滑重载。清单和目标版本不缓存，哈希 ZIP 可长缓存并支持 Range；CDN 应遵循响应头。
5. 文件就绪后协调游戏服版本生效。先提供文件再更新服务端版本，避免用户被要求下载尚不存在的版本。现有发行版校验仍适用于所有平台；提高 `release_version` 时，Windows 的 Steam 版本和 WebGL 对应版本也按原流程准备并发布。验证用的发行版 33 只在独立副本和本机测试服务生效，源配置仍为 32。

启动器在加载业务 DLL 前检查内容；下载中断支持续传，校验/解包失败保留旧内容。新版首次启动未确认成功而进程退出时，下次启动恢复旧版本并显示恢复界面。基础包内保留重试、恢复随包内容、应用内 APK 安装和官网下载；Android 的安装授权及最终安装由系统处理。回退旧内容不会绕过游戏服的版本检查。

接口：`/api/client-updates/android/version` 返回游戏服目标版本；`/content?baseVersion=1&releaseVersion=32` 判断内容更新；`/framework?installedVersionCode=32` 检查可覆盖安装的基础包；`/content/<base>/<release>/<sha256>` 下载已验证 ZIP。新框架允许 APK 的内置内容较旧，安装后补齐内容；以下旧 APK 协议继续保留。

## 旧 APK 更新入口

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

CDN 必须遵循版本、发布清单和 APK 查询 API 的 `Cache-Control: no-store`，不要对这些元数据接口强制缓存；哈希内容 ZIP 则遵循其 `immutable` 长缓存响应头。首次部署此修复时，可以清除 CDN 中旧静态 APK 的缓存；已经保存在用户手机里的旧文件/缓存无法由服务器统一删除，重新打开更新后的下载页会使用全新的哈希地址，避开旧 URL。

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
