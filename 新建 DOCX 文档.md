# open_mahjong_unity 开发文档

## 项目简介

open_mahjong_unity 是一款基于 Unity/Python-FastAPI 的简易国标麻将平台，该平台的优势是免费、开源、支持 PC/安卓/iOS 三端互通；劣势是画面简陋、功能简单、没有什么开发者和用户。欢迎加入 QQ 群 906497522 参与讨论和协助测试。

## 1. 许可说明

本项目使用 GPL 许可证，但在文件根目录有独立的许可证文件标注的除外，这些除外可能包含：贡献者有条件的授权，限定商业使用的资源，仅授权了本项目的私有资源等。

## 2. 项目结构

本项目的文件结构如下：

```
open_mahjong_unity/
├── Assets/                   # Unity 资源文件
│   ├── Prefabs/              # 预制体
│   ├── Scenes/               # 场景文件
│   ├── Scripts/              # C# 脚本
│   ├── Resources/            # 资源文件
│   ├── Shaders/              # 着色器
│   └── websocket-sharp/      # WebSocket 客户端库
└── ProjectSettings/          # Unity 项目设置

open_mahjong_server/
└── server/                   # 服务器核心代码
    ├── server.py             # 主服务器文件
    ├── response.py           # 响应处理
    ├── room_manager.py       # 房间管理
    ├── room_validators.py    # 房间验证
    └── GB_Game/              # 国标麻将游戏逻辑

open_mahjong_web/
├── app.py                     # Flask 主应用
├── mahjong.py                 # 麻将解算核心
├── count_xt.py                # 统计工具
├── locustfile.py              # 压力测试
├── requirements.txt           # Python 依赖
├── templates/                 # HTML 模板
├── static/                    # 静态资源
└── README.md                  # 项目说明

open_mahjong_chatServer/       # 聊天服务器（待开发）
```

## 3. 技术栈

### 客户端 (open_mahjong_unity)
- **引擎**: Unity 2022.3 LTS
- **语言**: C#
- **网络**: WebSocket (websocket-sharp)

### 服务器端 (open_mahjong_server)
- **框架**: FastAPI
- **语言**: Python 3.10+
- **网络**: WebSocket, HTTP
- **数据库**: SQLite/MySQL
- **部署**: uvicorn

### Web 端 (open_mahjong_web)
- **框架**: Flask 3.0.3
- **语言**: Python 3.10+
- **数据库**: SQLAlchemy + PyMySQL/PostgreSQL
- **模板**: Jinja2
- **部署**: Gunicorn + Nginx + Supervisor

## 4. 功能模块

### 客户端功能
- 用户登录注册
- 房间创建和加入
- 实时麻将游戏
- 聊天功能
- 游戏记录查看

### 服务器功能
- 用户认证和管理
- 房间管理和匹配
- 国标麻将游戏逻辑
- WebSocket 实时通信
- 数据持久化

### Web 端功能
- 麻将手牌解算工具
- 用户信息查询
- 对局信息查看
- 游戏记录管理
- 后台管理界面

## 5. 开发环境搭建

### 客户端开发
1. 安装 Unity 2022.3 LTS
2. 克隆项目到本地
3. 用 Unity Hub 打开 `open_mahjong_unity` 文件夹
4. 等待 Unity 导入完成

```
其中，open_mahjong_unity为客户端代码，用以在unity当中编译可在windows/web/android/ios等平台运行的客户端实例
open_mahjong_server为服务器端代码，用以实现服务器端的逻辑功能，包括游戏逻辑、用户管理、数据存储等功能
open_mahjong_web为网页端代码，用以实现网页端程序的入口，提供麻将解算工具，查询用户、对局信息，游戏记录，后台管理等功能
open_mahjong_chatServer为聊天服务器代码，用以独立在客户端实现外载的聊天功能，和主服务器代码相分离
## 6. 部署说明

### 服务器部署
1. 安装 Python 3.10+
2. 配置虚拟环境
3. 安装依赖包
4. 使用 uvicorn 启动服务

### Web 端部署
1. 安装 Nginx
2. 配置 Gunicorn
3. 配置 Supervisor
4. 设置反向代理

详细部署步骤请参考 `open_mahjong_web/README.md`

## 7. 开发规范

### 代码规范
- 使用有意义的变量和函数名
- 添加适当的注释
- 遵循各语言的编码规范
- 提交前进行代码审查

### Git 工作流
- 使用 feature 分支开发新功能
- 提交信息要清晰描述变更
- 定期合并到主分支
- 保持代码库整洁

## 8. 测试

### 单元测试
- 为关键功能编写测试用例
- 使用 pytest 进行测试
- 保持测试覆盖率

### 集成测试
- 测试客户端与服务器通信
- 测试 Web 端功能
- 进行压力测试

## 9. 贡献指南

1. Fork 项目
2. 创建功能分支
3. 提交更改
4. 推送到分支
5. 创建 Pull Request

## 10. 联系方式

- QQ 群：906497522
- 项目地址：[GitHub/Gitee 链接]
- 问题反馈：[Issues 页面链接]

---

*最后更新：2024年*

