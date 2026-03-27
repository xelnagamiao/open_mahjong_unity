# open_mahjong_unity 开发文档

## 概述

欢迎使用 open_mahjong_unity！ 测试网址salasasa.cn

## 项目简介

open_mahjong_unity是一款基于unity/python-fastapi的麻将平台项目，该项目遵循MIT许可协议、免费、开源、支持PC/安卓/ios三端互通；目标是支持所有麻将规则、并且提供给玩家自定义规则的选项。欢迎加入q群906497522参与讨论、协助和测试。

## 1.许可说明

本项目采用 MIT 许可证（详见 LICENSE 文件）。

该MIT许可证授权范围如下：

除明确排除的部分外，本项目的所有源代码及文件均可根据 MIT 许可证的条款自由使用、复制、修改、合并、发布、分发、再许可和/或销售，前提是保留原始版权声明和本许可声明，以下是明确排除部分的详细描述：

### 1.项目贡献者保留部分权利、或不完整授权的引用资源

资源文件夹下仅授权用于本项目的资产，以及遵循其他声明或开源协议的借物、包括open_mahjong_unity/Assets/Resources 目录及其所有子目录和文件，不适用 MIT 许可证。该目录中的内容（包括但不限于图像、音频、模型、配置文件等资源）仅供本项目内部使用，未经版权所有者事先书面许可，不得被提取、复制、修改、分发、再许可、用于其他项目，或用于任何商业或非商业用途，版权所有者保留对未明确授权内容的全部权利。
若 open_mahjong_unity/Assets/Resources 下的某个子目录中包含独立的许可证文件（如 LICENSE、LICENSE.txt 等），则该子目录中的内容以该独立许可证为准，优先于上述限制。
此外，如果您仅以非商业目的在个人服务器或私有环境中部署本项目（包括采用本相同声明的修改版或分支），则视为在本项目范围内获得了对 Resources 文件夹中资源的使用授权，这一声明超出上述许可证限制，这代表如果您部署时不考虑任何商业用途，就自动拥有了Resources下任意资源的使用和修改权；但该授权仍然不得扩展至独立提取或在其他项目中单独使用这些资源，如果您想要以商业形式部署本项目，需要替换Resources文件夹中未被授权的资产，也可以尝试联系项目维护者、获得个别资源的书面授权。

### 2.第三方规则贡献者的权利保留声明

本条款是对 MIT 许可证的特别补充与限制。本项目中包含的由第三方贡献者提供的特定游戏规则变体、玩法逻辑及数值配置代码（通常位于 /calculation/目录或明确标注规则贡献者信息的文件），在默认状态下遵循 MIT 许可证条款，允许使用者进行商业运营、公共服务器部署、任意分发及修改。但是，各规则内容的原始贡献者仍保留对其独创玩法逻辑的著作权，拥有单方面收回其商业使用权的权利。
如果您部署的平台与规则创作者之间产生了某些争议，或者贡献者通过项目仓库公告、代码移除或书面通知的方式声明终止授权，使用者须在收到通知后的合理期限内(默认为30天)，停止基于该特定规则的公共服务与商业发行，这一点属于不可控因素，希望可以理解。

## 2.项目结构

本项目的文件结构如下：

