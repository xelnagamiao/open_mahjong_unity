# 用户数据表设计与全部字段

更新日期：2026-09-23。依据 Python/Node 初始化代码和本机 PostgreSQL 实际表结构核对。本文列出账号、个性化设置、游戏配置、段位、头衔目录、头衔授权六张表的全部字段（44 个）；新增背包五张表的全部字段（40 个）见 [背包系统设计与字段](inventory-system.md)。合计十一张核心表、84 个字段，不包含用户实际数据。

## 关系与职责

```text
users（账号）
 ├─ 1:1 user_settings（当前佩戴/头像/角色/音色）
 ├─ 1:1 user_config（游戏配置）
 ├─ 1:1 rank_data（段位）
 ├─ 1:N user_titles（已获授的头衔）─ N:1 titles（头衔目录）
 ├─ 1:N user_inventory（物品余额）─ N:1 item_definitions（物品目录）
 ├─ 1:N user_equipment（当前装备槽）
 └─ 1:N inventory_operations（操作）─ 1:N inventory_ledger（数量流水）
```

“拥有”与“佩戴”分别保存。一个用户可拥有多个头衔，但 `user_settings.title_id` 只保存一个当前选择。`1` 表示不佩戴，不是可授予的头衔；可授予的头衔从 `2` 开始。物品背包现支持永久角色/装扮、道具数量与消耗、装备槽、操作去重和流水，尚无交易、到期或独立实例。

## users：账号主表（16 个字段）

| 字段 | PostgreSQL 类型 | 可空 | 默认值/约束 | 含义 |
|---|---|---|---|---|
| user_id | BIGINT | 否 | 主键；`nextval('registered_user_id_seq')` | 永久用户标识 |
| username | VARCHAR(255) | 否 | UNIQUE | 用户名；实际输入长度与字符规则由应用层额外校验 |
| password | VARCHAR(255) | 否 | 无默认值 | `salt:hash` 格式的 PBKDF2-SHA256 密码哈希；游客为空字符串 |
| is_tourist | BOOLEAN | 是 | FALSE | 是否游客；历史表结构允许 NULL，应用通常按非游客处理 |
| created_at | TIMESTAMP WITHOUT TIME ZONE | 是 | CURRENT_TIMESTAMP | 创建时间 |
| is_mcrpl_qualified | BOOLEAN | 否 | FALSE | MCRPL 特许入场资格 |
| sponsor_expires_at | TIMESTAMP WITHOUT TIME ZONE | 是 | NULL | 赞助到期时间；有效赞助身份根据此字段计算 |
| ban_expires_at | TIMESTAMP WITHOUT TIME ZONE | 是 | NULL | 封禁到期时间；类型非空且到期为空表示永久封禁 |
| ban_type | VARCHAR(32) | 是 | NULL | `login` / `chat` / `match` / `full`；NULL 表示未封禁 |
| ban_reason | TEXT | 是 | NULL | 封禁原因 |
| email | VARCHAR(255) | 是 | NULL | 绑定邮箱；已验证且非空时按 LOWER(email) 唯一，未验证记录不受该部分索引限制 |
| email_verified_at | TIMESTAMP WITHOUT TIME ZONE | 是 | NULL | 验证成功时间；未验证邮箱不能用于邮箱登录/找回 |
| is_beginner_qualified | BOOLEAN | 否 | FALSE | 初级场特许入场 |
| is_intermediate_qualified | BOOLEAN | 否 | FALSE | 中级场特许入场；不解除中级场段位上限 |
| is_advanced_qualified | BOOLEAN | 否 | FALSE | 高级场特许入场 |
| rename_count | INTEGER | 否 | 0 | 剩余自助改名次数；字段最初迁移时已有非游客补 1 次 |

注册 ID 序列从 `10000001` 开始，不复用。游客使用独立 `tourist_user_id_seq`，范围 `9000000–9900000`，允许循环。机器人 ID `<=10` 不依赖真实用户记录。不要在后续物品表用用户名作关联键。

