# open_mahjong_unity 开发文档

## 概述

欢迎使用 open_mahjong_unity！

Web端测试网址 [https://salasasa.cn](https://salasasa.cn)

PC端Steam页 [https://store.steampowered.com/app/4565740/Salasasa/](https://store.steampowered.com/app/4565740/Salasasa/)

Salasasa麻将平台测试群 906497522

## 项目简介

open_mahjong_unity是一款基于unity/python-fastapi的麻将平台项目，该项目遵循MIT许可协议、免费、开源、支持PC/安卓/ios三端互通；目标是支持所有麻将规则、并且提供给玩家自定义规则的选项。欢迎加入qq群参与讨论、协助和测试。

## 1.许可说明

本项目采用 MIT 许可证（详见 LICENSE 文件）。

该MIT许可证授权范围如下：

除明确排除的部分外，本项目的所有源代码及文件均可根据 MIT 许可证的条款自由使用、复制、修改、合并、发布、分发、再许可和/或销售，前提是保留原始版权声明和本许可声明，以下是明确排除部分的详细描述：

### 1.1 项目贡献者保留部分权利、或不完整授权的引用资源

资源文件夹下仅授权用于本项目的资产，以及遵循其他声明或开源协议的借物、包括open_mahjong_unity/Assets/Resources 目录及其所有子目录和文件，不适用 MIT 许可证。该目录中的内容（包括但不限于图像、音频、模型、配置文件等资源）仅供本项目内部使用，未经版权所有者事先书面许可，不得被提取、复制、修改、分发、再许可、用于其他项目，或用于任何商业或非商业用途，版权所有者保留对未明确授权内容的全部权利。
若 open_mahjong_unity/Assets/Resources 下的某个子目录中包含独立的许可证文件（如 LICENSE、LICENSE.txt 等），则该子目录中的内容以该独立许可证为准，优先于上述限制。
此外，如果您仅以非商业目的在个人服务器或私有环境中部署本项目（包括采用本相同声明的修改版或分支），则视为在本项目范围内获得了对 Resources 文件夹中资源的使用授权，这一声明超出上述许可证限制，这代表如果您部署时不考虑任何商业用途，就自动拥有了Resources下任意资源的使用和修改权；但该授权仍然不得扩展至独立提取或在其他项目中单独使用这些资源，如果您想要以商业形式部署本项目，需要替换Resources文件夹中未被授权的资产，也可以尝试联系项目维护者或资源提供者、获得个别资源的书面授权。

### 1.2 第三方规则贡献者的权利保留声明

本条款是对 MIT 许可证的特别补充与限制。本项目中包含的由第三方贡献者提供的特定游戏规则变体、玩法逻辑及数值配置代码（通常位于 /calculation/目录或明确标注规则贡献者信息的文件），在默认状态下遵循 MIT 许可证条款，允许使用者进行商业运营、公共服务器部署、任意分发及修改。但是，各规则内容的原始贡献者仍保留对其独创玩法逻辑的著作权，拥有单方面收回其商业使用权的权利。
如果您部署的平台与规则创作者之间产生了某些争议，或者贡献者通过项目仓库公告、代码移除或书面通知的方式声明终止授权，使用者须在收到通知后的合理期限内(默认为30天)，停止基于该特定规则的公共服务与商业发行，这一点属于不可控因素，希望可以理解。

### 1.3 关于Salasasa名称和本项目的关系的声明

“Salasasa” 名称以及网址 salasasa.cn 是项目示例服务器的标识与域名。在基于 open_mahjong_unity 创建独立分支或修改版本时，请遵守以下约定：
    1.不得使用 “Salasasa” 原名称来命名自己的服务器，但以个人学习、维护特定规则为目的，不以商业形式公开发行或得到临时许可的除外；
    2.未经许可，不得在“Salasasa”命名以外的客户端中继续使用 salasasa.cn 的 API 或服务器服务；
    3.基于 “Salasasa” 命名的客户端变体允许存在，但必须在获取许可的同时，在客户端的版本验证 API 中显式标注该版本为非官方修改版；
    4.如果客户端变体出现网络攻击、恶意行为或严重影响服务器正常运行的情况，salasasa 保留要求停止发布、封禁相关客户端，以及采取法律措施维权的权利。

## 2.项目结构

仓库由 Unity 客户端、Python 游戏服务器、Node.js/Vue Web 平台和 Go 聊天服务器组成。以下仅列出主要源码目录；规则实现、第三方资源以及构建生成目录不继续展开。

```text
open_mahjong_unity/
├── open_mahjong_unity/             # Unity 游戏客户端
│   ├── Assets/
│   │   ├── Editor/                 # Unity 编辑器扩展
│   │   ├── Plugins/                # 第三方插件
│   │   ├── Prefabs/                # UI、房间及牌桌预制体
│   │   ├── Resources/              # 模型、图片、字体、音效等运行时资源
│   │   ├── Scenes/                 # Unity 场景
│   │   ├── Scripts/
│   │   │   ├── ChatServer/         # 聊天功能
│   │   │   ├── Config/             # 客户端配置与用户数据
│   │   │   ├── GameScene/          # 对局、牌桌、计分及各规则表现
│   │   │   ├── GameSceneConfig/    # 牌桌外观配置
│   │   │   ├── Network/            # HTTP/WebSocket 通信
│   │   │   ├── Rendering/          # 渲染相关功能
│   │   │   ├── Room/               # 房间创建与管理
│   │   │   └── UI/                 # 主界面与通用 UI
│   │   └── Shaders/                # 着色器
│   ├── Packages/                   # Unity 包配置
│   └── ProjectSettings/            # Unity 项目设置
│
├── open_mahjong_server/            # Python/FastAPI 游戏服务器
│   ├── main.py                     # 服务启动入口
│   ├── server/
│   │   ├── server.py               # FastAPI 与 WebSocket 主服务
│   │   ├── chat_server/            # 聊天服务接入
│   │   ├── database/               # 数据库、统计与牌谱存储
│   │   ├── event/                  # 赛事功能
│   │   ├── friend/                 # 好友系统
│   │   ├── game_calculation/       # 和牌、听牌及规则计算
│   │   ├── gamestate/              # 各麻将规则的对局状态机
│   │   ├── match/                  # 匹配系统
│   │   ├── public/                 # 公共服务与限流等模块
│   │   ├── room/                   # 房间管理
│   │   └── webapi/                 # Web 计算接口
│   ├── scripts/                    # 维护与迁移脚本
│   ├── load_test/                  # 负载测试
│   ├── pyproject.toml              # Python 项目配置
│   ├── requirements.txt            # Python 依赖
│   ├── uv.lock                     # uv 依赖锁定
│   └── start_server.bat            # Windows 启动脚本
│
├── open_mahjong_web/               # Node.js/Vue 3 Web 平台
│   ├── client/                     # Vue 3 前端
│   │   ├── public/                 # 静态资源与 Unity WebGL 文件
│   │   ├── src/
│   │   │   ├── api/                # HTTP 客户端
│   │   │   ├── components/         # 通用组件
│   │   │   ├── composables/        # Vue 组合式逻辑
│   │   │   ├── game2d/             # 2D 对局前端
│   │   │   ├── layouts/            # 页面布局
│   │   │   ├── router/             # 路由
│   │   │   ├── stores/             # Pinia 状态管理
│   │   │   ├── styles/             # 全局样式
│   │   │   ├── utils/              # 前端工具
│   │   │   └── views/              # 页面
│   │   ├── package.json
│   │   └── vite.config.js
│   ├── server/                     # Express/Socket.IO 后端
│   │   ├── index.js                # Node.js 服务入口
│   │   ├── botapi/                 # 机器人接口
│   │   ├── config/                 # 数据库及环境配置
│   │   ├── guessfan/               # 猜番对抗服务
│   │   ├── middleware/             # 鉴权与限流中间件
│   │   ├── routes/                 # Web API 路由
│   │   ├── services/               # 服务层
│   │   └── utils/                  # 服务端工具
│   ├── docs/                       # Web 部署与功能文档
│   ├── scripts/                    # 构建、部署脚本
│   ├── package.json
│   └── start.bat
│
├── open_mahjong_chatServer/        # Go 聊天服务器
│   ├── Main.go                     # 程序入口
│   ├── ConnectPool.go              # 连接池
│   ├── RoomManager.go              # 聊天房间管理
│   ├── go.mod
│   └── go.sum
│
├── other/                          # 美术源文件、实验代码、规则资料与辅助素材
├── LICENSE
└── README.md
```

## 3.技术栈

### 游戏客户端 (open_mahjong_unity)

- **引擎**: Unity 6.4 (6000.4.7f1)
- **语言**: C#
- **网络**: WebSocket (Nativewebsocket)
- **平台**: PC/Web/Android/iOS

### 游戏服务器 (open_mahjong_server、open_mahjong_chat_server)

- **框架**: FastAPI
- **语言**: Python 3.12 Golang
- **网络**: WebSocket, HTTP
- **数据库**: PostgreSQL 18
- **部署**: supervisor 或 任意您喜欢的任务管理器

### Web 平台 (open_mahjong_web)

- **前端**: Vue3
- **后端**: Node.js
- **数据库**: PostgreSQL 18

### 4.交流

- **Salasasa平台测试群**: 906497522
- **open_mahjong_unity开发交流群**: 1084537740
- **项目地址**: [https://github.com/xelnagamiao/open_mahjong_unity](https://github.com/xelnagamiao/open_mahjong_unity)
- **语雀文档(未完成)**: [https://www.yuque.com/xelnaga-yjcgq/zkwfgr/lusmvid200iez36q?singleDoc#](https://www.yuque.com/xelnaga-yjcgq/zkwfgr/lusmvid200iez36q?singleDoc#)
- **开发进度表**: [https://docs.qq.com/sheet/DZkh2a2VBQkpucXNr?tab=BB08J2](https://docs.qq.com/sheet/DZkh2a2VBQkpucXNr?tab=BB08J2)
- **赞助**: q1448826180

### 5.鸣谢

牌面提供者：雪枫XueFun9
表情包提供者：影子
随机种子设计：Zoe
新编MCR编著者：Natsuki
青雀设计者：莫莫柴
浪涌麻将设计者：自恧
直播宣传：Cloud980Ti  轻轻的飘
赞助：九曜、健哥、何苏、Null、莫莫柴、Zazaka、中山大学国标麻将同好会、kiki、东西喵、GitHub/baisebaoma
特别感谢：莫莫柴、码龙、Null、影子、chinkaku
支持：棋牌游戏研究院、立直麻雀研习社、柴の麻将群
早期测试：夜色祢 chlorine 陪练的命运

*最后更新：2026年7月29日 dev ver 0.4.74.0*
