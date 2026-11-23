# 比赛级状态 观战和牌谱 V2

一个比赛聚合两个独立牌桌。开室和闭室各自持有 GameState、行动序列、回放节点和观战延迟；比赛只负责身份、牌山实例、队伍、配对结算和生命周期。客户端同时缓存两桌公布的数据，只渲染当前桌。普通单桌使用同一种牌谱容器，其唯一桌号为 `main`。

字段的机器契约见 `match-record-v2.schema.json` 和 `match-spectator-v2.schema.json`。JSON Schema 检查类型和结构；`format.validate_match` 另外检查跨字段一致性、物理来源唯一性、节点数量和牌桌身份。

## 运行状态

`PairedMatch.data` 是可持久化聚合，`PairedMatch.states` 是运行时的 `{open: GameState, closed: GameState}` 引用，不序列化对象或合并两桌的玩家列表。每桌仍独立推进操作、计时、补牌和终局。

| 字段 | 类型和语义 |
| --- | --- |
| `state_schema_version` | 当前为 1；旧快照加载时补充稳定的牌山实例标识 |
| `id`、`revision` | 比赛 ID；乐观锁修订号，每次持久化增加 |
| `profile` | 规则版本、牌山分区、补牌策略、IMP 标尺等冻结配置 |
| `room_ids` | `open`、`closed` 对应的房间 ID |
| `members` | 两桌原始参赛成员快照；观战准入同时检查两桌成员 |
| `sync_mode` | true 时逐副等两桌完成；false 时各桌独立推进 |
| `boards` | 有序的原始牌山，包含原始手牌和各原始风位牌墙 |
| `board_instances` | 与 boards 一一对应：`board_instance_id`、从 1 开始的 `board_no`、从 1 开始的 `attempt` |
| `planned_boards` | 计划副数 |
| `completed_boards` | 已配对结算的副数 |
| `table_completed_boards` | 两桌各自已完成的副数 |
| `pending_boards` | 按副号保存尚未配对的结果和恢复检查点 |
| `results` | 逐副两桌结果、原始分差和 IMP 结算 |
| `scores`、`small_scores`、`imp_scores` | 比赛聚合积分；不从观战进度推测 |
| `status` | waiting、running、completed、interrupted |
| `final_records`、`final_players`、`record_ids` | 两桌结算时的牌谱、人员快照及旧桌编号；完整落盘后，相同的逐局数据使用正文引用 |

牌山实例属于一次有效发牌。训练重开即使使用完全相同的种子和牌序，也产生新的实例 ID 并增加 attempt。旧快照初次补标识采用稳定 UUID 派生，重复加载不改变身份。不能用种子、牌山 key 或副号跨比赛合并实例。

## 牌谱容器

根字段：`format="open_mahjong.match_record"`、`schema_version=2`、`match_id`、`record_revision`、`match_format`、`completeness`、`rule`、`sub_rule`、`rule_profile`、`sync_mode`、`teams`、`board_catalog`、`tables`、`settlements`、`result`、`provenance`。

`match_format` 为 `paired_tables_2v2` 或 `single_table`；前者必须同时出现 open 和 closed 键，即使某桌缺失。`completeness` 为 complete 或 partial。同一比赛只有一条牌谱正文。另一桌完成后在同一事务内补全该行。

`sync_mode` 与两桌 `payload.game_title.duplicate_sync_mode`、`payload.duplicate_match.sync_mode` 必须一致。生成牌谱和一次性迁移可从任一已记录的布尔值取值；全部缺失时保留 null，不推断为同步或非同步。记录矛盾或类型错误时拒绝构造，迁移报告冲突并停止切换；客户端对已有矛盾记录显示“记录冲突”，不会加载时重写原记录。

`teams[]` 包含 `team_id` 和 `members[]`；成员由 `table_id`、`original_player_index`、`user_id` 定位。机器人可能共享用户 ID，不能以用户 ID 代替座位身份。队伍归属取原始牌谱标题中的 `team_by_player_index`，不根据当前显示的东南西北重新推断。

`board_catalog[]` 包含 `board_instance_id`、`board_no`、`attempt`、`wall_layout`、`status` 和可选 `wall_definition`。旧记录不能确认 attempt 时保留 null。直播只公布可见实例，移除完整原始牌山；只有最终且有权访问的牌谱才包含完整 wall_definition。

每个有效 `tables[table_id]`：

| 字段 | 定义 |
| --- | --- |
| `table_id` | open、closed 或 main，与字典键一致 |
| `table_record_id` | 已保存云端的原桌牌谱 ID；没有则 null |
| `record_state` | partial 或 complete；缺桌用 missing |
| `payload_codec` | `om.action_ticks.v1`，保留现有规则的行动解码器 |
| `payload` | 原牌谱标题、逐局数据和扩展字段；仅移除 ask_hand / ask_other 传输请求节点 |
| `players` | 该桌人员和外观快照，含原始座位 |
| `source_sha256` | 移除传输请求之前的原始 record 的规范 JSON 哈希 |
| `round_meta` | 以原有 `round_index_N` 为键的定位元数据 |

