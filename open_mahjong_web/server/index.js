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
const io = socketIo(server, { // 创建socket.io实例 将http服务器作为参数传入
  cors: {
    origin: "http://localhost:5173", // Vue3开发服务器地址
    methods: ["GET", "POST"]
  }
});

// 中间件配置
app.use(cors()); // 使用cors配置允许跨域
app.use(express.json()); // 使用express.json解析JSON请求体
app.use(express.urlencoded({ extended: true })); // 使用express.urlencoded解析URL编码的请求体

// 数据库连接
const db = require('./config/database');

// 路由
const mahjongRoutes = require('./routes/mahjong'); // mahjongRoutes: 处理麻将游戏相关的 API（如创建房间、开始游戏等）
const playerRoutes = require('./routes/player'); // playerRoutes: 处理玩家数据查询相关的 API

app.use('/api/mahjong', mahjongRoutes); // 将mahjongRoutes挂载到/api/mahjong路径下
app.use('/api/player', playerRoutes); // 将playerRoutes挂载到/api/player路径下

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

// 开发模式下的前端路由处理
if (process.env.NODE_ENV === 'production') {
  // 生产模式：提供静态文件
  app.use(express.static(path.join(__dirname, '../client/dist')));
  
  // 所有非API路由都返回index.html
  app.get('*', (req, res) => {
    res.sendFile(path.join(__dirname, '../client/dist/index.html'));
  });
} else {
  // 开发模式：重定向到Vue开发服务器
  app.get('*', (req, res) => {
    res.redirect('http://localhost:5173' + req.url);
  });
}

// 错误处理中间件
app.use((err, req, res, next) => {
  console.error(err.stack);
  res.status(500).json({ 
    success: false, 
    message: '服务器内部错误' 
  });
});

const PORT = process.env.PORT || 3000;

server.listen(PORT, () => {
  console.log(`服务器运行在端口 ${PORT}`);
  console.log(`前端地址: http://localhost:${PORT}`);
  console.log(`API地址: http://localhost:${PORT}/api`);
}); 