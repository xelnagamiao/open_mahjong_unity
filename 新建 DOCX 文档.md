# open_mahjong_unity 开发文档

## 概述

欢迎使用 open_mahjong_unity！

open_mahjong_unity 是一款开源自由部署的麻将平台，支持 PC/Web/移动端三端互通。该平台采用现代化的技术栈，提供完整的麻将游戏体验和开发工具。

您可以通过以下导航栏选择您需要查阅的模块：
- **如何部署平台** - 快速部署和运行指南
- **如何开发和修改平台** - 开发环境搭建和代码结构说明
- **反馈问题与参与测试** - 社区参与和问题反馈

## 项目简介

open_mahjong_unity 是一款基于 Unity/Node.js/Vue3 的现代化麻将平台，具有以下特点：

### 优势
- **免费开源** - 完全开源，可自由部署和修改
- **跨平台支持** - 支持 PC/Web/Android/iOS 多端互通
- **现代化技术栈** - 使用最新的开发技术和框架
- **模块化设计** - 清晰的架构，便于维护和扩展
- **实时对战** - 基于 WebSocket 的实时多人游戏

### 技术特色
- **前端** - Vue3 + Element Plus + Vite
- **后端** - Node.js + Express + Socket.IO
- **游戏客户端** - Unity 2022.3 LTS
- **数据库** - MySQL + Redis
- **部署** - Docker + Nginx

## 1. 许可说明

本项目使用 GPL 许可证，但在文件根目录有独立的许可证文件标注的除外，这些除外可能包含：贡献者有条件的授权，限定商业使用的资源，仅授权了本项目的私有资源等。

## 2. 项目结构

本项目的文件结构如下：

```
open_mahjong_unity/
├── open_mahjong_unity/        # Unity 游戏客户端
│   ├── Assets/                # Unity 资源文件
│   │   ├── Prefabs/           # 预制体
│   │   ├── Scenes/            # 场景文件
│   │   ├── Scripts/           # C# 脚本
│   │   ├── Resources/         # 资源文件
│   │   └── websocket-sharp/   # WebSocket 客户端库
│   └── ProjectSettings/       # Unity 项目设置
│
├── open_mahjong_server/       # Python 游戏服务器
│   └── server/                # 服务器核心代码
│       ├── server.py          # FastAPI 主服务器
│       ├── response.py        # 响应处理
│       ├── room_manager.py    # 房间管理
│       ├── room_validators.py # 房间验证
│       └── GB_Game/           # 国标麻将游戏逻辑
│
├── open_mahjong_web/          # Node.js/Vue3 Web 平台
│   ├── server/                # Node.js 后端
│   │   ├── index.js           # Express 主服务器
│   │   ├── config/            # 配置文件
│   │   └── routes/            # API 路由
│   ├── client/                # Vue3 前端
│   │   ├── src/               # 源代码
│   │   │   ├── views/         # 页面组件
│   │   │   ├── components/    # 通用组件
│   │   │   ├── router/        # 路由配置
│   │   │   └── stores/        # 状态管理
│   │   ├── public/            # 静态资源
│   │   └── package.json       # 前端依赖
│   ├── package.json           # 后端依赖
│   └── start.bat              # 启动脚本
│
└── open_mahjong_chatServer/   # 聊天服务器
    ├── server/                # 聊天服务器核心代码
    ├── config/                # 配置文件
    └── requirements.txt       # Python 依赖
```

## 3. 技术栈

### 游戏客户端 (open_mahjong_unity)
- **引擎**: Unity 2022.3 LTS
- **语言**: C#
- **网络**: WebSocket (websocket-sharp)
- **平台**: PC/Web/Android/iOS

### 游戏服务器 (open_mahjong_server)
- **框架**: FastAPI
- **语言**: Python 3.10+
- **网络**: WebSocket, HTTP
- **数据库**: SQLite/MySQL
- **部署**: uvicorn

### Web 平台 (open_mahjong_web)
- **前端**: Vue3 + Element Plus + Vite
- **后端**: Node.js + Express + Socket.IO
- **数据库**: MySQL + Redis
- **认证**: JWT + bcryptjs
- **部署**: Docker + Nginx

## 4. 功能模块

### 游戏客户端功能
- 用户登录注册
- 房间创建和加入
- 实时麻将游戏
- 聊天功能
- 游戏记录查看

### 游戏服务器功能
- 用户认证和管理
- 房间管理和匹配
- 国标麻将游戏逻辑
- WebSocket 实时通信
- 数据持久化

### Web 平台功能
- **麻将分析工具**
  - 听牌待牌判断
  - 国标麻将牌型解算
  - 立直麻将牌型解算
- **用户管理**
  - 用户注册登录
  - 个人信息管理
  - 玩家数据统计
- **游戏集成**
  - Unity 游戏嵌入
  - 开发手册查看
  - GitHub 项目链接

