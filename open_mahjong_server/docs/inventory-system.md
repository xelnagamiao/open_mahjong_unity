# 统一仓库系统

更新：2026-09-24。玩家端统一称为“仓库”，头衔是其中一种可装配物品，改名卡是可使用的消耗物品。统一网格、选择、搜索、详情和操作，不再提供两套独立页面。管理后台继续分别维护头衔目录/授权与物品目录/库存。账号原有 6 张表、44 个字段见 [user-schema.md](user-schema.md)；本文列出库存 5 张表的全部 40 个字段，本次不修改表结构。

## 使用方式

- 登录后点击主菜单侧栏“背包”打开仓库（场景对象为 `OverlayCanvas/StorePanel`），通过面板的关闭按钮退出。
- 左侧竖排标签为“背包 / 头衔 / 角色 / 装扮”，每次正常打开默认进入背包，无“全部”标签。头衔包含默认“暂无头衔”；角色按角色目录/角色槽归类；装扮收纳外观装备；背包收纳改名卡等普通物品。空分类显示获得提示，不生成预设库存。搜索在当前分类内按物品名称/类型匹配。卡片显示名称、图标、数量或佩戴状态，右侧展示说明与可执行操作。
- “暂无头衔”（内部 title:1）是永久可用的默认物品，无需授权。所有头衔物品共用一个装配位置；装配新物品替换旧物品，点击已装配物品的“卸下”恢复默认。大厅、个人资料和对局均显示对应名称。
- 改名卡显示在同一网格，每次可使用 1–100 张，受实际持有量限制；每张增加一次网页账号中心的改名机会。游客可以查看，但使用按钮禁用并说明原因。
- 管理后台 `/admin/titles` 管理头衔、`/admin/inventory` 管理道具；用户详情支持授权、发放、回收和数量流水。头衔与背包卡片仍位于段位之后、最近登录 IP 之前。
- 发放/授予不自动使用或佩戴。停用或回收头衔会取消相关佩戴；改名卡停用保留数量并禁止使用。

## 保留内容

| 类型 | ID | 名称 | 功能 |
|---|---|---|---|
| 装配物品（头衔） | 2 | 最初的初段 | 后台授予后进入仓库；1 表示默认项“暂无头衔” |
| 消耗物品 | 3001 | 改名卡 | 每张增加 1 次 users.rename_count；编码 rename_coupon 保持稳定 |

角色、头像、头像框、礼包及对应创建模板均已移出目录。原游戏基础头像与语音资源继续供正常游戏使用，不作为背包预设。默认角色不再自动生成库存或装备项。

## 状态与事务

改名卡数量上限为 1,000,000。写操作先取得目录锁、再锁用户行；扣卡、增加改名次数、写流水在同一事务内完成。

写请求携带 8–64 位 request_id（字母/数字/下划线/连字符）。同一用户、编号和请求只执行一次；编号用于不同请求则返回冲突。客户端关闭后仍保留未确认的使用操作，重试不会重复扣卡。

后台发放、回收和目录修改须填写原因，与变更一起写入 admin_audit_log。服务端按原有目录独立校验授权、持有量和操作；玩家端统一投影为 WarehouseEntry，使用 title:{id} / item:{id} 区分来源，避免两张目录的数字 ID 冲突。授权数据不会复制到 user_inventory，也不会变成可消耗的头衔数量。游客仓库随既有游客账号生命周期清理。

后台临时创建头衔只需填写名称、说明并授权，无需 Unity 预设、图标资源或重新打包。通用物品卡会显示新名称及统一的头衔图标；目录改名、停用、授权和回收通知重新构建同一集合。选择由来源加 ID 保持稳定；物品回收或消耗完毕时选择自动回退，不保留过期操作入口。停用物品仍可查看说明，但不能使用或装配。

Unity 固定界面保存在 MainScene 的 OverlayCanvas，通过 InventoryPanel.Baking.cs 编辑器烘焙。运行时只绑定和复用物品卡片。左侧分类标签、中间三列物品网格、右侧详情组成一个仓库；切换分类共用选择和操作逻辑，取消装配在详情操作中完成。面板内所有提示统一使用仓库/物品语义。使用本项目字体和原创矢量图标。

