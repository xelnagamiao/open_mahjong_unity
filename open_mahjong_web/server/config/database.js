const { Pool } = require('pg');
const config = require('./config');

// 创建 PostgreSQL 连接池
const pool = new Pool({
  host: config.db.host,
  user: config.db.user,
  password: config.db.password,
  database: config.db.database,
  port: config.db.port,
  max: 20, // 最大连接数
  idleTimeoutMillis: 30000,
  connectionTimeoutMillis: 2000,
});

// 测试数据库连接
async function testConnection() {
  try {
    const result = await pool.query('SELECT NOW()');
    console.log('PostgreSQL 数据库连接成功');
    console.log('数据库时间:', result.rows[0].now);
  } catch (error) {
    console.error('PostgreSQL 数据库连接失败:', error);
  }
}

// 初始化数据库连接
testConnection();

// 处理连接错误
pool.on('error', (err, client) => {
  console.error('Unexpected error on idle client', err);
  process.exit(-1);
});

module.exports = pool; 