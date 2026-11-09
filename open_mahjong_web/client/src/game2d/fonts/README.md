# Salasasa 2D 字体

2D 的中文字体和英文字体分别设置。随项目发布的字体会在连接游戏 WebSocket 前完成
下载，避免进入牌桌后再加载。

## 中文字体

| 选项 | 本地文件 | 上游来源 | 许可证 |
| --- | --- | --- | --- |
| 思源宋体 | `noto-serif-sc.woff2` | https://github.com/notofonts/noto-cjk | `OFL-Noto-Serif-SC.txt` |
| 系统楷体 | 无，只调用玩家系统中的 KaiTi/STKaiti 等 | 操作系统 | 不随项目分发 |
| 系统默认 | 无，调用 system-ui、微软雅黑、苹方等系统界面字体 | 操作系统 | 不随项目分发 |
| AR PL KaitiM GB | `gkai00mp.ttf` | https://packages.debian.org/fonts-arphic-gkai00mp | `ARPHIC-PUBLIC-LICENSE.txt` |

`gkai00mp.ttf` 来自 Debian 上游 `fonts-arphic-gkai00mp` 2.11 原始发布包，
保持原始 TTF 文件不变。根据 Arphic Public License 第 1 节，原始字体可在任何
介质复制和分发，但每一份副本都必须保留未经修改的许可证。

## 英文字体

| 选项 | 本地文件 | 许可证 |
| --- | --- | --- |
| Latin Modern（项目早期风格） | `latinmodern-math.woff2` | `GUST-FONT-LICENSE-Latin-Modern.txt` |
| Noto Serif Latin | `noto-serif-latin.woff2` | SIL OFL 1.1 |
| Noto Sans Latin | `noto-sans-latin.woff2` | SIL OFL 1.1 |

系统楷体只在本机存在时使用，不会把 Windows 的 SimKai 字体复制到服务器。

## 西里尔字符回退

俄语使用 `noto-sans-cyrillic.woff2`（Noto Sans Regular 400，Google Fonts 官方
西里尔子集，7,096 字节），许可证为随附的 `OFL-Noto-Sans.txt`（SIL OFL 1.1）。
字体及许可证保持下载原样，作为本地资源发布，不在运行时请求 Google Fonts。

- 字体声明来源：https://fonts.googleapis.com/css2?family=Noto+Sans:wght@400&display=swap
- 字体文件：https://fonts.gstatic.com/s/notosans/v42/o-0mIpQlx3QUlC5A4PNB6Ryti20_6n1iPHjcz6L1SoM-jCpoiyD9A-9e6VLKzA.woff2
- 许可证来源：https://github.com/google/fonts/blob/main/ofl/notosans/OFL.txt
- SHA-256：`05af9e705d3b63aedba67197d19fb448b97353e1386930716c7e835b08f126ba`

该字体放在所选英文字体之后、中文字体之前，并使用上游的 `unicode-range`。
这样俄文字母不会落到楷体的全宽字形，同时保留已有中英文字体选择。预加载使用
俄文样本，确保此子集在进入牌桌前就绪。