missing 桌的 payload、table_record_id 为 null，players 为空数组，round_meta 为空对象；可附 reason，例如 access_denied。缺桌不能靠复制另一桌补齐。

每局元数据包含 `table_round_id`、`board_instance_id`、`round_index`、`legacy_node_map`、`initial_anchors`、`action_meta`、`alignment_status`、`alignment_reason`。action_meta 与清理后的 action_ticks 等长。每项包含 `actor_original_seat`、`occurred_at_ms`、`wall_ref`、`wall_ref_basis`。时间未知为 null，不伪造时间戳；basis 为 recorded、reconstructed、unavailable 或 not_applicable。

节点 n 表示已应用前 n 个行动，范围为 0 到 action_ticks.length。局部节点不承担跨桌身份。`legacy_node_map[n]` 把旧节点映射为移除 ask 后的新节点；旧链接进入时使用此映射，新链接直接记录当前节点，并带 table 和 cursor=v2。

## 同牌山位置切桌

物理来源键为以下四元组：

```json
{"board_instance_id":"…","zone":"seat_wall","source_original_seat":1,"offset":0}
```

zone 为 seat_wall 或 common_wall；公共区的 source_original_seat 必须为 null。offset 从 0 开始，表示该原始分区中的固定位置。摸牌人 actor_original_seat 是元数据，不参与身份比较。队友退场后接管牌墙、补杠摸牌、血流公共区轮转都保留原分区和原 offset，不能改成摸牌人的当前座位。

定位按以下顺序执行：

1. 找到来源所在的同一个 board_instance_id；副号、节点、牌值、剩余牌数相同均不能替代此匹配。
2. 在目标桌该实例中寻找完全相同的四元组。多处命中视为 DATA_CONFLICT，不能任选一个。
3. 对行动索引 i 的摸牌，摸牌前为节点 i，摸牌后为节点 i+1。
4. 庄家起手第十四张记录在 initial_anchors，摸牌后为节点 0；无“摸牌前”的回放状态。
5. 来源由当前选中视角的 `original_player_index` 确定，只取 `actor_original_seat` 与之相同的摸牌。摸牌前仅在紧接着的下一行动属于该玩家时使用下一行动；其他情况使用该玩家最近一次已应用的摸牌，不扫描尚未播放的后续行动。最近一次摸牌缺少来源时不能回退到更早的来源。起手锚点仅适用于该玩家本人是庄家的情况。
6. 任一非精确结果都留在原桌、原节点并说明原因，不自动跳最近节点。

| 结果 | 含义 |
| --- | --- |
| EXACT | 精确命中，返回目标 round_key、node、actor_original_seat |
| NOT_VISIBLE_YET | 目标桌仍在进行，该来源尚未公布 |
| NOT_CONSUMED | 目标副已结束且来源完整，但未消费该位置 |
| TABLE_MISSING | 无可访问的目标桌 |
| INSTANCE_UNVERIFIED | 目标桌没有可确认的同次发牌实例 |
| ORIGIN_UNAVAILABLE | 原记录缺少可验证的摸牌来源 |
| PHASE_UNAVAILABLE | 请求了不存在的阶段，例如庄家第十四张的摸牌前 |
| DATA_CONFLICT | 同一来源重复命中或元数据冲突 |

直播切桌取对桌已经公布的尾部，不等待追上原桌。历史阅览的“切换到同牌山位置”和“独立阅览进度”为两种模式；改变模式不自动前往另一桌。各桌独立光标包含局、节点和原始座位视角，与对照时的临时位置分开保存。退出对照恢复此前独立进度；切桌对照保持同原始座位视角，目标实际摸牌人单独显示，借墙时不自动换成实际摸牌人的视角。开启对照后暂停自动播放；新追加信息只更新缓存，不把用户强制带回直播。

## 阅览信息与 IMP

“阅览状态”随节点、局、视角、桌号、播放/暂停和追加信息刷新。游戏信息展示比赛级规则、原对局同步模式和完整八人名单：开闭室两列，每列按原始东南西北排列。名单中的个人 IMP 是全场同座位比较；规则、玩家 ID、牌山来源与校验值折叠显示，长值提供完整复制。训练牌山按 `wall_source=training` 展示，不显示复式密钥或独立的场景复现开关。

