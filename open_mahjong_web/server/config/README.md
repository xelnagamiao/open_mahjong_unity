# 配置管理说明

## 概述

所有配置统一在 `config.js` 文件中管理，通过环境变量进行配置。

## 配置方式

### 方式一：使用 .env 文件（推荐开发环境）

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

# 前端和跨域配置
FRONTEND_URL=https://salasasa.cn
CORS_ORIGIN=https://salasasa.cn
SOCKET_ORIGIN=https://salasasa.cn
```

### 方式二：系统环境变量（推荐生产环境）

通过 Supervisor、PM2、systemd 等进程管理器设置环境变量。

## 配置项说明

### NODE_ENV（重要）

**含义**：标识当前运行环境，影响应用行为

- `production`：生产环境
  - 提供静态文件服务（从 `client/dist` 目录）
  - 不暴露详细错误堆栈信息
  - 使用生产环境的 CORS 配置
  - 使用生产环境的前端地址

- `development` 或其他值：开发环境
  - 重定向到开发服务器（localhost:5173）
  - 显示详细错误信息便于调试
  - 使用开发环境的 CORS 配置（允许所有源）

### 数据库配置

| 环境变量 | 说明 | 默认值 |
|---------|------|--------|
| DB_HOST | 数据库主机 | localhost |
| DB_USER | 数据库用户 | postgres |
| DB_PASSWORD | 数据库密码 | qwe123 |
| DB_NAME | 数据库名称 | open_mahjong |
| DB_PORT | 数据库端口 | 5432 |

### 应用配置

| 环境变量 | 说明 | 默认值 |
|---------|------|--------|
| PORT | 服务器端口 | 3000 |
| DEBUG | 调试模式 | false（生产环境）或 true（开发环境） |

### 前端和跨域配置

| 环境变量 | 说明 | 默认值 |
|---------|------|--------|
| FRONTEND_URL | 前端访问地址 | https://salasasa.cn（生产）或 http://localhost:5173（开发） |
| CORS_ORIGIN | CORS 允许的源 | 使用 FRONTEND_URL |
| SOCKET_ORIGIN | Socket.IO 允许的源 | 使用 FRONTEND_URL |

## 配置优先级

环境变量 > 默认值

## 使用示例

在代码中使用配置：

```javascript
const config = require('./config/config');

// 访问数据库配置
const dbHost = config.db.host;

// 访问应用配置
const port = config.app.port;
const isProduction = config.isProduction;

// 访问 CORS 配置
app.use(cors(config.cors));

// 访问 Socket.IO 配置
const io = socketIo(server, { cors: config.socket });
```

