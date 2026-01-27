## 数据库类型
PostgreSQL

## 数据库连接配置
- 主机: localhost
- 端口: 5432
- 数据库名: open_mahjong
- 默认用户: postgres

## 用户ID序列设计

系统使用两个独立的序列来管理用户ID：

1. **tourist_user_id_seq**：游客用户序列
   - 起始值：9000000
   - 最大值：9900000
   - 循环：是（达到最大值后重新开始）
   - 用途：临时游客账户，可被删除和重用

2. **registered_user_id_seq**：注册用户序列
   - 起始值：10000001
   - 递增：1
   - 无上限
   - 用途：永久注册账户，user_id 永不重用

> **注意**：users 表的 user_id 默认使用 `registered_user_id_seq`。游客账户在创建时手动指定使用 `tourist_user_id_seq` 的 ID。

## 数据库表结构

### users 用户信息表 存储玩家账号信息
| 字段名 | 类型 | 约束 | 说明 |
|--------|------|------|------|
| user_id | BIGSERIAL | PRIMARY KEY | 用户唯一标识，注册用户从 10000001 开始自增，游客用户使用 9000000-9900000 范围 |
| username | VARCHAR(255) | UNIQUE NOT NULL | 用户名，唯一 |
| password | VARCHAR(255) | NOT NULL | 密码哈希值（格式：salt:hash，使用 PBKDF2+SHA256，100000 次迭代）。游客账户密码为空字符串 |
| is_tourist | BOOLEAN | DEFAULT FALSE | 是否为游客账户（游客账户可被删除） |
| created_at | TIMESTAMP | DEFAULT CURRENT_TIMESTAMP | 创建时间 |

### user_settings 用户设置表
存储用户的个性化设置信息（称号、头像、角色、音色），每个用户对应一条记录。

| 字段名 | 类型 | 约束 | 说明 |
|--------|------|------|------|
| user_id | BIGINT | PRIMARY KEY, REFERENCES users(user_id) ON DELETE CASCADE | 用户ID，外键关联 users |
| title_id | INT | DEFAULT 1 | 称号ID（默认值为1） |
| profile_image_id | INT | DEFAULT 1 | 使用的头像ID（默认值为1） |
| character_id | INT | DEFAULT 1 | 选择的角色ID（默认值为1） |
| voice_id | INT | DEFAULT 1 | 选择的音色ID（默认值为1） |
| created_at | TIMESTAMP | DEFAULT CURRENT_TIMESTAMP | 创建时间 |
| updated_at | TIMESTAMP | DEFAULT CURRENT_TIMESTAMP | 最近更新时间 |

### user_config 游戏配置表
存储用户的游戏配置信息（音量等），每个用户对应一条记录。

| 字段名 | 类型 | 约束 | 说明 |
|--------|------|------|------|
| user_id | BIGINT | PRIMARY KEY, REFERENCES users(user_id) ON DELETE CASCADE | 用户ID，外键关联 users |
| volume | INT | NOT NULL DEFAULT 100 | 音量设置（0-100） |
| created_at | TIMESTAMP | DEFAULT CURRENT_TIMESTAMP | 创建时间 |
| updated_at | TIMESTAMP | DEFAULT CURRENT_TIMESTAMP | 最近更新时间 |

### game_records 存储对局记录的牌谱
| 字段名 | 类型 | 约束 | 说明 |
|--------|------|------|------|
| game_id | BIGSERIAL | PRIMARY KEY | 对局唯一标识，自增 |
| record | JSONB | NOT NULL | 完整的牌谱记录（包含 game_title 和 game_round） |
| created_at | TIMESTAMP | DEFAULT CURRENT_TIMESTAMP | 创建时间 |

### game_player_records 牌谱记录表，用于存储玩家对局记录
| 字段名 | 类型 | 约束 | 说明 |
|--------|------|------|------|
| game_id | BIGINT | NOT NULL, REFERENCES game_records(game_id) ON DELETE CASCADE | 对局ID，外键关联 game_records |
| user_id | BIGINT | NOT NULL, REFERENCES users(user_id) ON DELETE CASCADE | 用户ID，外键关联 users |
| username | VARCHAR(255) | NOT NULL | 玩家用户名（对局时的用户名） |
| score | INT | NOT NULL | 玩家最终分数（可能为负数） |
| rank | INT | NOT NULL CHECK (rank >= 1 AND rank <= 4) | 最终排名（1=一位，2=二位，3=三位，4=四位） |
| rule | VARCHAR(10) | NOT NULL | 规则类型（guobiao=国标，riichi=立直） |
| title_used | INT | NULL | 使用的称号ID（可为空） |
| character_used | INT | NULL | 使用的角色ID（可为空） |
| profile_used | INT | NULL | 使用的头像ID（可为空） |
| voice_used | INT | NULL | 使用的音色ID（可为空） |
| PRIMARY KEY | (game_id, user_id) | 复合主键 | 每个游戏每个玩家一条记录 |

> **级联删除说明**：
> - 删除 `users` 表中的用户记录时，会级联删除 `game_player_records` 中该用户的所有记录
> - 删除 `game_records` 表中的牌谱记录时，会级联删除 `game_player_records` 中该游戏的所有玩家记录
> - 不会删除牌谱数据

### 规则特定统计表

不同规则的统计表设计已移至各自文件夹下的 `db_design.md` 文件：

- **国标麻将**：`database/guobiao/db_design.md`
  - `guobiao_history_stats` - 基础统计数据
  - `guobiao_fan_stats` - 番种统计数据

- **立直麻将**：`database/riichi/db_design.md`
  - `riichi_history_stats` - 基础统计数据
  - `riichi_fan_stats` - 番种统计数据（待定义）

### 删除 users 表中的用户记录时，会级联删除以下表中的数据：

1. **user_settings** - 该用户的设置记录
2. **user_config** - 该用户的配置记录
3. **guobiao_history_stats** - 该用户的国标麻将基础统计数据
4. **guobiao_fan_stats** - 该用户的国标麻将番种统计数据
5. **riichi_history_stats** - 该用户的立直麻将基础统计数据
6. **riichi_fan_stats** - 该用户的立直麻将番种统计数据
7. **game_player_records** - 该用户参与的所有对局记录