牌谱阅览左上角显示截至当前播放位置已结算的队伍累计 IMP，与原对局同步模式无关。第 N 副局首只计入此前各副，本副在正式 `board_summary`（旧记录为 end）被应用后计入；回退、跳局、切桌均重新投影。先按当前 `board_instance_id` 确认唯一副号，再读取 `settlements[].imp_scores`；结算含实例 ID 时优先匹配实例，否则要求副号唯一。缺失或矛盾时显示不可用，不能拿个人 IMP、原始分或全场最终分代替。血流复式的正式胜负仍遵循其冻结规则，阅览 IMP 不改变正式结果。直播与结束前的观战仍遵循服务端的同步/非同步公布边界。

## 观战协议

沿用已认证游戏 WebSocket，协议字段 `protocol_version=2`。一个用户同时只有一个比赛订阅。列表按 match_id 聚合为一张两行卡片、一个观战入口，控制台提供“前往开室”和“前往闭室”。

```json
{"type":"spectator/match_subscribe","protocol_version":2,"request_id":"客户端请求ID","match_id":"比赛ID","requested_tables":["open","closed"],"resume":{}}
```

成功先返回 `spectator/match_accepted`：原 request_id、match_id、服务端新建的 subscription_id、allowed_tables 和 policy_version=1。随后发送 match、open、closed 三个独立流；只发送获准的桌。

所有流消息含 match_id、subscription_id、stream_id、stream_epoch 和 success。`stream_snapshot` 用 at_seq 和 body 表示完整已公布快照；`stream_delta` 用 from_seq、to_seq 和等长 events 表示连续事件。seq 计数传输事件，与牌谱 node 不同。

match 的 body 是比赛 manifest：规则、同步模式、计划副数、公开阶段、队伍、可见 board_catalog 和结果。桌 body 含 table_id、availability、table_record、released_head、table_phase。released_head 是 `{table_round_id,node}`；waiting 时为 null。

| event.kind | 追加内容 |
| --- | --- |
| manifest_updated | manifest 完整替换 |
| record_ready | 最终 manifest，附 final_streams 向量 |
| table_available | 桌 body 完整替换，包括初次可见和标题更新 |
| round_opened | round_key、round_header、round_meta |
| actions_appended | table_round_id、start_node、ticks、action_meta；start_node 必须等于现有 ticks 数量 |
| table_finalized | 带最终完整牌谱的桌 body |

同 epoch 已应用消息去重；部分重叠 delta 只应用未收到的连续后缀。间隙、epoch 不符、节点前缀不符，整包拒绝并发送 `spectator/stream_resync`，不部分写入。保留最近 512 个事件，超出窗口回完整快照。重开改变桌 epoch；客户端记住已退役 epoch，晚到旧快照不能恢复旧状态。

客户端 `stream_ack` 发送 `{cursors:{流:{stream_epoch,applied_seq}}}`。重连带相同格式的 resume，但获取全新 subscription_id；旧连接消息、旧退订不能影响新订阅。权限缩减时丢弃已撤销桌缓存及旧 manifest 并重取。退出发送 match_unsubscribe，并清除客户端缓存和服务端订阅任务。

最终完成要求两桌落盘完成，且两桌最后的消息均已度过观战延迟。`final_streams` 指明各获准桌最终 epoch/seq，客户端全部应用后才从观战转为牌谱阅览。不能只收到“比赛已结束”就停止收取另桌尾部。

权限复用已有规则：当前仅训练复式开放，正式题库锁定；参赛者（任一桌）、正在其他对局的人、正在匹配的人不能加入。每桌检查观战开关和 AI 数量限制。隐藏桌的数据不得通过结果、队伍成员、结算、完整牌山或 provenance 泄露。发送有超时边界，慢连接不会卡住游戏状态推进。

## 牌谱存储与读取

普通比赛用原 game_id，四川复式的公开号由内部比赛 ID 确定性生成。game_replays 每场只保存一份 V2 正文；game_record_tables 是每桌的定位索引，保留桌编号、桌别、比赛和时间关联。game_player_records 保留参赛成绩和本人的收藏、备注。正文、下载和公开参赛名单不能输出这两个私有字段。

常规读取没有旧视图或逐桌还原函数。只计桌数、查日期时查定位索引；需要标题或回放时按 replay_id 和 table_id 取该桌 V2 字段。比赛列表按 replay_id 去重，按桌计的统计使用原桌编号。少数需要原始操作位置的证据、历史检查点和来源核验使用正文内的溯源信息，不另存一份牌谱。

客户端通过 get_record_by_id 或 HTTP GET /api/platform/record/{game_id} 取得相同的 record、game_id、selected_table_id 和参赛者。旧桌编号额外返回 legacy_cursor_map。两桌各自校验牌墙权限，锁定桌的局面、相应参赛者和比赛结算不会进入公开正文。

