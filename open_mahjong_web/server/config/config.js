// 配置文件模块

const dbConfig = {
  host: process.env.DB_HOST || 'localhost',
  user: process.env.DB_USER || 'postgres',
  password: process.env.DB_PASSWORD || 'qwe123',
  database: process.env.DB_NAME || 'open_mahjong',
  port: parseInt(process.env.DB_PORT) || 5432
};

// 判断是否为调试模式（用于日志输出等）
const isDebug = process.env.DEBUG === 'true' || process.env.NODE_ENV !== 'production';

if (isDebug) {
  console.log('使用开发环境配置');
} else {
  console.log('使用生产环境配置');
}

console.log(`数据库连接: ${dbConfig.user}@${dbConfig.host}:${dbConfig.port}/${dbConfig.database}`);

module.exports = {
  db: dbConfig,
  isDebug: isDebug
};
