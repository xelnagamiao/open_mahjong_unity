# 虹雀牌谱番值修复

虹雀 v1.6 的和牌 tick 第 4 项保存番种标签。标签格式为 `番名*达成次数|合计番值`，次数为 1 时省略 `*1`。例如：

```json
["双同顺*2|4", "清顺*4|4", "门清|1"]
```

`|` 后是该条目的合计番值，直接供客户端结算面板和计分板展示；`*` 后是达成次数，供番种统计使用。两个双同顺是 4 番、2 次，不能把番值当成次数。和牌 tick 的分数仍是原有的 `hu_score`，此修复不改计分规则、得分、排名或动作顺序。

曾经写入的仅含番名/次数的标签缺少 `|合计番值`，现有客户端会显示成 0 番。服务端 `game_hongque.record.fan_labels` 现在同时写入次数和计分器的 `total`，现有客户端即可正确显示。客户端不增加旧格式猜值代码。

## 一次性修复存量

先更新并重启服务端，避免继续产生缺少番值的记录。在 `open_mahjong_server` 目录执行一次性脚本。脚本不会在服务器启动或牌谱读取时自动运行。

```powershell
# 只读预检（默认）
& .venv/Scripts/python.exe scripts/repair_hongque_record_fans.py --database
# 写入修复；与原始记录备份在同一事务内提交
& .venv/Scripts/python.exe scripts/repair_hongque_record_fans.py --database --apply
```

连接使用 `HONGQUE_RECORD_DATABASE_URL`；未设置时与服务端一样选择 `server/local_config.py` 或 `server/test_config.py`。上线时须针对相应环境执行，代码更新本身不会修复远端存量。

脚本仅处理虹雀 v1.6，按固定的 v1.6 番表补回缺少的番值。已显式保存的番值保留。非虹雀牌谱不变。任意未知番名、畸形标签或未知版本都会导致整批回滚；不跳过错误后发布部分修复。重复运行无数据改动。

修改前的完整记录保存在 `hongque_record_fan_backups`，以 `game_id` 标识，重复执行不会覆盖首次备份。回滚时可按需要将备份记录恢复至相同 `game_id`，不必恢复其他业务表。迁移不会改写番种统计，因为达成次数保持不变。重新打开牌谱即可读取修正后的记录。

已导出的 JSON 可另存修正副本：

```powershell
& .venv/Scripts/python.exe scripts/repair_hongque_record_fans.py --input old.json --output repaired.json
```

脚本拒绝覆盖原件或既有输出文件。省略 `--output` 仅预检。

## 回归

`server/gamestate/game_hongque/test_hongque_record_fans.py` 覆盖四个庄位的自摸、点和、多家和，以及番值和统计次数与权威结算的一致性。

`tests/database/test_hongque_record_fan_migration.py` 覆盖实际牌局往返迁移、零番、未知番名、非虹雀隔离、原件保留、幂等、文件覆盖保护，以及独立 PostgreSQL schema 内的只读预检、事务备份和整批失败回滚。数据库测试使用 `SUBRULE_TEST_DATABASE_URL` 指定本地测试连接。