桌面目录保存 `<game_id>.json`、索引和 `record-aliases.json`。WebGL 在浏览器自身的 IndexedDB 中处理对应数据。历史 ask_hand、ask_other 的位置和内容保存在 transport_actions 中，原记录中的时间戳数量差异由 source_round_fields 表示。以 restore_source 重建的逐桌数据可用 source_sha256 核对。

## 一次性迁移

在 open_mahjong_server 目录执行。PostgreSQL 的 OM_MATCH_MIGRATION_DSN 指向要迁移的数据库。先生成独立备份，在备份副本上完整演练并核对后执行实际切换。

```text
python -m server.records.migrate verify --backend postgres
python -m server.records.migrate apply --backend postgres
python -m server.records.migrate recompute --backend postgres --dry-run
python -m server.records.migrate recompute --backend postgres
python -m server.records.migrate verify --backend files --input <LocalRecords目录>
python -m server.records.migrate apply --backend files --input <LocalRecords目录> --backup <独立备份目录>
```

旧物理库逐桌比较原文并保留原编号、外键；发行版 36 已统一的库不重搬正文。`verify` 在一致性快照中核对每桌来源 SHA-256，并预演依赖限制；`--receipt-out` 可保存本次已验证的指纹。切换时暂停旧写入，`apply --receipt` 在锁内核对所有键与内容指纹，只对新增或变更桌重新做完整校验。只使用自己刚验证的副本凭据。两个旧视图和 SQL 函数以 RESTRICT 在同一事务移除；未知依赖、来源冲突或锁超时均停止，不能跳过。重复执行不重建视图。退回发行版 36 程序时先从切换前备份恢复这三个对象的定义，而不是覆盖更新后的业务数据。

`recompute` 按已保存动作、结算及可用快照补录可证明的复式派生字段，不重算其他规则 ELO 或重新发放奖励。`--dry-run` 可预览。桌面版逐份写入并读回核对，发生中断可重跑同一命令。Unity WebGL、网页本地牌谱和分析缓存在原站点、原浏览器配置文件中各自执行一次升级，手动导入导出使用 browser_migration.js 和同一 CLI 的 browser 后端。

规范哈希采用 UTF-8、对象键按 UTF-16 码元排序、无空格。整数精确保留；浮点数为 IEEE 754 binary64，非整数按 17 位有效数字展开十进制，整数值浮点数展开为其精确整数，去掉小数尾零和指数，负零归零。字符串只转义 JSON 必需的引号、反斜杠和 U+0000 至 U+001F，保留其他 Unicode 字符。Python、Unity 和浏览器桥使用同一规则。不要用带日期自动转换的 JSON 解析器改变原字符串。

## 验证入口

Python：server/records/test_format.py、test_migrate.py、test_store.py、test_spectator.py、test_contract.py；test_store 仅在 OM_RECENT_TEST_DSN 指定的数据库中创建临时 schema，不改业务表。实际双桌游戏回归位于 server/gamestate/game_sichuan/test_duplicate_simulation.py。

在 open_mahjong_server 中用 `python -m pip install -r requirements-test.txt` 安装已验证的测试依赖，再运行 `python -m pytest server/records`。WebSocket 传输回归位于 test_spectator_wire.py。

浏览器迁移：`node --test server/records/browser_migration.test.js`，测试依赖 fake-indexeddb，OM_TEST_PYTHON 指定 Python 可执行文件。Web 路由单测位于 open_mahjong_web/server/services/publicGameRecord.test.js。

浏览器测试依赖单独锁定在 server/records/package.json / package-lock.json：在 open_mahjong_server 中执行 `npm ci --prefix server/records --ignore-scripts`。运行前设置 OM_TEST_PYTHON 和 OM_BROWSER_MIGRATION_TEST_DIR（后者指向 .om_workspace 下的绝对临时目录，或 CI 临时目录），随后执行 `npm test --prefix server/records`。这些 Node 依赖仅供测试，不参与 Python 服务端运行或浏览器迁移脚本发布。

Unity：MatchRecordValidation.Run 检查跨语言协议缓存和定位；MainScene Play Mode 中调用 MatchRecordSceneValidation.Run 并传入 Fixtures/PairedReplaySbr.json 内容，验证真实牌桌加载、按钮、摸牌前后、各桌光标、后台追加与退出。UI 使用现有 MainScene 和真实牌面，MatchSpectatorUiBaker 仅负责把正式控件和配对卡片写入场景。

MatchRecordCodecValidation.Run 使用 Fixtures/MatchCanonical.json 检查 Python、Unity、浏览器的规范哈希一致性，并通过正式响应解码器验证日期字符串、Unicode、浮点数和 64 位整数不会被传输层改写。Web 的旧规则入口和权限回归分别位于 hongzhongRecordAccess.test.js、shanxiRecordAccess.test.js、recordMetadata.integration.test.js。
