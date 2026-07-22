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

// ==================== 游戏计算服务器（Python FastAPI）====================
// 用于代理国标算分/拆解/听牌等纯计算接口
const calcServerConfig = {
  baseUrl: process.env.CALC_SERVER_URL || 'http://127.0.0.1:8081',
  timeoutMs: parseInt(process.env.CALC_SERVER_TIMEOUT_MS) || 8000,
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
    (isProduction ? productionFrontendUrl : true),
  methods: ['GET', 'POST'],
  credentials: true
};

// ==================== 管理后台配置 ====================
function requireEnv(name) {
  const value = process.env[name];
  if (value === undefined || value === null || !String(value).trim()) {
    throw new Error(`缺少必需环境变量: ${name}`);
  }
  return String(value).trim();
}

function parseAdminUserIds(raw) {
  if (!raw || !String(raw).trim()) {
    return new Set();
  }
  return new Set(
    String(raw)
      .split(',')
      .map((s) => parseInt(s.trim(), 10))
      .filter((n) => !Number.isNaN(n))
  );
}

function loadAdminConfig() {
  const userIds = parseAdminUserIds(requireEnv('ADMIN_USER_IDS'));
  if (userIds.size === 0) {
    throw new Error('ADMIN_USER_IDS 未包含有效的用户 ID');
  }

  const jwtExpiresSec = parseInt(requireEnv('ADMIN_JWT_EXPIRES_SEC'), 10);
  if (Number.isNaN(jwtExpiresSec) || jwtExpiresSec <= 0) {
    throw new Error('ADMIN_JWT_EXPIRES_SEC 必须是正整数');
  }

  return {
    userIds,
    jwtSecret: requireEnv('ADMIN_JWT_SECRET'),
    jwtExpiresSec,
  };
}

const adminConfig = loadAdminConfig();

// 比赛管理后台：复用超管 JWT 密钥，通过 aud=event-admin 区分令牌
function loadEventAdminConfig() {
  const jwtExpiresSec = parseInt(
    process.env.EVENT_ADMIN_JWT_EXPIRES_SEC || String(adminConfig.jwtExpiresSec),
    10
  );
  if (Number.isNaN(jwtExpiresSec) || jwtExpiresSec <= 0) {
    throw new Error('EVENT_ADMIN_JWT_EXPIRES_SEC 必须是正整数');
  }
  return {
    jwtSecret: (process.env.EVENT_ADMIN_JWT_SECRET || adminConfig.jwtSecret).trim(),
    jwtExpiresSec,
    audience: 'event-admin',
  };
}

const eventAdminConfig = loadEventAdminConfig();

// 普通玩家 Web 登录：复用超管密钥，通过 aud=player 区分令牌
function loadPlayerAuthConfig() {
  const jwtExpiresSec = parseInt(
    process.env.PLAYER_JWT_EXPIRES_SEC || String(adminConfig.jwtExpiresSec),
    10
  );
  if (Number.isNaN(jwtExpiresSec) || jwtExpiresSec <= 0) {
    throw new Error('PLAYER_JWT_EXPIRES_SEC 必须是正整数');
  }
  return {
    jwtSecret: (process.env.PLAYER_JWT_SECRET || adminConfig.jwtSecret).trim(),
    jwtExpiresSec,
    audience: 'player',
  };
}

const playerAuthConfig = loadPlayerAuthConfig();

function loadBotApiConfig() {
  const jwtSecret = process.env.BOT_API_JWT_SECRET;
  if (!jwtSecret || !String(jwtSecret).trim()) {
    throw new Error('缺少必需环境变量: BOT_API_JWT_SECRET');
  }
  return {
    jwtSecret: String(jwtSecret).trim(),
  };
}

const botApiConfig = loadBotApiConfig();

function loadSmtpConfig() {
  const host = (process.env.SMTP_HOST || 'smtp-relay.brevo.com').trim();
  const port = parseInt(process.env.SMTP_PORT || '587', 10);
  const user = (process.env.SMTP_USER || '').trim();
  const pass = (process.env.SMTP_PASS || '').trim();
  const fromEmail = (process.env.SMTP_FROM_EMAIL || user || '').trim();
  const fromName = (process.env.SMTP_FROM_NAME || 'salasasa').trim();
  const enabled = !!(user && pass && fromEmail);
  return {
    enabled,
    host,
    port: Number.isNaN(port) ? 587 : port,
    secure: process.env.SMTP_SECURE === 'true' || port === 465,
    user,
    pass,
    fromEmail,
    fromName,
  };
}

const smtpConfig = loadSmtpConfig();

const devFrontendUrl = 'http://localhost:5173';

function printDebugConfig() {
  if (!appConfig.isDebug) return;

  const apiBase = `http://localhost:${appConfig.port}`;
  const frontendUrl = isProduction ? productionFrontendUrl : devFrontendUrl;

  console.log('');
  console.log('=== 调试模式配置 ===');
  console.log(`前端地址: ${frontendUrl}/`);
  console.log(`API 地址: ${apiBase}/api`);
  console.log(`管理后台: ${frontendUrl}/admin/login`);
  console.log(`管理后台 API: ${apiBase}/api/admin`);
  console.log(`比赛管理后台: ${frontendUrl}/event-admin/login`);
  console.log(`比赛管理 API: ${apiBase}/api/event-admin`);
  console.log(`计算服务器: ${calcServerConfig.baseUrl}`);
  console.log(`管理员用户 ID: ${[...adminConfig.userIds].join(', ')}`);
  console.log(`SMTP 邮件: ${smtpConfig.enabled ? `已启用 (${smtpConfig.fromEmail})` : '未配置'}`);
}

// ==================== 日志输出 ====================
console.log(isProduction ? '=== 生产环境配置 ===' : '=== 开发环境配置 ===');
console.log(
  `环境: ${appConfig.nodeEnv}${appConfig.isDebug ? ' · 调试模式: 开启' : ' · 调试模式: 关闭'}`
);
console.log(`端口: ${appConfig.port}`);
console.log(`数据库: ${dbConfig.user}@${dbConfig.host}:${dbConfig.port}/${dbConfig.database}`);
if (isProduction) {
  console.log(`前端地址: ${productionFrontendUrl}`);
}
console.log(`管理后台管理员数量: ${adminConfig.userIds.size}`);

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

  // 计算服务器配置
  calcServer: calcServerConfig,

  // 管理后台
  admin: adminConfig,

  // 比赛管理后台（赛事主管理员 / 赛事子管理员）
  eventAdmin: eventAdminConfig,

  // 普通玩家 Web 登录
  playerAuth: playerAuthConfig,

  // 邮件（Brevo SMTP 等）
  smtp: smtpConfig,

  // QQ 机器人等第三方 Bot API
  botApi: botApiConfig,
  
  // 便捷访问
  isProduction: isProduction,
  isDebug: appConfig.isDebug,
  frontendUrl: isProduction ? productionFrontendUrl : devFrontendUrl,

  printDebugConfig,
};