主表没有 `updated_at`、`last_login_at`、余额、背包 JSON、`is_admin` 或 `is_sponsor` 字段。管理员权限来自 Web 服务配置；赞助身份由到期时间计算；登录记录另存 `user_login_ips`。

## user_settings：当前外观选择（7 个字段）

| 字段 | 类型 | 可空 | 默认值/约束 | 含义 |
|---|---|---|---|---|
| user_id | BIGINT | 否 | 主键；外键 users，ON DELETE CASCADE | 对应用户 |
| title_id | INTEGER | 是 | 1 | 当前佩戴头衔；1=不佩戴。服务端校验该用户已获授且头衔启用 |
| profile_image_id | INTEGER | 是 | 1 | 头像 ID |
| character_id | INTEGER | 是 | 1 | 角色 ID |
| voice_id | INTEGER | 是 | 1 | 音色 ID |
| created_at | TIMESTAMP WITHOUT TIME ZONE | 是 | CURRENT_TIMESTAMP | 创建时间 |
| updated_at | TIMESTAMP WITHOUT TIME ZONE | 是 | CURRENT_TIMESTAMP | 最近设置修改时间；本次佩戴/撤销/停用操作会更新 |

保留旧字段和默认值，不改动旧用户数据格式。`title_id` 没有直接外键约束，因为 `1` 是兼容旧客户端的空选择；写入和读取均由服务端校验授权。查询遇到无授权、已停用或未知 ID 时按不佩戴返回。头像/角色/音色目前仍是选择 ID，不能据此推断已实现物品所有权。

## user_config：游戏配置（4 个字段）

| 字段 | 类型 | 可空 | 默认值/约束 | 含义 |
|---|---|---|---|---|
| user_id | BIGINT | 否 | 主键；外键 users，ON DELETE CASCADE | 对应用户 |
| volume | INTEGER | 否 | 100 | 总音量，业务语义 0–100；现有表未加范围 CHECK |
| created_at | TIMESTAMP WITHOUT TIME ZONE | 是 | CURRENT_TIMESTAMP | 创建时间 |
| updated_at | TIMESTAMP WITHOUT TIME ZONE | 是 | CURRENT_TIMESTAMP | 最近修改时间 |

Unity 的其他本机设置还有 PlayerPrefs，不全在此表内。

## rank_data：段位数据（5 个字段）

| 字段 | 类型 | 可空 | 默认值/约束 | 含义 |
|---|---|---|---|---|
| user_id | BIGINT | 否 | 主键；外键 users，ON DELETE CASCADE | 对应用户 |
| guobiao_rank | VARCHAR(10) | 否 | '10级' | 国标段位名 |
| guobiao_score | DOUBLE PRECISION | 否 | 0 | 国标段位积分 PT，支持小数 |
| created_at | TIMESTAMP WITHOUT TIME ZONE | 是 | CURRENT_TIMESTAMP | 创建时间 |
| updated_at | TIMESTAMP WITHOUT TIME ZONE | 是 | CURRENT_TIMESTAMP | 最近修改时间 |

各规则对局统计另存独立统计表，不混入用户主表。

## titles：头衔目录（新增，7 个字段）

| 字段 | 类型 | 可空 | 默认值/约束 | 含义 |
|---|---|---|---|---|
| title_id | INTEGER | 否 | 主键；IDENTITY 从 3 开始；CHECK >1 | 头衔定义 ID；保留 2=最初的初段 |
| name | VARCHAR(24) | 否 | UNIQUE；去空白后非空 | 展示名称；后台拒绝富文本标签与控制字符 |
| description | VARCHAR(200) | 否 | '' | 面板内说明 |
| is_enabled | BOOLEAN | 否 | TRUE | 是否启用；停用后不能授予/佩戴 |
| sort_order | INTEGER | 否 | 0 | 排序，小值靠前，同值按 ID |
| created_at | TIMESTAMPTZ | 否 | CURRENT_TIMESTAMP | 创建时间 |
| updated_at | TIMESTAMPTZ | 否 | CURRENT_TIMESTAMP | 最近编辑时间 |

