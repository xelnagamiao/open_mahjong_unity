# 项目清理清单 · 2026-09-12

本清单对应当前工作区 `dev-feature-xel`，清理范围以 2026-09-01 起的 Codex 任务为依据，重点核对了场景设置、桌布/边框、中心盘、牌面、3D 模型贴图、自由模式和渲染示例相关任务。

## 已移出项目结构

以下内容不是生产运行时入口，且已有主项目实现、有效素材或源码归档作为替代。由于当前环境禁止永久递归删除，已移动到被 `.gitignore` 忽略的本地隔离区：

`D:/open_mahjong_unity/.om_workspace/cleanup-quarantine-20260912/`

| 原位置 | 处理 | 依据 |
| --- | --- | --- |
| `other/Tablecloth/TableRenderLab` | 移出完整示例工程、Library、构建和日志 | `other/Tablecloth/SeamLibrary/Archive/TableRenderLab_source_20260911.zip` 已完成源文件校验 |
| `other/Tablecloth/SettingsPreviewOptimization_20260909/UnityProject` | 移出隔离验证工程及 Library | 主项目已包含最终 `TableSurfacePreviews`、异步加载和预览生成逻辑 |
| `other/CenterDisplay` | 移出完整历史设计归档：v1/v2/v3/v4 示例场景、旧脚本、浏览页、验证输出和源码包 | 主项目已保留当前中心盘资源与代码；历史归档不再是当前入口 |
| `other/SceneSettings/UnityPanelLab/UnityProject` | 移出示例工程、Library、旧构建 | 当前设置功能已进入主项目 `MainScene.unity` |
| `other/SceneSettings/UnityPanelLab/MainStyleValidationProject` | 移出重复验证工程、Library | 与主项目实现重复，仅用于历史验收 |
| `other/SceneSettings/UnityPanelLab/Builds` | 移出旧 EXE 构建 | 构建不可替代主项目源码，也不再是当前入口 |
| `other/tiles/unused` | 移出未启用历史牌面来源 | `other/tiles/packs` 与 `sources` 是当前牌包和源文件入口 |
| 各 C#/dotnet `bin`、`obj`、Unity 编译缓存 | 移出 | 可由源码重新生成，不应进入美术/源码归档 |
| 旧网页图库脚本、重复 ZIP、一次性实验脚本 | 移出 | 保留 PNG、README、报告和汇总数据，移除捕获/打包工具副本 |
| `open_mahjong_unity/Assets/Editor/CardModeSampleExport.cs` | 移出 | 只导出已退役的 `UnityPanelLab` 示例，目标路径已经不存在 |
| `tools/tilepack/convert_tilepacks.py`、`rebuild_official_flowers.py`、`fix_pin5_white_border.py` | 移出 | 兼容转发入口和一次性实验已由 `rebuild_table_faces.py` 取代；原脚本只会抛出退役提示 |
| `tools/tilepack/__pycache__`、`other/rule/research/_tools/__pycache__`、`other/tiles/face-build.json`、`open_mahjong_web/client/vite-dev.log` | 移出/忽略 | Python 缓存、牌面生成报告和本地开发日志均可重建，不属于共享源码或素材 |

隔离区可恢复；本次没有使用 Git 回滚，也没有覆盖用户已有的工作区改动。

## 主项目继续保留

- `Assets/Resources/TableSurfacePreviews`：32 项桌布、边框和接缝预览，`catalog.json` 可解析且全部 32 张 PNG 存在。
- `Assets/Resources/image/Board/TableCloth`、`TableSeams`、`CenterDisplay`：运行时桌布、接缝和中心盘素材。
- `Assets/Resources/image/CardFacePacks` 与 `other/tiles`：当前牌面及其来源、牌体背景、牌背和生成参数。
- 主项目中的中心盘、桌布接缝、牌面上传、3D 模型、描边和自由模式代码，以及场景序列化结果。
- `open_mahjong_unity/Assets/Editor` 中仍服务于当前主项目验证的工具；已确认不再使用的示例导出工具已移出。

这些主项目变更仍留在当前 Git 工作区，是否提交由后续发布/评审决定；本次没有自动 commit 或 push。

## 仍保留在 `other` 中的内容

- `other/GuobiaoMahjongTiles`：带许可证和来源说明的开源牌面候选。
- `other/3Dcard`：当前 3D 牌模型、Blender 源文件和比例/导出工具。
- `other/TileStyleLab`：结论性图片和历史测量/验收报告；其中引用已退役示例路径的 JSON 是历史证据，不是运行入口。
- Web UI、花牌、事件自动匹配、准备桌、段位和账号宽度等任务的前后对照 PNG、README 和必要检查摘要。

完整历史 `Tablecloth` 设计页（包括验证日志、缓存、历史示例和源 ZIP）已移至本地隔离工作区；供项目其他使用者继续修改的最小共享素材集已整理回 `other/Tablecloth`，包括 13 张桌布、12 张边框、7 张缝线和全部 9 个真实 PSD。没有伪造新的扁平 PSD。

## 验证结果

- 运行时桌面预览目录：32/32 文件存在。
- 主项目中心盘预览：6 张 PNG 存在。
- 桌布源 ZIP 和中心盘历史源码 ZIP 均保存在 `other` 或本地隔离区。
- 退役示例工程已不再位于 `other`；当前生产入口扫描未发现对这些示例的运行时引用。历史审计 JSON 和验收报告中的旧路径按原样保留，用于追溯。
- 当前维护的牌面生成入口仅保留 `tools/tilepack/rebuild_table_faces.py`、`snow_artwork_layout.json` 和 README；旧工具、缓存、生成报告和开发日志已移入隔离区或加入忽略规则。
- 未执行 Unity 全量重导入或发布构建；本次清理没有改变生产脚本逻辑，仅移出了样例、缓存和一次性工具内容。