```
open_mahjong_unity/
├── open_mahjong_unity/        # Unity 游戏客户端
│   ├── Assets/                # Unity 资源文件
│   │   ├── Prefabs/           # 预制体
│   │   │   ├── Chat/          # 聊天相关预制体
│   │   │   ├── GameScene/     # 游戏场景预制体
│   │   │   ├── Notification/  # 通知相关预制体
│   │   │   │   └── PlayerInfo/ # 玩家信息预制体
│   │   │   ├── Record/        # 记录相关预制体
│   │   │   └── Room/          # 房间相关预制体
│   │   ├── Scenes/            # 场景文件
│   │   │   ├── MainScene.unity
│   │   │   └── GameScene.unity
│   │   ├── Scripts/           # C# 脚本
│   │   │   ├── ChatServer/    # 聊天服务器相关脚本
│   │   │   │   ├── ChatManager.cs      # 聊天管理器
│   │   │   │   ├── ChatRequest.cs      # 聊天请求
│   │   │   │   └── ChatResponse.cs     # 聊天响应
│   │   │   ├── Config/        # 配置管理脚本
│   │   │   │   ├── ConfigManager.cs   # 配置管理器
│   │   │   │   └── UserDataManager.cs # 用户数据管理器
│   │   │   ├── GameScene/     # 游戏场景脚本
│   │   │   │   ├── 3DManager/          # 3D 管理相关脚本
│   │   │   │   │   ├── Game3DManager.cs # 3D 游戏管理器（主文件）
│   │   │   │   │   ├── Game3DManager.Actions.cs # 操作相关
│   │   │   │   │   ├── Game3DManager.Animations.cs # 动画相关
│   │   │   │   │   ├── Game3DManager.GetTile.cs # 获取牌
│   │   │   │   │   ├── Game3DManager.HandCardArrangement.cs # 手牌排列
│   │   │   │   │   ├── Game3DManager.RemoveHandCards.cs # 移除手牌
│   │   │   │   │   ├── Game3DManager.SetTile.cs # 设置牌
│   │   │   │   │   ├── PosPanel3D.cs # 3D 位置面板
│   │   │   │   │   └── 3DObject/      # 3D 对象相关
│   │   │   │   │       ├── Card3DHoverManager.cs # 3D 卡牌悬停管理
│   │   │   │   │       ├── MahjongObjectPool.cs # 麻将对象池
│   │   │   │   │       ├── OutlineNormalsCalculator.cs # 轮廓法线计算
│   │   │   │   │       └── Tile3D.cs # 3D 牌对象
│   │   │   │   ├── BoardManager/      # 桌面管理相关脚本
│   │   │   │   │   ├── BoardCanvas.cs # 桌面画布
│   │   │   │   │   ├── BoardCanvas_CurrentDisplay.cs # 当前显示
│   │   │   │   │   └── ControPanel.cs # 控制面板
│   │   │   │   ├── Calculation/       # 计算相关脚本
│   │   │   │   │   ├── GBhepai.cs    # 国标和牌检查
│   │   │   │   │   ├── GBtingpai.cs  # 国标听牌检查
│   │   │   │   │   ├── Qingque13External.cs # 青雀外部接口
│   │   │   │   │   ├── TestPanel.cs  # 测试面板
│   │   │   │   │   └── Qingque13/    # 青雀计算模块
│   │   │   │   │       └── Qingque13Calc/ # 青雀计算核心
│   │   │   │   │           ├── Core/  # 核心类
│   │   │   │   │           ├── Criteria/ # 番种判断
│   │   │   │   │           ├── Patterns/ # 牌型模式
│   │   │   │   │           ├── Qingque13Hepai.cs # 和牌检查
│   │   │   │   │           ├── Qingque13Tingpai.cs # 听牌检查
│   │   │   │   │           ├── QingqueDerepellenise.cs # 去重复
│   │   │   │   │           ├── QingqueFan.cs # 番数计算
│   │   │   │   │           ├── QingqueFanEvaluator.cs # 番数评估
│   │   │   │   │           └── QingqueScoring.cs # 计分
│   │   │   │   ├── CanvasManager/    # 画布管理相关脚本
│   │   │   │   │   ├── GameCanvas.cs # 游戏画布
│   │   │   │   │   ├── GameCanvas_ActionButton.cs # 操作按钮
│   │   │   │   │   ├── GameCanvas_ActionDisplay.cs # 操作显示
│   │   │   │   │   ├── GameCanvas_Changehand.cs # 手牌变更
│   │   │   │   │   ├── GameCanvas_Timer.cs # 计时器
│   │   │   │   │   └── CanvasControl/ # 画布控制
│   │   │   │   │       ├── ActionButton.cs # 操作按钮
│   │   │   │   │       ├── BlockClick.cs # 块点击
│   │   │   │   │       ├── HoverEventTrigger.cs # 悬停事件
│   │   │   │   │       ├── StaticCard.cs # 静态卡牌
│   │   │   │   │       ├── TileCard.cs # 牌卡
│   │   │   │   │       └── Tips/ # 提示相关
│   │   │   │   │           ├── FanCount.cs # 番数
│   │   │   │   │           └── TipsFanCount.cs # 提示番数
│   │   │   │   ├── GameSceneUI/      # 游戏场景 UI
│   │   │   │   │   ├── AutoAction.cs # 自动操作
│   │   │   │   │   ├── EndGamePanel.cs # 游戏结束面板
│   │   │   │   │   ├── EndLiujuPanel.cs # 流局面板
│   │   │   │   │   ├── EndResultPanel.cs # 结果面板
│   │   │   │   │   ├── GamePlayerPanel.cs # 玩家面板
│   │   │   │   │   ├── GameSceneUIManager.cs # UI 管理器
│   │   │   │   │   ├── GameScoreRecord.cs # 游戏分数记录
│   │   │   │   │   ├── RecordSetting.cs # 记录设置
│   │   │   │   │   ├── RoundPanel.cs # 轮次面板
│   │   │   │   │   ├── StartGamePanel.cs # 开始游戏面板
│   │   │   │   │   ├── SwitchSeatPanel.cs # 换位面板
│   │   │   │   │   ├── TipsBlock.cs # 提示块
│   │   │   │   │   └── TipsContainer.cs # 提示容器
│   │   │   │   ├── GameStateManager/ # 游戏状态管理
│   │   │   │   │   ├── NormalGameStateManager.cs # 普通游戏状态管理器
│   │   │   │   │   ├── SpectatorGameStateManager.cs # 观战游戏状态管理器
│   │   │   │   │   └── GameRecordManager/ # 游戏记录管理
│   │   │   │   │       ├── GameRecordData.cs # 游戏记录数据
│   │   │   │   │       ├── GameRecordJsonDecoder.cs # JSON 解码器
│   │   │   │   │       └── GameRecordManager.cs # 游戏记录管理器
│   │   │   │   └── GameSceneConfig/  # 游戏场景配置
│   │   │   │       ├── CharacterPanel.cs # 角色面板
│   │   │   │       ├── Desktop.cs # 桌面
│   │   │   │       ├── SceneConfigPanel.cs # 场景配置面板
│   │   │   │       ├── TableCloth.cs # 桌布
│   │   │   │       ├── TableClothPanel.cs # 桌布面板
│   │   │   │       ├── TableEdge.cs # 桌边
│   │   │   │       ├── TableEdgePanel.cs # 桌边面板
│   │   │   │       └── UploadFile.cs # 上传文件
│   │   │   ├── Network/       # 网络通信脚本
│   │   │   │   ├── NetworkManager.cs     # 网络管理器
│   │   │   │   ├── DataNetworkManager.cs # 数据网络管理器
│   │   │   │   ├── GameStateNetworkManager.cs # 游戏状态网络管理器
│   │   │   │   ├── RoomNetworkManager.cs # 房间网络管理器
│   │   │   │   ├── Request.cs            # 请求类
│   │   │   │   └── Response.cs           # 响应类
│   │   │   ├── Others/        # 其他工具脚本
│   │   │   │   ├── Destroyer.cs           # 销毁器
│   │   │   │   └── SoundManager.cs       # 音效管理器
│   │   │   ├── Room/          # 房间管理脚本
│   │   │   │   ├── CreateRoomPanel/      # 创建房间面板
│   │   │   │   │   ├── CreatePanel.cs    # 创建面板
│   │   │   │   │   ├── GB_Create_Panel.cs # 国标创建面板
│   │   │   │   │   ├── GB_Create_RoomConfig.cs # 国标房间配置
│   │   │   │   │   ├── Qingque_Create_Panel.cs # 清雀创建面板
│   │   │   │   │   └── Qingque_Create_RoomConfig.cs # 清雀房间配置
│   │   │   │   ├── RoomListPanel/        # 房间列表面板
│   │   │   │   │   ├── RoomListPanel.cs  # 房间列表
│   │   │   │   │   └── RoomItem.cs      # 房间项
│   │   │   │   └── RoomPanel/            # 房间面板
│   │   │   │       ├── RoomPanel.cs      # 房间面板
│   │   │   │       ├── PlayerRoomPanel.cs # 玩家房间面板
│   │   │   │       ├── RoomConfigContainer.cs # 房间配置容器
│   │   │   │       └── ConfigItem.cs     # 配置项
│   │   │   ├── Editor/        # 编辑器脚本
│   │   │   └── UI/            # UI 相关脚本
│   │   │       ├── Manager/              # UI 管理器
│   │   │       │   ├── WindowsManager.cs # 窗口管理器
│   │   │       │   ├── NotificationManager.cs # 通知管理器
│   │   │       │   └── RoomWindowsManager.cs # 房间窗口管理器
│   │   │       ├── PageContent/          # 页面内容
│   │   │       │   ├── ConfigSlider.cs   # 配置滑块
│   │   │       │   └── LinksContent.cs   # 链接内容
│   │   │       ├── Panel/                # 面板脚本
│   │   │       │   ├── AboutUsPanel.cs   # 关于我们面板
│   │   │       │   ├── AppConfigPanel.cs # 应用配置面板
│   │   │       │   ├── ChatPanel.cs      # 聊天面板
│   │   │       │   ├── HeaderPanel.cs    # 头部面板
│   │   │       │   ├── LoginPanel.cs     # 登录面板
│   │   │       │   ├── MainPanel.cs      # 主面板
│   │   │       │   ├── MeunPanel.cs      # 菜单面板
│   │   │       │   ├── NowPlayer.cs      # 当前玩家
│   │   │       │   └── PlayerConfigPanel.cs # 玩家配置面板
│   │   │       ├── PlayerInfo/           # 玩家信息
│   │   │       ├── Prefab/               # 预制体脚本
│   │   │       └── Scripts/              # UI 脚本
│   │   ├── Plugins/           # 插件目录
│   │   │   ├── Dark UI/       # Dark UI 插件
│   │   │   ├── Free UI Click Sound Effects Pack/ # UI 音效包
│   │   │   ├── TextMesh Pro/ # TextMesh Pro 资源
│   │   │   ├── UI pack/       # UI 包
│   │   │   ├── UnityStandaloneFileBrowser/ # 文件浏览器插件
│   │   │   └── websocket-sharp/ # WebSocket 客户端库
│   │   ├── Resources/         # 资源文件
│   │   │   ├── 3D/            # 3D 模型资源
│   │   │   ├── font/          # 字体资源
│   │   │   ├── image/         # 图片资源
│   │   │   └── Sound/         # 音效资源
│   │   └── Shaders/           # 着色器文件
│   └── ProjectSettings/       # Unity 项目设置
│
├── open_mahjong_server/       # Python 游戏服务器
│   ├── requirements.txt        # Python 依赖
│   ├── pyproject.toml         # Python 项目配置
│   ├── uv.lock                # UV 锁文件
│   ├── start_server.bat       # 启动脚本
│   ├── logs/                  # 日志目录
│   │   └── app.log*           # 应用日志文件
│   └── server/                # 服务器核心代码
│       ├── server.py          # FastAPI 主服务器
│       ├── response.py        # 响应处理
│       ├── local_config.py    # 本地配置
│       ├── test_config.py     # 测试配置
│       ├── secret_key.txt     # 密钥文件
│       ├── database/          # 数据库相关
│       │   ├── db_manager.py  # 数据库管理器
│       │   ├── data_router.py # 数据路由处理
│       │   ├── db_design.md   # 数据库设计文档
│       │   ├── guobiao/       # 国标麻将数据库操作
│       │   │   ├── store_guobiao.py    # 存储国标游戏记录
│       │   │   └── get_guobiao_stats.py # 获取国标统计数据
│       │   └── riichi/        # 立直麻将数据库操作
│       ├── game_calculation/  # 游戏计算服务
│       │   ├── game_calculation_service.py # 游戏计算服务
│       │   ├── gb_hepai_check.py           # 国标和牌检查
│       │   ├── gb_tingpai_check.py         # 国标听牌检查
│       │   ├── qingque13_bridge.py         # 青雀计算桥接
│       │   └── Qingque13Calc/              # 青雀计算模块
│       ├── gamestate/         # 游戏状态管理
│       │   ├── gamestate_manager.py    # 游戏状态管理器
│       │   ├── gamestate_router.py     # 游戏状态路由处理
│       │   ├── game_guobiao/           # 国标麻将游戏逻辑
│       │   │   ├── GuobiaoGameState.py # 国标游戏状态
│       │   │   ├── boardcast.py        # 广播处理
│       │   │   ├── get_action.py       # 获取操作
│       │   │   ├── wait_action.py      # 等待操作
│       │   │   ├── action_check.py     # 动作检查
│       │   │   └── spectator_manager.py # 观战管理器
│       │   ├── game_mmcr/              # 青雀麻将游戏逻辑
│       │   │   ├── QingqueGameState.py # 青雀游戏状态
│       │   │   ├── boardcast.py        # 广播处理
│       │   │   ├── get_action.py       # 获取操作
│       │   │   ├── wait_action.py      # 等待操作
│       │   │   └── action_check.py     # 动作检查
│       │   └── public/                 # 公共游戏状态模块
│       │       ├── init_game_tiles.py  # 初始化游戏牌
│       │       ├── game_record_manager.py # 游戏记录管理
│       │       ├── auto_cut_ai.py      # 自动切牌AI
│       │       ├── next_game_round.py  # 下一局游戏
│       │       └── logic_common.py     # 通用逻辑
│       ├── room/              # 房间管理
│       │   ├── room_manager.py    # 房间管理器
│       │   ├── room_router.py     # 房间路由处理
│       │   ├── room_validators.py # 房间验证
│       │   └── readme.md          # 房间模块说明
│       └── chat_server/       # 聊天服务器管理
│           ├── chat_server.py # 聊天服务器接口
│           ├── open_mahjong_chatServer.exe # 聊天服务器可执行文件
│           └── secret_key.txt # 聊天服务器密钥
│
├── open_mahjong_web/          # Node.js/Vue3 Web 平台
│   ├── server/                # Node.js 后端
│   │   ├── index.js           # Express 主服务器
│   │   ├── config/            # 配置文件
│   │   │   └── database.js    # 数据库配置
│   │   └── routes/            # API 路由
│   │       ├── auth.js        # 认证路由
│   │       └── mahjong.js     # 麻将相关路由
│   ├── client/                # Vue3 前端
│   │   ├── src/               # 源代码
│   │   │   ├── views/         # 页面组件
│   │   │   ├── components/    # 通用组件
│   │   │   ├── router/        # 路由配置
│   │   │   ├── stores/        # 状态管理
│   │   │   └── dll/           # 动态链接库相关
│   │   ├── public/            # 静态资源
│   │   ├── package.json       # 前端依赖
│   │   └── vite.config.js     # Vite 配置
│   ├── package.json           # 后端依赖
│   └── start.bat              # 启动脚本
│
└── open_mahjong_chatServer/   # Go 聊天服务器
    ├── Main.go                # 主程序入口
    ├── ConnectPool.go         # 连接池管理
    ├── RoomManager.go         # 房间管理
    ├── go.mod                 # Go 模块定义
    └── go.sum                 # Go 依赖校验
```

## 3.技术栈

### 游戏客户端 (open_mahjong_unity)
- **引擎**: Unity 2022.3 LTS (2022.3.62f3c1)
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
- **测试/开发群号**: 906497522
- **项目地址**: https://github.com/xelnagamiao/open_mahjong_unity
- **语雀文档(未完成)**: https://www.yuque.com/xelnaga-yjcgq/zkwfgr/lusmvid200iez36q?singleDoc#
- **开发进度表**: https://docs.qq.com/sheet/DZkh2a2VBQkpucXNr?tab=BB08J2

*最后更新：2026年3月16日 dev ver 0.3.52.0 *

