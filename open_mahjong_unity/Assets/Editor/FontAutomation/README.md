# 平台字体自动准备

- 普通进入 Play Mode 不扫描、不裁剪、不烘焙。播放器也不包含扫描代码。
- 相关资源导入后等待 8 秒，在编辑器空闲时运行后台增量扫描；持续编辑会合并处理。
- 构建前检查所有输入文件的时间戳和大小，只读取变化文件。有效字符集没有变化时不重写字体或图集。
- 完整 Regular 保留动态补字。Medium/Bold 完整源文件只用于 Editor，播放器携带保留真实字形的子集。
- 常用中英文本静态烘焙；新中文放入独立的自动扩展图集，不扩大原 4096 图集。不能预知的在线内容由完整 Regular 补字。
- 构建检查实际场景文字和最终打包内容。未知在线字符仍受完整 Regular 字体本身的覆盖范围限制。
- 空源黑体保留原静态图集与镂空材质，自动收集对应组件、混合字体别名和其三语翻译，补齐数字及英文。Font Awesome 自动收集组件和代码中的图标码点；同属固定 UI 的动态图标资产也改为静态烘焙，完整源文件不进包。
- 空源等装饰字体用于固定 UI，不能预知的在线任意文案仍应使用完整 Regular。构建时会检查场景实际字体覆盖，不会把缺失的装饰字形伪装成正常显示。

首次准备或字符集发生变化需要 Python 3 与 fontTools：

```sh
python -m pip install -r Assets/Editor/FontAutomation/requirements.txt
```

默认使用 PATH 中的 `python`。需要指定解释器时设置环境变量 `SALASASA_FONT_PYTHON`，值为解释器的完整路径。不会自动安装依赖或修改系统环境。生成的 OTF、manifest、TMP 资产应一同提交。

手动入口：`Tools/Salasasa/Fonts/Update Generated Fonts`。缓存、扫描报告和耗时在 `Library/PlatformFonts`，删除 Library 后会重建。源字体和烘焙器变更会重新计算子集；字体子集内部名称为 Salasasa UI Sans，原始 SIL OFL 许可证见 FontSources 目录。

字符来源包括运行时代码字符串/字符常量、场景、预制体、ScriptableObject 的文本字段，以及 Resources / StreamingAssets 的 JSON、CSV、TSV 和 TXT。动态拼接的固定片段也会被收集。外部工作簿生成的 UI 文案通过其生成的项目资源收集。后台未来发送的文字不可能在构建时穷举。
