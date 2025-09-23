const mysql = require('mysql2/promise');

const dbConfig = {
  host: process.env.DB_HOST || 'localhost',
  user: process.env.DB_USER || 'root',
  password: process.env.DB_PASSWORD || 'qwe123',
  database: process.env.DB_NAME || 'database_mj',
  port: process.env.DB_PORT || 3306,
  waitForConnections: true,
  connectionLimit: 10,
  queueLimit: 0
};

// 创建连接池
const pool = mysql.createPool(dbConfig);

// 测试数据库连接
async function testConnection() {
  try {
    const connection = await pool.getConnection();
    console.log('数据库连接成功');
    connection.release();
  } catch (error) {
    console.error('数据库连接失败:', error);
  }
}

// 初始化数据库表
async function initDatabase() {
  try {
    const connection = await pool.getConnection();
    
    // 创建麻将结果表
    const createTableSQL = `
      CREATE TABLE IF NOT EXISTS mahjong_results (
        id INT AUTO_INCREMENT PRIMARY KEY,
        mj_input VARCHAR(200),
        mj_output TEXT,
        is_valid BOOLEAN NOT NULL,
        created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP
      )
    `;
    
    await connection.execute(createTableSQL);
    console.log('数据库表初始化完成');
    connection.release();
  } catch (error) {
    console.error('数据库表初始化失败:', error);
  }
}

// 初始化
testConnection();
initDatabase();

module.exports = pool; 