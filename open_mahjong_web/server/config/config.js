// 统一配置管理模块
// 所有配置项都在此文件中定义和管理
// 配置优先级：环境变量 > 默认值

/**
 * NODE_ENV 说明：
 * - 'production': 生产环境，用于正式部署
 *   - 提供静态文件服务
 *   - 不暴露详细错误信息
 *   - 使用生产环境的 CORS 配置
 * - 'development' 或其他: 开发环境
 *   - 重定向到开发服务器
 *   - 显示详细错误信息
 *   - 使用开发环境的 CORS 配置
 */
const NODE_ENV = process.env.NODE_ENV || 'development';
const isProduction = NODE_ENV === 'production';

// ==================== 数据库配置 ====================
const dbConfig = {
  host: process.env.DB_HOST || 'localhost',
  user: process.env.DB_USER || 'postgres',
  password: process.env.DB_PASSWORD || 'qwe123',
  database: process.env.DB_NAME || 'open_mahjong',
  port: parseInt(process.env.DB_PORT) || 5432
};

// ==================== 应用配置 ====================
const appConfig = {
  port: parseInt(process.env.PORT) || 3000,
  nodeEnv: NODE_ENV,
  isProduction: isProduction,
  isDebug: process.env.DEBUG === 'true' || !isProduction
};

// ==================== 前端和跨域配置 ====================
// 生产环境的前端地址
const productionFrontendUrl = process.env.FRONTEND_URL || 'https://salasasa.cn';

// CORS 配置
const corsConfig = {
  origin: process.env.CORS_ORIGIN || 
    (isProduction ? productionFrontendUrl : '*'),
  credentials: true
};

// Socket.IO CORS 配置
const socketConfig = {
  origin: process.env.SOCKET_ORIGIN || 
    (isProduction ? productionFrontendUrl : 'http://localhost:5173'),
  methods: ['GET', 'POST'],
  credentials: true
};

// ==================== 日志输出 ====================
if (appConfig.isDebug) {
  console.log('=== 开发环境配置 ===');
} else {
  console.log('=== 生产环境配置 ===');
}

console.log(`环境: ${appConfig.nodeEnv}`);
console.log(`端口: ${appConfig.port}`);
console.log(`数据库: ${dbConfig.user}@${dbConfig.host}:${dbConfig.port}/${dbConfig.database}`);
if (isProduction) {
  console.log(`前端地址: ${productionFrontendUrl}`);
}

// ==================== 导出配置 ====================
module.exports = {
  // 数据库配置
  db: dbConfig,
  
  // 应用配置
  app: appConfig,
  
  // CORS 配置
  cors: corsConfig,
  
  // Socket.IO 配置
  socket: socketConfig,
  
  // 便捷访问
  isProduction: isProduction,
  isDebug: appConfig.isDebug,
  frontendUrl: isProduction ? productionFrontendUrl : 'http://localhost:5173'
};
