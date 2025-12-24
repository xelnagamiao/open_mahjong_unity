# salasasa.cn - 麻将分析网站

基于 Node.js 和 Vue3 的现代化麻将分析工具，提供听牌判断、国标麻将和立直麻将牌型解算功能。

## 功能特色

- 🎯 **听牌待牌判断** - 分析手牌是否听牌，以及听牌手牌的待牌
- 🏆 **国标麻将牌型解算** - 根据手牌、副露、花牌、和牌方式计算和牌构成与点数
- ⭐ **立直麻将牌型解算** - 根据手牌、副露、宝牌、和牌方式计算和牌构成与点数
- 📊 **玩家数据统计** - 查询玩家统计数据和对局记录
- 📱 **响应式设计** - 支持桌面端和移动端访问
- 🎨 **现代化UI** - 基于 Element Plus 的美观界面

## 技术栈

### 后端
- **Node.js** - 运行环境
- **Express** - Web框架
- **PostgreSQL** - 数据库
- **Socket.IO** - 实时通信

### 前端
- **Vue 3** - 前端框架
- **Vue Router** - 路由管理
- **Pinia** - 状态管理
- **Element Plus** - UI组件库
- **Axios** - HTTP客户端
- **Vite** - 构建工具

## 快速开始

### 环境要求
- Node.js 16+
- PostgreSQL 12+
- npm 或 yarn

### 安装依赖

```bash
# 安装后端依赖
npm install

# 安装前端依赖
cd client
npm install
```

### 数据库配置

1. 创建PostgreSQL数据库：
```sql
CREATE DATABASE open_mahjong;
```

2. 配置数据库连接（推荐使用环境变量）：
   
   在项目根目录创建 `.env` 文件：
```env
# 数据库配置
DB_HOST=localhost
DB_USER=postgres
DB_PASSWORD=your_password
DB_NAME=open_mahjong
DB_PORT=5432

# 应用配置
NODE_ENV=production
DEBUG=false
PORT=3000
```

   或者通过系统环境变量设置（适用于 Supervisor、PM2、systemd 等进程管理器）

3. 默认值说明：
   - 如果未设置环境变量，将使用以下默认值：
     - `DB_HOST=localhost`
     - `DB_USER=postgres`
     - `DB_PASSWORD=qwe123`
     - `DB_NAME=open_mahjong`
     - `DB_PORT=5432`
   - **生产环境请务必设置正确的环境变量，不要使用默认值**

### 启动项目

```bash
# 开发模式（同时启动前后端）
npm run dev

# 或者分别启动
npm run server  # 启动后端服务器
npm run client  # 启动前端开发服务器
```

### 生产部署

```bash
# 构建前端
npm run build

# 启动生产服务器
npm start
```

## 项目结构

```
open_mahjong_web_now/
├── server/                 # 后端代码
│   ├── config/            # 配置文件
│   ├── routes/            # 路由文件
│   └── index.js           # 服务器入口
├── client/                # 前端代码
│   ├── src/
│   │   ├── components/    # 组件
│   │   ├── views/         # 页面
│   │   ├── stores/        # 状态管理
│   │   ├── router/        # 路由配置
│   │   └── main.js        # 应用入口
│   └── package.json
├── package.json           # 项目配置
└── README.md
```

## API接口

### 玩家数据接口
- `GET /api/player/info/:userid` - 获取玩家信息和统计数据
- `GET /api/player/records/:userid` - 获取玩家对局记录

### 麻将分析接口
- `POST /api/mahjong/count-hand` - 听牌判断
- `POST /api/mahjong/count-chinese` - 国标麻将解算
- `POST /api/mahjong/count-riichi` - 立直麻将解算
- `GET /api/mahjong/history` - 获取历史记录

## 开发说明

### 添加新的麻将规则
1. 在 `server/routes/mahjong.js` 中添加新的路由
2. 实现相应的计算逻辑
3. 在前端添加对应的页面组件

### 自定义样式
- 全局样式：`client/src/style.css`
- 组件样式：使用 `<style scoped>` 在组件内定义

### 数据库迁移
数据库表会在应用启动时自动创建，如需修改表结构，请更新 `server/config/database.js` 中的初始化代码。

## 贡献指南

1. Fork 项目
2. 创建功能分支 (`git checkout -b feature/AmazingFeature`)
3. 提交更改 (`git commit -m 'Add some AmazingFeature'`)
4. 推送到分支 (`git push origin feature/AmazingFeature`)
5. 打开 Pull Request

## 许可证

本项目采用 MIT 许可证 - 查看 [LICENSE](LICENSE) 文件了解详情。

## 联系方式

- QQ群：432372422
- 管理员：Q1448826180
- 项目地址：[GitHub Repository]

---

© 2024 立直麻雀研习社. 保留所有权利. 