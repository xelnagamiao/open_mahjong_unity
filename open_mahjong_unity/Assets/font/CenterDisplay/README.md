# 中心盘静态字体

本目录保存中心盘专用字体原件、许可及静态字符清单。现有默认读数使用 Adobe 思源黑体 SC Medium 2.005 的原厂静态 500 字重，不使用合成粗体或人为拉大字距。字体的完整 family 为 `Source Han Sans SC Medium`；部分读取器将 subfamily 显示为 `Regular`，但 OS/2 字重仍为 500。

## 来源与授权

- 原件：`SourceHanSansSC-Medium.otf`，16,546,328 字节。
- 官方项目：[Adobe Source Han Sans](https://github.com/adobe-fonts/source-han-sans)。
- 官方下载：[SimplifiedChinese/SourceHanSansSC-Medium.otf](https://raw.githubusercontent.com/adobe-fonts/source-han-sans/release/OTF/SimplifiedChinese/SourceHanSansSC-Medium.otf)。
- 原件 SHA-256：`1df61d31687d04fd2f928a3bb6ca6cd61f0e988cc267cf317f32406edbb49f70`。
- 许可证：本目录 `OFL.txt`，来自 [Adobe 官方 LICENSE.txt](https://github.com/adobe-fonts/source-han-sans/blob/master/LICENSE.txt)。SHA-256：`fcac737e761ec63dbfbdce11030a1780161920d80315edba9c8beff1c2bac5a2`。

字体采用 SIL Open Font License 1.1，可随商业软件嵌入、打包及再分发。发布时保留版权声明与本目录 `OFL.txt`；字体不得单独出售。修改后的字体须遵守保留名称 `Source` 的限制。中心盘派生资源使用项目自己的名称 `CenterReadoutStatic`。

完整黑体源文件保留在 `Assets/font`，用于后续重新烘焙；运行时中心盘加载 `Resources` 中的 `font/CenterDisplay/CenterReadoutStatic`，不依赖完整源文件。

## 已烘焙资源

资源路径：`Assets/Resources/font/CenterDisplay/CenterReadoutStatic.asset`。

运行时 Resources 地址：`font/CenterDisplay/CenterReadoutStatic`。

同一 Resources 目录内另存一份 `OFL.txt`，作为 TextAsset 随字体资源进入玩家构建；本目录仍保留原件的完整许可。

2026-09-12 在 Unity 6000.4.7f1 中完成烘焙及重新加载检查：

- TMP `Static`，单张 1024 × 1024 图集；90 point、padding 8、SDFAA。
- 126 个字符、126 个字形，缺字 0；材质和图集均保存为该字体资源的子资产。
- outline、face dilation、normal/bold synthetic weights 均为 0；多图集关闭。
- 本字体的 0–9 原始 advance 都为 570/1000 em，数值宽度稳定。文本使用 `FontStyles.Normal`、`characterSpacing = 0`；不要再叠加 Bold 或字形膨胀。

烘焙报告保存在仓库工作目录 `.om_workspace/20260912-center-v2/fonts/report.txt`，状态在同目录 `status.txt`。上述是字体资产烘焙结果，不代替主场景镜头下的布局和可读性检查。

## 字符覆盖

本目录 `characters.txt` 是 UTF-8、无 BOM、无换行的 126 字符输入，首字符为空格。当前内容 SHA-256：`1f7652d938d95cb433f3db6730509446b8648036bc0c71f7fdc7b9a7458a5208`。

- U+0020–U+007E 的 95 个可打印 ASCII：大小写英文、数字、空格、正负号、半角冒号和问号等，覆盖基础英文 fallback。
- 当前中心盘中文：`东南西北风一二三四五六七八九十第局副余`。
- 兼容及同入口总局数文案：`東風战全庄未知共`；额外排版符号：`−×：·`。

字符清单按 `Assets/Scripts/Config/RoundTextDictionary.cs`、规则 RoundName 注册、虹雀/四川/长沙 FanText，以及 BoardCanvas 实际显示文案核对：

| 规则或字段 | 已覆盖的原文 |
| --- | --- |
| 古典、简单、青雀、立直、台湾 | `东一局`…`北四局`，保留中文数字 |
| 国标 | `东风东`…`北风北`，共 16 项 |
| 虹雀 | `第一局`…`第十六局`；越界回退 `第{数字}局` |
| 四川 | `第一副`…`第十六副`；越界回退 `第{数字}副` |
| 长沙、未知规则 | `第{数字}局` |
| 风位、余牌、分数/分差 | `东南西北`、`?`、`余:48`、`25000`、`-1300`、`+1000` 等 |

额外覆盖总局数入口的 `东风战`、`东南战`、`东西战`、`全庄战`、`未知({数字})`、`四局`、`八局`、`十六局` 和 `共{数字}局`。这些总局数文案当前不是中心盘运行字段。原代码负分使用 ASCII `-`，余牌使用半角 `:`，不要仅保留外观近似的 `−` 或 `：`。

## 新增字符后的重新烘焙

静态字体不会在运行时添加新字形。增加规则或改变局名后，先核对所有局名、风位及回退文案，将新增字符合并到本目录 `characters.txt`，保留现有集合及开头空格。四字局名（如 `第十六副`）和负分仍需完整显示。运行包只更新 `Assets/Resources/font/CenterDisplay/CenterReadoutStatic.asset`。
