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
