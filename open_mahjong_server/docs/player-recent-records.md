# 玩家面板：最近顺位与国标历史最高番

## 统计口径

- 以已保存的完整牌谱和 `game_player_records` 为准。整场包含机器人（user_id <= 10）则排除；支持游客和注册玩家。
- `room_type=match` 是天梯，`custom` 是自定义，分别保存；赛事 `events` 不混入两者。
- 最近顺位覆盖国标、立直、青雀、古典、简单、四川、长沙、台湾、虹雀，排除自由模式。
- 按玩家、规则、房间类型合并各局制，保存最近 10 场最终 `rank`；沿用结算的并列顺位。
- 不另外根据子规则或起和番数过滤已经保存的牌谱。原有累计统计表的筛选口径保持原状。
- 最近大和仅统计国标，取该玩家该分类的历史最高**实际结算总番数**，没有 10/30 场或时间限制；同番取较新的一次。
- 从全部现存牌谱恢复历史；已经被删除的原始牌谱无法追溯。完成采集的历史最高番独立保留，不会因十场窗口滑动而失效。

## 启动与独立脚本

正常执行 `python main.py` 时，FastAPI lifespan 会在开始提供服务前自动回填。首次运行读取全部历史，默认每 100 份牌谱提交一次数据和检查点。后续启动检查完成标记，不重复扫描。

也可以在 `open_mahjong_server` 目录手动执行：

```powershell
.\.venv\Scripts\python.exe scripts/backfill_player_recent_records.py --batch-size 100
```

脚本使用与服务器一致的 `server.local_config.Config`（不存在时回退 `test_config`）。不启动游戏服务、不改动牌谱、不重建原有累计统计。

首次回填期间日志按页输出进度。进程中断后从最后已提交的页继续；重复执行不会重复增加顺位点。数据库错误回滚当前页并向调用方报错。

不能完整还原的国标牌谱写入 `player_recent_record_errors`，顺位仍正常保存。错误记录会在下次启动/手动运行时重试；存在错误时完成标记保持空，独立脚本返回退出码 1。修复对应牌谱或追溯逻辑后再次运行即可。检查：

```sql
SELECT * FROM player_recent_record_migrations;
SELECT game_id, details FROM player_recent_record_errors ORDER BY game_id;
```

## 保存与追溯

`player_recent_records` 主键为 `(user_id, rule, room_type)`：

- `placements`：最多十条，按结束时间从旧到新排列，含 `game_id / ended_at / rank / match_type`。
- `big_win`：国标最高番快照，其他规则为空。含实际总番数、主番种名、完整番种列表、手牌、副露分组、花牌、和牌张、和牌方式及牌谱定位。

老牌谱时间无时区时按原服务的 Asia/Shanghai 解释，统一输出 UTC；缺少/无效 `end_time` 时回退该牌谱 `created_at`。同时间以 `game_id` 稳定排序。

新国标对局在 `player_action_record_hu` 写入有效和牌动作后、清理手牌前复制快照；错和跳过。牌谱根部 `player_best_wins_version=1` 和 `player_best_wins` 保存每位玩家本场最高番，以便后续追溯和重建。

旧国标牌谱逐手还原摸切、补花、吃碰、明杠/暗杠/加杠、抢杠和，使用每手 `seats` 将当前座位映射回原始座位与 user_id。保留历史结算番数，不用当前规则重新计番。旧牌谱可恢复牌面分组；`combination_mask` 仅新结算快照保留原始方向掩码。

九种规则的 `store_*_game_record` 在提交前统一调用 `update_player_recent_records`，与牌谱、玩家行处于同一事务。按固定玩家顺序锁行，避免并发对局相互覆盖。相同 game_id 重复处理会合并而不是追加重复点。迁移使用数据库互斥锁，避免多个服务进程同时回填。

## 同步和显示

请求 `data/get_player_recent_records`：`userid`、`request_id`。响应 `player_recent_records`：`user_id`、原样回传的 `request_id`、`rules`；每种规则各有 `match` 和 `custom`，包含 `placements` 与可空的 `big_win`。

面板每次打开请求一次全部规则，切换标签使用同一份玩家数据；玩家 ID 和请求 ID 同时匹配才接收，防止迟到回复串到其他玩家。

- 无最高番记录：显示“无最近大和”，清空牌面。
- 顺位图固定十个横坐标位置，旧场次在左；不足十场时从左开始连线，右侧留白。
- 一场只显示左侧一个黄色点；零场只有四条网格线，不显示空数据文案或示例点。

## 验证

```powershell
.\.venv\Scripts\python.exe -m pytest tests/database/test_player_recent_records.py server/gamestate/verifier/test_record_replay.py -q
```

纯牌谱测试无需数据库；集成测试需要环境变量 `OM_RECENT_TEST_DSN` 指向专用测试 PostgreSQL。每项集成测试创建独立 schema 并在结束后清理，勿指向生产数据库。
