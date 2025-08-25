const express = require('express');
const cors = require('cors');
const path = require('path');
const http = require('http');
const socketIo = require('socket.io');
require('dotenv').config();

const app = express();
const server = http.createServer(app);
const io = socketIo(server, {
  cors: {
    origin: "http://localhost:5173", // Vue3开发服务器地址
    methods: ["GET", "POST"]
  }
});

// 中间件配置
app.use(cors());
app.use(express.json());
app.use(express.urlencoded({ extended: true }));

// 数据库连接
const db = require('./config/database');

// 路由
const mahjongRoutes = require('./routes/mahjong');
const authRoutes = require('./routes/auth');

app.use('/api/mahjong', mahjongRoutes);
app.use('/api/auth', authRoutes);

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