## 全部字段

除明确标注可空的 actor_id 外，以下字段全部 NOT NULL；时间字段默认 CURRENT_TIMESTAMP，写操作维护 updated_at。

### item_definitions：物品目录（15）

| 字段 | PostgreSQL 类型 | 默认值/约束与含义 |
|---|---|---|
| item_id | INTEGER | 主键，IDENTITY 从 4000 起；内置项使用保留 ID |
| code | VARCHAR(64) | 唯一稳定编码，创建后不可修改 |
| name | VARCHAR(32) | 展示名称，拒绝富文本标签和控制字符 |
| description | VARCHAR(300) | 默认空字符串，说明 |
| category | VARCHAR(16) | CHECK：character / item / cosmetic |
| asset_key | VARCHAR(80) | 受支持的资源/效果键，创建后不可修改 |
| slot | VARCHAR(24) | 默认空字符串；模板决定 character / avatar / avatar_frame，道具为空 |
| max_quantity | INTEGER | CHECK 1–1000000；永久项为 1，道具为 1000000 |
| is_default | BOOLEAN | 默认 FALSE；当前没有默认持有物品 |
| grant_enabled | BOOLEAN | 默认 TRUE，允许新发放 |
| use_enabled | BOOLEAN | 默认 TRUE，允许使用/装备 |
| sort_order | INTEGER | 默认 0；先按此值、再按 ID 排序 |
| config | JSONB | 默认 `{}`；改名卡使用空配置 |
| created_at | TIMESTAMPTZ | 创建时间 |
| updated_at | TIMESTAMPTZ | 最近修改时间 |

### user_inventory：持有数量（5）

| 字段 | 类型 | 默认值/约束与含义 |
|---|---|---|
| user_id | BIGINT | 复合主键，FK users，ON DELETE CASCADE |
| item_id | INTEGER | 复合主键，FK item_definitions |
| quantity | INTEGER | CHECK 0–1000000；应用校验单品上限，0 行保留但不显示 |
| acquired_at | TIMESTAMPTZ | 首次建立持有记录时间 |
| updated_at | TIMESTAMPTZ | 最近数量变化时间 |

主键 (user_id,item_id)，另有 item_id 索引。

### user_equipment：当前装备（4）

| 字段 | 类型 | 默认值/约束与含义 |
|---|---|---|
| user_id | BIGINT | 复合主键，FK users，ON DELETE CASCADE |
| slot | VARCHAR(24) | 复合主键，CHECK character / avatar / avatar_frame |
| item_id | INTEGER | FK item_definitions；服务端校验持有量和槽位 |
| updated_at | TIMESTAMPTZ | 最近装备时间 |

主键 (user_id,slot) 保证一槽一件，另有 item_id 索引。空槽不保存行。当前物品目录不包含角色/外观装备；仓库中的头衔装配继续保存到 user_settings.title_id。该表保留供后续扩展。

### inventory_operations：操作与防重（9）

| 字段 | 类型 | 默认值/约束与含义 |
|---|---|---|
| operation_id | UUID | 主键，服务端生成 |
| user_id | BIGINT | FK users，ON DELETE CASCADE，库存所属用户 |
| request_id | VARCHAR(64) | 调用方稳定操作编号 |
| request_hash | VARCHAR(64) | 操作、操作者、请求正文的 SHA-256 |
| action | VARCHAR(32) | equip / use / grant / revoke |
| actor_id | BIGINT，可空 | 发放/回收的管理员；玩家自行操作为空 |
| reason | VARCHAR(500) | 默认空字符串；管理操作必填 |
| result | JSONB | 默认 `{}`；提交结果、提示和数量变化 |
| created_at | TIMESTAMPTZ | 提交时间 |

唯一约束 (user_id,request_id)。重试返回原结果，同时重读当前背包快照。

### inventory_ledger：数量流水（7）

