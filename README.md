# open_mahjong_unity 开发文档

## 概述

欢迎使用 open_mahjong_unity！

## 项目简介

open_mahjong_unity是一款基于unity/python-fastapi的麻将平台项目，该项目遵循MIT许可协议、免费、开源、支持PC/安卓/ios三端互通；目标是支持所有麻将规则、并且提供给玩家自定义规则的选项。欢迎加入q群906497522参与讨论、协助和测试。

## 1.许可说明

本项目采用 MIT 许可证（详见 LICENSE 文件）。

该MIT许可证授权范围如下：

除明确排除的部分外，本项目的所有源代码及文件均可根据 MIT 许可证的条款自由使用、复制、修改、合并、发布、分发、再许可和/或销售，前提是保留原始版权声明和本许可声明。
例外内容：open_mahjong_unity/Assets/Resources 目录及其所有子目录和文件 不适用 MIT 许可证。该目录中的内容（包括但不限于图像、音频、模型、配置文件等资源）仅供本项目内部使用，未经版权所有者事先书面许可，不得被提取、复制、修改、分发、再许可、用于其他项目，或用于任何商业或非商业用途。
子目录特例：若 open_mahjong_unity/Assets/Resources 下的某个子目录中包含独立的许可证文件（如 LICENSE、LICENSE.txt 等），则该子目录中的内容以该独立许可证为准，优先于上述限制。
版权所有者保留对未明确授权内容（特别是 Resources 目录中的资产）的全部权利。


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
│   │   │   ├── PlayerInfo/    # 玩家信息预制体
│   │   │   ├── Record/        # 记录相关预制体
│   │   │   └── Room/          # 房间相关预制体
│   │   ├── Scenes/            # 场景文件
│   │   │   ├── MainScene.unity
│   │   │   └── GameScene.unity
│   │   ├── Scripts/           # C# 脚本
│   │   │   ├── ChatServer/    # 聊天服务器相关脚本
│   │   │   ├── GameScene/     # 游戏场景脚本
│   │   │   ├── Network/       # 网络通信脚本
│   │   │   ├── PlayerInfo/    # 玩家信息脚本
│   │   │   ├── Room/          # 房间管理脚本
│   │   │   ├── UI/            # UI 相关脚本
│   │   │   └── *.cs           # 根级脚本文件
│   │   ├── Resources/         # 资源文件
│   │   │   ├── 3D/            # 3D 模型资源
│   │   │   ├── font/          # 字体资源
│   │   │   ├── image/         # 图片资源
│   │   │   └── Sound/         # 音效资源
│   │   ├── Shaders/           # 着色器文件
│   │   ├── TextMesh Pro/      # TextMesh Pro 资源
│   │   └── websocket-sharp/   # WebSocket 客户端库
│   └── ProjectSettings/       # Unity 项目设置
│
├── open_mahjong_server/       # Python 游戏服务器
│   └── server/                # 服务器核心代码
│       ├── server.py          # FastAPI 主服务器
│       ├── response.py        # 响应处理
│       ├── room_manager.py    # 房间管理
│       ├── room_validators.py # 房间验证
│       ├── config.py          # 配置文件
│       ├── local_config.py   # 本地配置
│       ├── requirements.txt  # Python 依赖
│       ├── start_server.bat  # 启动脚本
│       ├── database/          # 数据库相关
│       │   ├── db_manager.py  # 数据库管理器
│       │   └── db_design.md   # 数据库设计文档
│       ├── game_calculation/  # 游戏计算服务
│       │   ├── game_calculation_service.py
│       │   ├── gb_hepai_check.py
│       │   └── gb_tingpai_check.py
│       ├── game_gb/           # 国标麻将游戏逻辑
│       │   ├── game_state.py  # 游戏状态管理
│       │   ├── logic_handler.py # 逻辑处理
│       │   ├── action_check.py # 动作检查
│       │   ├── boardcast.py   # 广播处理
│       │   ├── game_record_manager.py # 游戏记录管理
│       │   └── record.md      # 记录文档
│       └── chat_server/       # 聊天服务器管理
│           └── chat_server.py # 聊天服务器接口
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
- **引擎**: Unity 2022.3 LTS
- **语言**: C#
- **网络**: WebSocket (websocket-sharp)
- **平台**: PC/Web/Android/iOS

### 游戏服务器 (open_mahjong_server、open_mahjong_chat_server)
- **框架**: FastAPI
- **语言**: Python 3.10+ Golang
- **网络**: WebSocket, HTTP
- **数据库**: PostgreSQL
- **部署**: supervisor

### Web 平台 (open_mahjong_web)
- **前端**: Vue3
- **后端**: Node.js
- **数据库**: PostgreSQL

### 4.交流
- **测试/开发群号**: 906497522
- **项目地址**: https://github.com/xelnagamiao/open_mahjong_unity


*最后更新：2025年12月13日 dev ver 0.0.27.0 *

