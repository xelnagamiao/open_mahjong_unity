const express = require('express'); // 引入express Node.js Web 应用框架
const cors = require('cors'); // 引入cors 解决跨域问题 允许前端（如 Vue）在不同端口或域名下访问本服务器 API
const path = require('path'); // 引入path 处理文件路径
const http = require('http'); // 引入http 创建服务器
const socketIo = require('socket.io'); // 引入socket.io 实现WebSocket通信
require('dotenv').config(); // 引入dotenv 加载环境变量

// 加载配置（从环境变量读取，支持 .env 文件）
const config = require('./config/config');

const app = express(); // 创建express应用
const server = http.createServer(app); // 创建http服务器 将 Express 的 app 作为请求处理器传入

// Socket.IO 配置（从统一配置模块读取）
const io = socketIo(server, { // 创建socket.io实例 将http服务器作为参数传入
  cors: config.socket
});

// 中间件配置
// CORS 配置（从统一配置模块读取）
app.use(cors(config.cors)); // 使用cors配置允许跨域
app.use(express.json()); // 使用express.json解析JSON请求体
app.use(express.urlencoded({ extended: true })); // 使用express.urlencoded解析URL编码的请求体

// 数据库连接
const db = require('./config/database');

// 路由
const mahjongRoutes = require('./routes/mahjong'); // mahjongRoutes: 处理麻将游戏相关的 API（如创建房间、开始游戏等）
const playerRoutes = require('./routes/player'); // playerRoutes: 处理玩家数据查询相关的 API
const adminRoutes = require('./routes/admin');
const { createWindowLimiter } = require('./middleware/rateLimit');
const { ensureAuditTable } = require('./utils/audit');

// 数据库与多表聚合查询较贵：每 IP 每分钟约 24 次
const playerQueryLimiter = createWindowLimiter({
  windowMs: 60_000,
  max: 24,
  keyFn: (req) => `${req.ip || 'unknown'}:player`,
});
// 牌理 / 听牌 / 国标算分等转发 Python：每 IP 每分钟约 40 次
const mahjongCalcLimiter = createWindowLimiter({
  windowMs: 60_000,
  max: 40,
  keyFn: (req) => `${req.ip || 'unknown'}:mahjong`,
});

app.use('/api/mahjong', mahjongCalcLimiter, mahjongRoutes);
app.use('/api/player', playerQueryLimiter, playerRoutes);
app.use('/api/admin', adminRoutes);

if (config.admin.userIds.size === 0) {
  console.warn('警告: ADMIN_USER_IDS 未配置，管理后台将无法登录');
}

// WebSocket连接处理
io.on('connection', (socket) => {
  console.log('用户连接:', socket.id);
  
  socket.on('join-room', (roomId) => {
    socket.join(roomId);
    console.log(`用户 ${socket.id} 加入房间 ${roomId}`);
  });
  
  socket.on('leave-room', (roomId) => {
    socket.leave(roomId);
    console.log(`用户 ${socket.id} 离开房间 ${roomId}`);
  });
  
  socket.on('disconnect', () => {
    console.log('用户断开连接:', socket.id);
  });
});

// 路径处理：
// 生产环境前端静态资源由 Nginx 直接提供，Node 仅负责 /api 路由
// 开发环境将非 API 请求重定向到 Vite dev server
if (!config.isProduction) {
  app.get('*', (req, res, next) => {
    if (req.path.startsWith('/api')) {
      return next();
    }
    res.redirect('http://localhost:5173' + req.url);
  });
}

// 错误处理中间件
app.use((err, req, res, next) => {
  console.error('错误详情:', err.stack);
  
  // 生产环境不暴露详细错误信息
  res.status(err.status || 500).json({ 
    success: false, 
    message: config.isProduction ? '服务器内部错误' : err.message,
    ...(config.isProduction ? {} : { stack: err.stack })
  });
});

async function startServer() {
  try {
    await ensureAuditTable();
    console.log('管理审计表已就绪');
  } catch (err) {
    console.error('管理审计表初始化失败:', err);
  }
  server.listen(config.app.port, () => {
    console.log(`服务器运行在端口 ${config.app.port}`);
    console.log(`API地址: http://localhost:${config.app.port}/api`);
    console.log(`管理后台 API: http://localhost:${config.app.port}/api/admin`);
  });
}

startServer(); 