| 字段 | 类型 | 默认值/约束与含义 |
|---|---|---|
| entry_id | BIGSERIAL | 主键 |
| operation_id | UUID | FK inventory_operations，ON DELETE CASCADE |
| user_id | BIGINT | FK users，ON DELETE CASCADE |
| item_id | INTEGER | FK item_definitions |
| delta | INTEGER | 实际数量变化，可正、负、0 |
| quantity_after | INTEGER | CHECK >=0，变更后余额 |
| created_at | TIMESTAMPTZ | 流水时间 |

索引 (user_id,entry_id DESC) 用于最近流水查询。每次改名卡发放、回收或使用产生对应数量流水。

## 网络、显示与牌谱

WebSocket：inventory/catalog 是公开资源目录；inventory/get 只返回已登录用户的库存；inventory/equip、inventory/use 从连接读取身份，忽略伪造 user_id。

Unity 接收 inventory/state（自己的持有量和装备）及 inventory/update（目录和公开外观）。其他玩家不会收到库存数量。修改同步到房间缓存与对局人物；开局或重连加载权威状态。

头像解析接入大厅用户卡、个人资料、好友/关注/排行榜和牌桌。角色音色在对局沿用 voice_used。牌谱表另新增 `game_player_records.avatar_frame_used INTEGER NOT NULL DEFAULT 0` 保存头像框快照，旧牌谱默认无框。九种落库规则、牌谱列表/详情、观战玩家设置和 Unity 回放 DTO 均携带该字段；回放不会被实时玩家装备更新覆盖。

管理路由均 requireAdmin，以下前缀为 `/api/admin/inventory`：

| 方法与路径 | 用途 |
|---|---|
| GET /catalog | 定义与可选模板 |
| POST /catalog | 创建物品 |
| PATCH /catalog/{item_id} | 修改定义/状态 |
| GET /users/{user_id} | 用户背包 |
| POST /users/{user_id}/grant | 发放 |
| POST /users/{user_id}/revoke | 回收 |
| GET /users/{user_id}/ledger | 最近 100 条数量流水 |

Node 代理到既有 calcServer.baseUrl 的 Python `/admin/inventory/...`，传递实际管理员 Bearer token。Python 调用 Node `/api/admin/auth/me` 再次验证操作者。认证地址优先使用 `INVENTORY_ADMIN_AUTH_URL`；未设置时读取同工作区 `open_mahjong_web/.env` 的 `PORT`，连接 `http://127.0.0.1:<PORT>/api/admin/auth/me`，没有 Web 配置时回退本地开发端口 3000。生产 Web 使用 8082 时须在 Web `.env` 中配置该端口；若端口只由启动器环境指定，或服务分机/容器部署，须显式设置 `INVENTORY_ADMIN_AUTH_URL`。不会使用请求提供的地址，认证不可用时拒绝管理操作。

## 正常初始化、历史迁移与验证

商品目录的一次性迁移已经完成，迁移脚本已归档，Python 正常启动不再清空或重置目录、持有记录、装备和数量流水。正常建表和默认物品初始化保留，重复执行必须保留现有库存、余额、装备以及管理员添加的定义。

旧迁移的审计标记为 store.catalog_reset / 20260923_minimal_store，改动前数据保存在 admin_audit_log.payload，留作追溯及回退，不作为正常启动触发器。Python 与 Node 的初始头衔统一为“最初的初段”；物品初始化仅生成改名卡。

自动化覆盖独立 PostgreSQL schema 的正常建表、重复初始化保留目录/余额/外观、不创建目录重置审计、防重、并发扣卡、停用、鉴权、事务回滚及历史牌谱字段；头衔测试覆盖授权与单选/取消佩戴。运行方式：

- Python：python -m unittest server.database.test_inventory server.database.test_titles -v，分别设置 INVENTORY_TEST_DATABASE_URL / TITLES_TEST_DATABASE_URL；测试只操作各自创建的独立 schema。
- Node：node --test server/services/inventory.integration.test.js server/services/titles.integration.test.js。
- Unity：编译、场景仓库引用/网格检查、目录 ID 冲突与默认项验证、搜索/筛选、主菜单背包入口与面板关闭按钮、装配/卸下、改名卡使用与后台实时回收。

此次未生成发布版 Unity/WebGL 包或部署。发布需同步更新 Python、Node/Vue 与 Unity。
