## 数据库类型
PostgreSQL

## 数据库连接配置
- 主机: localhost
- 端口: 5432
- 数据库名: open_mahjong
- 默认用户: postgres

## 数据库表结构

### users 表
用户信息表，用于存储玩家账号信息。

| 字段名 | 类型 | 约束 | 说明 |
|--------|------|------|------|
| id | SERIAL | PRIMARY KEY | 自增主键 |
| username | VARCHAR(255) | UNIQUE NOT NULL | 用户名，唯一 |
| password | VARCHAR(255) | NOT NULL | 密码 |
| created_at | TIMESTAMP | DEFAULT CURRENT_TIMESTAMP | 创建时间 |