后台以停用代替删除，保留 ID 和授权关系。重新启用不会自动恢复佩戴，玩家可重新选择。更新名称后在线客户端同步目录，客户端无需重新打包才能认识新头衔。

## user_titles：头衔授权（新增，5 个字段）

| 字段 | 类型 | 可空 | 默认值/约束 | 含义 |
|---|---|---|---|---|
| user_id | BIGINT | 否 | 复合主键成员；外键 users，ON DELETE CASCADE | 获授用户 |
| title_id | INTEGER | 否 | 复合主键成员；外键 titles | 已获授头衔 |
| granted_by | BIGINT | 是 | 外键 users，ON DELETE SET NULL | 授予管理员；历史迁移可为空 |
| granted_at | TIMESTAMPTZ | 否 | CURRENT_TIMESTAMP | 授予时间 |
| grant_reason | VARCHAR(500) | 否 | '' | 授予原因；后台要求填写 |

主键 `(user_id, title_id)` 防止重复授予；另有 `title_id` 索引便于统计获授人数。撤销删除当前授权行，并在同一事务中取消相关佩戴；操作历史保留在现有 `admin_audit_log`。头衔定义、授权与审计同事务写入。

## 与背包的关系

库存沿用“定义 → 用户持有 → 当前装备”的关系拆分。后台的头衔目录和授权表保持独立，方便动态新增；Unity 统一投影为“仓库”物品集合，头衔作为可装配物品，与改名卡共用网格、搜索、详情及操作，不再按两套系统分页面。当前目录仅有“最初的初段”和改名卡，另有无需授权的默认项“暂无头衔”。`user_settings.character_id/voice_id/profile_image_id` 是装备结果的兼容投影，所有权由 `user_inventory` 校验；头像框来自 `user_equipment`，API 的 `avatar_frame_id` 为派生字段，不是 user_settings 新列。改名卡在同一事务内扣道具并增加 `users.rename_count`，网页账号中心继续使用该计数。本次统一仓库不新增或迁移数据库字段。

牌谱表另新增 `game_player_records.avatar_frame_used INTEGER NOT NULL DEFAULT 0` 保存该局头像框；不计入上述十一张核心表。完整使用方法、内置物品和新增表全部字段见 [inventory-system.md](inventory-system.md)。

其他关联数据见 [数据库总览](../server/database/DB_design.md)：登录 IP、邮箱验证/找回、好友、牌谱和各规则统计等各自独立保存。历史牌谱保留对局时的 `title_used` ID，头衔改名显示新目录名称；不会因当前佩戴改变而回写旧牌谱 ID。

## 使用与部署

- 后台：`/admin/titles` 管理目录；用户详情“头衔授权”授予/撤销，沿用管理员鉴权。
- Unity：点击主菜单“背包”打开场景中的 `OverlayCanvas/StorePanel`，通过面板的关闭按钮退出。
- 选择自己获授的启用头衔并保存，或选择默认项“暂无头衔”（ID 1，无需授权）。取消佩戴后也恢复该默认项，默认名称在大厅、个人资料和对局中正常显示。重登保留；在线房间和牌桌同步选择。后台授予不自动佩戴。
- Node/Python 启动时执行幂等建表；初始头衔为“最初的初段”，物品仅预设改名卡；一次性清理迁移将移出的目录和授权归档，保留账号及改名卡余额，详见背包文档。
- 发布时需要更新 Python、Node/Vue 和 Unity 客户端；旧客户端只有写死的头衔目录，不能显示新头衔。
- Node 通过游戏服务 `/admin/titles/refresh` 触发数据库重读和在线刷新；该入口只允许无浏览器/代理来源头的 loopback 请求。若服务分机部署，需提供受保护的内部同步通道；保存接口会明确返回同步是否成功。同步失败时数据仍已保存，重新登录/开局读取权威设置。

测试：Node `server/services/titles.integration.test.js` 与 Python `server.database.test_titles` 使用 `TITLES_TEST_DATABASE_URL` 指定连接，每次创建并清理自己独有的测试 schema。