## 5. 如何部署平台

### 快速部署指南

#### Web 平台部署
1. **环境要求**
   - Node.js 16+ 
   - MySQL 8.0+
   - Redis 6.0+

2. **一键启动**
   ```bash
   cd open_mahjong_web
   ./start.bat  # Windows
   # 或
   npm run dev  # 开发模式
   npm start    # 生产模式
   ```

3. **访问地址**
   - 前端: http://localhost:5173
   - 后端: http://localhost:3000
   - API: http://localhost:3000/api

#### 游戏服务器部署
1. **环境要求**
   - Python 3.10+
   - MySQL/SQLite

2. **启动服务**
   ```bash
   cd open_mahjong_server
   pip install -r requirements.txt
   python server/server.py
   ```

#### Unity 游戏部署
1. **Web 版本**
   - 将 Unity 构建文件放入 `open_mahjong_web/client/public/unity-game/`
   - 通过 Web 平台访问游戏

2. **移动端版本**
   - 使用 Unity 构建 Android/iOS 版本
   - 配置服务器连接地址

## 6. 如何开发和修改平台

### 开发环境搭建

#### Web 平台开发
1. **环境准备**
   ```bash
   # 安装 Node.js 16+
   # 安装 MySQL 8.0+
   # 安装 Redis 6.0+
   ```

2. **项目启动**
   ```bash
   cd open_mahjong_web
   npm install          # 安装依赖
   npm run dev          # 启动开发服务器
   ```

3. **项目结构**
   - `client/` - Vue3 前端代码
   - `server/` - Node.js 后端代码
   - `package.json` - 项目配置

#### Unity 游戏开发
1. **环境准备**
   - 安装 Unity 2022.3 LTS
   - 安装 Unity Hub

2. **项目打开**
   ```bash
   # 使用 Unity Hub 打开
   open_mahjong_unity/open_mahjong_unity/
   ```

3. **开发要点**
   - 游戏逻辑在 `Assets/Scripts/` 目录
   - WebSocket 通信使用 `websocket-sharp` 库
   - 支持多平台构建

#### 游戏服务器开发
1. **环境准备**
   ```bash
   # 安装 Python 3.10+
   pip install -r requirements.txt
   ```

2. **开发启动**
   ```bash
   cd open_mahjong_server
   python server/server.py
   ```

### 代码结构说明

#### Web 平台架构
- **前端**: Vue3 + Element Plus + Vite
- **后端**: Express + Socket.IO + MySQL
- **认证**: JWT + bcryptjs
- **路由**: Vue Router 统一管理

#### 游戏服务器架构
- **框架**: FastAPI + WebSocket
- **游戏逻辑**: 国标麻将规则实现
- **房间管理**: 多房间并发支持
- **数据存储**: SQLite/MySQL 可选

## 7. 反馈问题与参与测试

### 社区参与

#### QQ 群交流
- **群号**: 906497522
- **用途**: 技术讨论、问题反馈、测试交流
- **欢迎**: 开发者、测试者、用户参与

#### GitHub 协作
- **项目地址**: https://github.com/xelnagamiao/open_mahjong_unity
- **问题反馈**: 通过 Issues 提交 Bug 报告
- **功能建议**: 通过 Issues 提出新功能想法
- **代码贡献**: 通过 Pull Request 提交代码

### 测试参与

#### 功能测试
- **Web 平台测试**: 麻将分析工具、用户管理功能
- **游戏测试**: Unity 客户端功能、多人对战
- **服务器测试**: 稳定性、性能测试

#### 测试环境
- **开发环境**: 本地部署测试
- **测试环境**: 在线测试服务器
- **生产环境**: 正式部署验证

### 问题反馈

#### Bug 报告
1. 描述问题现象
2. 提供复现步骤
3. 附上错误日志
4. 说明环境信息

#### 功能建议
1. 描述需求场景
2. 说明预期效果
3. 提供实现思路
4. 评估优先级

### 开发规范

#### 代码规范
- 使用有意义的变量和函数名
- 添加适当的注释
- 遵循各语言的编码规范
- 提交前进行代码审查

#### Git 工作流
- 使用 feature 分支开发新功能
- 提交信息要清晰描述变更
- 定期合并到主分支
- 保持代码库整洁

### 测试指南

#### 单元测试
- 为关键功能编写测试用例
- 使用 pytest 进行测试
- 保持测试覆盖率

#### 集成测试
- 测试客户端与服务器通信
- 测试 Web 端功能
- 进行压力测试

---

## 快速导航

- **部署平台**: 查看第5节 - 如何部署平台
- **开发修改**: 查看第6节 - 如何开发和修改平台  
- **问题反馈**: 查看第7节 - 反馈问题与参与测试
- **技术文档**: 查看各模块的 README.md 文件

---

*最后更新：2024年12月*

