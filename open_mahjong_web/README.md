# Mahjong.fit - 麻将分析网站

基于 Node.js 和 Vue3 的现代化麻将分析工具，提供听牌判断、国标麻将和立直麻将牌型解算功能。

## 功能特色

- 🎯 **听牌待牌判断** - 分析手牌是否听牌，以及听牌手牌的待牌
- 🏆 **国标麻将牌型解算** - 根据手牌、副露、花牌、和牌方式计算和牌构成与点数
- ⭐ **立直麻将牌型解算** - 根据手牌、副露、宝牌、和牌方式计算和牌构成与点数
- 👤 **用户系统** - 支持用户注册、登录和历史记录
- 📱 **响应式设计** - 支持桌面端和移动端访问
- 🎨 **现代化UI** - 基于 Element Plus 的美观界面

## 技术栈

### 后端
- **Node.js** - 运行环境
- **Express** - Web框架
- **MySQL** - 数据库
- **Socket.IO** - 实时通信
- **JWT** - 用户认证
- **bcryptjs** - 密码加密

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
- MySQL 8.0+
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

1. 创建MySQL数据库：
```sql
CREATE DATABASE database_mj;
```

2. 配置数据库连接（创建 `.env` 文件）：
```env
DB_HOST=localhost
DB_USER=root
DB_PASSWORD=your_password
DB_NAME=database_mj
DB_PORT=3306
JWT_SECRET=your-secret-key
PORT=3000
```

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

### 认证接口
- `POST /api/auth/register` - 用户注册
- `POST /api/auth/login` - 用户登录
- `GET /api/auth/profile` - 获取用户信息

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