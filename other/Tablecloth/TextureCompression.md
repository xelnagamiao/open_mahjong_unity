# 桌面与虹雀 3D 牌面压缩规则

2026-09-15：`TableFrameAssetImporter` 调用 `TableSurfaceCompressionPolicy`，对以下 PNG 使用高质量压缩：

- `Resources/image/Board/TableCloth`：5 张桌布。
- `Resources/image/Board/TableSeams`：5 张接缝。
- `Resources/image/Board/TableLighting/OriginalLight`：1 张光照。
- `Resources/image/Board/Edge`：1 张 256px 默认回退图。
- `Resources/image/Cards/Faces/hongque/table`：126 张虹雀 3D 牌面。

Standalone 使用 BC7，Android 使用 ASTC 4x4；WebGL/iOS 清除旧未压缩覆盖，交由平台的 Automatic / CompressedHQ 选择。压缩有损，保持最高质量，不开启 Crunch。ASTC 的设备兼容性及最终 Android 包仍需真机/构建验证，不保证旧设备原生支持 ASTC。

保留原始 PNG、GUID、alpha、颜色空间、采样和 mip 设置；Read/Write 关闭。桌布合成走 GPU，不需要 CPU 可读源图。虹雀 3D 牌面导入时从 220×366 向上缩放到 POT 256×512，不裁切原图，沿用原有归一化 UV。

**不对虹雀 hand 应用此规则。** 126 张手牌原图为 272×389，`HongqueTileVisual.LoadSprite` 使用完整贴图建立 Sprite，预览使用 preserveAspect。直接改 POT 会改变显示比例。隔离导入确认，仅设置 BC7 而保留原 NPOT 尺寸会回退 RGBA32；导入后补边实验也未成功启用 BC7。因此手牌需另做保留原显示区域的补边/图集方案，不能直接批量切 POT。

已有资源通过编辑器延迟任务自动迁移；播放时不迁移，回到编辑模式后继续。也可执行 `Tools > Mahjong > Table Surfaces > Apply Texture Compression`。处理幂等，不改场景或存档。

## 验证与体积口径

在 Unity 6000.4.7f1 隔离项目中，138 张实际资源在 Standalone 全部导入为 BC7，切换 Android 后全部导入为 ASTC_4x4；Read/Write 关闭，重复配置无变动。抽检桌布、接缝、光照和虹雀 3D 牌面 GPU 对比图，牌面有轻微边缘柔化。主项目 C# 离线编译通过。未进行完整对局、Android 真机或最终 Windows/APK 构建。

按导入尺寸、实际 BC7 格式及 mip 层计算，目标纹理数据约由 234.67 MB 降为 68.23 MB，减少约 166.44 MB（十进制）。其中 Board 约 180.62→46.20 MB，虹雀 table 约 54.05→22.02 MB。**这是纹理数据估算，不是最终压缩包实测值，也不是原始 PNG 文件缩小。** 旧建议中的 Board 522 MB、Windows 700 MB 不适用于已经清理过的当前项目。

本机验证、旧 meta 备份、对比图位于 `.om_workspace/20260915-texture-compression/`。恢复时应同时恢复旧导入设置和停用对应策略，避免下次导入重新应用。
