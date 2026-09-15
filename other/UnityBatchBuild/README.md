# Unity 三端独立构建

保存 Unity 场景和资源后，双击 `build-all.bat`。自动同步当前磁盘上的项目（包括未提交修改），然后依次构建 Android APK、WebGL、Windows x64。主编辑器可以保持打开，平台状态不变。同步期间请暂缓保存修改；出现 `Sync complete` 后可以继续编辑，后续修改留给下次构建。

## 输出位置

`outputs.json` 保留创建脚本时主项目 `Library/EditorUserBuildSettings.asset` 中记录的路径：

- Android：仓库下 `open_mahjong_unity/build/buildAndroid/open_mahjong_unity.apk`
- WebGL：仓库下 `open_mahjong_unity/build/buildweb`
- Windows：`C:/Users/Administrator/Desktop/sdk/tools/ContentBuilder/content/Game/open_mahjong_unity.exe`，以及同目录配套文件。不是项目的 `buildSteam`。

构建会直接更新这些位置，和手动 Build 一样；失败可能留下部分输出，应以日志和结果为准。不会自动上传 Steam 或部署网站。WebGL 不包括 Vue 网站构建。更换电脑或调整输出目录时编辑 `outputs.json`，相对路径以仓库根目录为基准。

## 操作

在本目录打开 PowerShell：

```powershell
# 只检查路径和模块，不复制、不构建
.\build-all.ps1 -DryRun
# 只构建某一端
.\build-all.ps1 -Targets WebGL
# 选择两端
.\build-all.ps1 -Targets Android,Windows
# 只同步独立副本
.\build-all.ps1 -SyncOnly
# 自定义 Unity 安装位置（必须匹配项目版本）
.\build-all.ps1 -UnityPath 'D:\Unity\6000.4.7f1\Editor\Unity.exe'
```

## 副本与日志

- 独立副本：仓库下 `.om_workspace/unity-batch-build/Project`。
- 日志与逐端结果：`.om_workspace/unity-batch-build/logs/<时间>/`。
- 仅镜像 `Assets`、`Packages`、`ProjectSettings`，并同步主项目的构建偏好；不复制主项目的整个 Library、Temp、输出目录或 Git。副本保留自己的 Library，首次构建需要完整导入，后续复用缓存。
- 每次同步会删除副本源目录中主项目已删除的文件，请勿在副本中开发。拒绝符号链接/目录联接，避免镜像影响源项目。
- 三端各启动一个 Unity 进程，顺序等待结束；一端失败后继续其余端，最终返回失败状态。锁防止重复运行，同一个副本不能另外在 Unity 中打开。
- 使用 Unity 的 `-activeBuildProfile` / `-build`，直接传入 `profiles.json` 指定的自定义配置资产：Android 对应 `Assets/Settings/Build Profiles/Android™.asset`，WebGL 对应 `Web.asset`，Windows 对应 `Windows.asset`。不生成替代配置，不改写配置内容，不通过命令行覆盖平台或子目标。每次运行自动同步最新配置及其引用资源，并在日志目录 `profiles.json` 记录路径和 SHA256。修改配置选项后保存即可；仅重命名、移动或更换配置文件时需要更新路径映射。输出路径仍由 `outputs.json` 指定。Android 若改为 AAB 或导出 Gradle 工程，脚本会提示与 APK 输出不符。
- 将主项目已解析的 `Library/PackageCache` 实际复制到副本，减少重复下载解包和 Windows 包缓存重命名失败；不共享目录联接。其他 Library 导入、编译缓存仍由副本独立维护。
- 使用本机现有 Unity 许可、Android SDK/JDK 和签名环境；签名密码若只存在编辑器内存中，需要另行配置，脚本不保存密码。
- Android 构建进程使用副本工作目录下 `java-tmp` 作为 TEMP/TMP 和 Java socket 临时目录，避免 Windows 重定向的 TEMP 导致 Gradle `Unable to establish loopback connection`；Unity 会过滤部分 JVM 环境参数，因此同时设置 TEMP/TMP。仅影响该构建及其子进程，不更改系统环境变量。除退出码外还检查 Unity 日志中的失败结果以及产物存在性；增量构建可能复用未变化的启动程序，因此不要求 exe 时间戳更新。

## 实际验证（2026-09-15，Unity 6000.4.7f1）

当前自定义配置版本：Android、WebGL 使用原始自定义配置构建成功，日志 `20260915-070900-825`（78 秒、195 秒）。同批 Windows 的 Unity 构建成功，但旧时间戳检查误报；修正增量构建结果判定后，Windows 用 `Windows.asset` 再次完整复验成功，日志 `20260915-071645-598`（57 秒）。配置资产按原样同步，路径和校验值保存在每批日志的 `profiles.json`。三端输出均为上述原目录。

以下是此前平台默认配置版本的历史记录，不代表当前配置选择：

- WebGL、Windows：真实构建成功，日志目录 `20260915-061756-862`，产物已更新到原目录。该批次 Android 失败在 Gradle 本机通信，随后修复。
- Android：修复 TEMP/TMP 后，通过脚本完整复验成功，日志目录 `20260915-070325-970`，APK 已更新到原目录；复用缓存后耗时 54 秒。
- 以上为构建验证，未启动各端应用进行运行功能测试。

官方依据：https://docs.unity3d.com/6000.4/Documentation/Manual/build-command-line.html
