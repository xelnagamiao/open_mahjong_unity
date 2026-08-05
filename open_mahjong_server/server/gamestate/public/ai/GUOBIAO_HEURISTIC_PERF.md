# 高性能罗伯特：速度优化

来源：哈基明（github.com/baisebaoma）。相对初版移植的半庄测速约 **25×**；决策语义不变。算法行为画像见 [`GUOBIAO_HEURISTIC_BOT.md`](./GUOBIAO_HEURISTIC_BOT.md)；一般型向听出处见 [`ATTRIBUTION.md`](./ATTRIBUTION.md)。

下用“OMC”代表“哈基明麻将”，可以理解为是速度优化前的“高性能罗伯特”数据，只是在不同的平台上跑出来的。

## 背景

近听时假想番会多次调用 `Chinese_Hepai_Check`（`guobiao_hepai_check.py`）。初版移植有两处拖慢半庄：

1. **缺一向听 memo**：合法听前瞻与假想番路径重复算向听 / 够番权重。
2. **检番入口无条件 `print`**：热路径刷屏 I/O，墙钟被放大。

说明：PyPI `mahjong` 是**牌效罗伯特**用的日麻向听库，与高性能罗伯特无关。本 bot 向听为自写 `guobiao_shanten`（一般型 DP 见 ATTRIBUTION / kobalab）。

## 优化（两轮合并）

| 层 | 改动 | 文件 |
|----|------|------|
| 检番 | 入口无条件 `print` → `debug_print`（仅 `debug=True` 输出） | `game_calculation/guobiao_hepai_check.py` |
| 听牌 | 热路径无条件 `print` → `debug_print` | `game_calculation/gb_tingpai_check.py` |
| 启发式 | 一向听 `shanten_memo` / `qualifies_memo`；假想番 `fan_memo`（单手决策内）；`Chinese_Hepai_Check` 单例（**不做**跨决策全局检番结果缓存） | `guobiao_heuristic_logic.py` |
| 向听热路径 | 套装 DP 缓存；稀疏 / 增量 pack；`effective_tiles` 候选剪枝；死一向听就地扫描（保语义） | `guobiao_shanten.py` 等 |

未拆 memo/剪枝；向听剪枝/缓存与全量枚举一致（fuzz 0 mism）。

**语义修正（相对曾用 `specials=False` 的厚度扫描）**：死一向听重塑厚度改回完整 `specials=True`，与主决策一致。另：检番入口对 `combination_list` / `way` 做拷贝，避免 `暗转明` 等就地修改污染后续假想番与全局 scorer 缓存。

## 流局口径（假想番 / 听口对齐）

四席同策 10 全庄 `72001–72010`（160 局）：

| 阶段 | 和 | 流局 | 流局率 | 半庄墙钟 (72001) |
|------|---:|-----:|-------:|-----------------:|
| 假想番调用对齐前（约） | — | — | **~6.88%** | — |
| + 自摸 14 张 /「和单张」/ 杠护听口 | 153 | 7 | **4.38%** | ~12.6 s |
| + `tenpai_wait_tiles`（`GB_tingpai` 对齐 `waiting_tiles`） | 153 | 7 | **4.38%** | **11.6 s** |
| + 鸣后切牌补全 L-D/L-H 字段 | 153 | 7 | **4.38%** | **11.6 s** |
| + 拒鸣落入死一向听 + `hepai_check` way 拷贝 | 158 | 2 | **1.25%** | **11.9 s** |

相对优化前半庄 **324.5 s** ≈ **27×**。本 10 种子流局 **1.25%**（低于目标带下沿 ~2%；160 局噪声大，方向已过目标带）。

### 听口 /「和单张」

- **根因**：`effective_tiles` 跳过手内已 4 枚的牌种，但实装 `GB_tingpai` / `waiting_tiles` 仍可能计入 → 误判独听 → 假加「和单张」→ 假合法听。
- **修复**：`tenpai_wait_tiles()` 优先 `Chinese_Tingpai_Check.tingpai_check`；`analyze_live_waits` / `qualifying_wait_weight` 用其判听种与 `single_wait`。
- **回归**：`test_four_of_kind_blocks_false_single_wait`；单测 **33 passed**。
- **流局残局画像**（hook `player_action_record_liuju`，同上 10 种子）：7 流局、合法听座位 15、假听 0、`waiting`↔`ting` 漂移 0。流局时多为薄合法听（含绝张 live=1）互不放铳，不是假听驻留。

### 中盘：拒鸣落入死一向听

- **根因**：弃牌侧已有「死一向听 → 二向听重塑」，但鸣牌只要向听数字推进就接受，可跳进「一摸无法达合法听」的死形，早锁薄听。
- **修复**：`evaluate_claim` 在 `shanten_after == 1` 时要求 `qualifying_tenpai_ukeire_one_draw > 0`，否则拒鸣。另：`hepai_check` 每拆解拷贝 `way`，避免 `暗转明` 串拆解（与 `hepai_decompose` 对齐）。
- **回归**：`test_reject_claim_into_dead_one_shanten`。
- **本 10 种子**：流局 **7 → 2**（4.38% → **1.25%**）；半庄 72001 **11.9 s**。

### 其它 A/B（同 10 种子，未合入）

| 尝试 | 流局率 | 结论 |
|------|-------:|------|
| 荣和海底仅 `wall==0`（更贴 Unity `last_cut`） | 5.00% | 变差；保留 OMC 式 `wall<=1` 前瞻 |
| 关闭绝张假想 | 4.38% | 无变化 |
| 鸣后切牌补 L-D/L-H | 4.38% | 本种子集决策无可见差 |

说明：OMC headless 同种子 10 全庄可得 **2.50%**，但牌山 RNG / 引擎与 Unity 服务端不同，**不能**直接当移植回归金标；长样本 OMC 画像仍约 **2.53%**。

## 测速

**口径**（与 smoke 一致）：四席高性能罗伯特、`guobiao/standard`、`hepai_limit=8`、`tactical_call=true`、`game_round=2`（半庄）、seed=`72001`、`fast_sleep`（测速时压低 `_BOT_DELAY`）。

| 阶段 | 墙钟（约） |
|------|-----------|
| 优化前 | **324.5 s** |
| 检番 + 启发式 memo / 缓存 | **48 s** |
| + 向听热路径 | **12.7 s** |
| 假想番语义对齐后复测 | **12.6 s** |
| 听口对齐后复测 | **11.6 s** |
| 死一向听拒鸣后复测 | **11.9 s** |

单测：`test_guobiao_heuristic.py` **33 passed**。

## 复测

在 `open_mahjong_server` 目录：

```powershell
python -m pytest server/gamestate/public/ai/test_guobiao_heuristic.py -v

# 半庄测速（东+南）
$env:SMOKE_MODE="half"; python -m server.gamestate.public.ai.test_guobiao_heuristic_smoke
```

可选：`SMOKE_BASE_SEED=72001`（默认即此）。短全庄接线仍用：

```powershell
python -m pytest server/gamestate/public/ai/test_guobiao_heuristic_smoke.py -v -k two_quanzhuang
```

全庄流局（`guobiao_heuristic_drawrate.py`，**多进程按种子分片**，对齐 OMC `guobiao-drawrate.ts` 的 fork 模型；勿用 asyncio.gather / 线程池扛 CPU）：

```powershell
# 10 全庄（默认 workers≈cpu-2；便捷 wrapper）
python guobiao_heuristic_drawrate10.py

# 63 全庄（统一入口默认即 63）
python guobiao_heuristic_drawrate.py --matches 63 --workers 14

# 冒烟：4 全庄 × 2 进程
python guobiao_heuristic_drawrate.py --matches 4 --workers 2 --skip-half --out guobiao_heuristic_drawrate_smoke.json
```

`--workers` 默认 `min(matches, cpu-2)`；传 `1` 即串行。每 worker 独立 import/跑一段种子，主进程汇总和/流局/番种/耗时。

## 63 全庄复核（历史口径：隔步三色误改期间 · 已回退）

> **口径修正（2026-08-05）**：国标 MCR 三色三步高只允许**依次递增一位（连步 step=1）**，跨 2 的隔步（3-5-7 形）不计三色三步高。PR #98（add-guobiao-heuristic-bot）曾把主规则与 Web 计算器改成“连步+隔步”，属外部 PR 引入的误改，现已回退（服务端 `guobiao_hepai_check.py`、Web `gbHepai.ts` 恢复仅连步；Unity 端从未改动）。下文「修后」列即该误改窗口的测量口径，**不可**当作现行规则金标；「修前（仅连步）」才是与现行规则一致的数值。

**当时口径**：`Chinese_Hepai_Check` 三色三步高同时认**连步（step=1）+ 隔步（step=2）**（现已回退为仅连步）；另含死一向听拒鸣、听口对齐、`hepai` way 拷贝等。种子 **`72001–72063`**，四席高性能、`game_round=4`、fast_sleep、`--workers 14`。脚本：`python -m server.gamestate.public.ai.guobiao_heuristic_drawrate --matches 63 --workers 14 --seed 72001` → 包根 `guobiao_heuristic_drawrate63_result.json`（gitignore）。

| 指标 | 修后 63qz | 修前 63qz（仅连步） | OMC restore-gebu 63z | OMC 长样本（4030 局） |
|------|----------:|-------------------:|---------------------:|---------------------:|
| 局数 | 1008 | 1008 | 1008 | 4030 |
| 和 / 流局 | 986 / 22 | 984 / 24 | — | — / 102 |
| 和率 | **97.82%** | 97.62% | — | 97.47% |
| 流局率 | **2.18%** | **2.38%** | **2.48%** | **2.53%** |
| 副露率 | **59.7%** | 58.2% | **59.3%** | 69.3% |
| 非花均番 | 12.86 | 12.91 | — | 12.31 |
| 半庄墙钟 (72001) | **11.9 s** | 12.1 s | — | — |
| 63 全庄总墙钟 | **255 s**（14 workers） | 1927 s（串行） | — | — |
| 单全庄均值 | 4.1 s（墙钟/ok，并行） | **30.6 s**（串行） | — | — |

主番（≥4，出现次数/和牌）：

| 番种 | 修后 63qz | 修前 63qz | OMC 长样本 |
|------|----------:|----------:|-----------:|
| 三色三步高 | **31.4%** | **23.9%** | **34.4%** |
| 不求人 | 10.2% | 11.5% | 8.3% |
| 和绝张 | 8.4% | 10.4% | 7.4% |
| 七对 | 7.5% | 7.6% | 6.2% |
| 三色三同顺 | 7.2% | 7.8% | 6.4% |
| 花龙 | 5.2% | 6.2% | 5.5% |
| 组合龙 | 4.4% | 4.6% | 4.3% |
| 五门齐 | 3.7% | 3.6% | 4.3% |
| 混一色 | 1.8% | 2.0% | 3.7% |
| 全带幺 | 1.1% | 1.2% | 1.5% |
| 碰碰和 | 0.8% | 0.9% | 1.5% |

结论（历史口径）：误加隔步后三色三步高 **23.9% → 31.4%**（接近 OMC restore-gebu 63z **31.1%**）；该提升来自隔步误改，**回退后按现行规则仍为仅连步口径（约 23.9%）**。流局 **2.18%** 仍落在 ~2.5% 目标带附近；副露 **59.7%** 与 restore-gebu **59.3%** 几乎贴合。半庄相对加速前仍约 **27×**（11.9 vs 324.5）。

### 历史：修隔步前 63（死一向听拒鸣后 · 仅连步）

旧 `guobiao_heuristic_drawrate63_result.json` / 下文数字为**修隔步三色 hepai 之前**样本，即**仅连步口径**，与现行规则一致（隔步误改已回退）。流局 2.38%、副露 58.2%、三色 **23.9%**、半庄 12.1 s、串行总墙钟 1927 s；主番见上表「修前」列。当时“三色偏低”的判断基于 OMC 隔步口径，按国标仅连步规则并不偏低。

## 未竟项

1. ~~本 10 种子流局 **1.25%**……~~ → 修前 63：**2.38%**；修后 63：**2.18%**——均在 ~2.5% 目标带附近；10 种子偏低属噪声。
2. 假想番 vs OMC 逐番：Unity `Chinese_Hepai_Check` 与 OMC `getGuobiaoFanScore` 在多拆解/阻挡上大体同构；`hepai_check` way 拷贝已补。**注意**：OMC 口径含隔步三色，与本仓国标仅连步规则不一致，逐番对比时三色三步高以本仓规则为准。claimed fans strip 仅组队抢番局有意义，自由桌空集。
3. 保持 memo/剪枝/`specials=True`/print→debug；不要拆加速。
4. 副露率 Unity 修后 63qz **~60%** vs OMC 长样本 **69%** 仍有差距（与 restore-gebu 63z **59.3%** 已对齐）；三色三步热度“修后 **~31%**”为隔步误改口径，回退后按仅连步规则约为 **24%**（贴近人类参考 ~25%）。

## 生产缓存与机器人执行器

- `GUOBIAO_AI_CACHE_MB`：单个服务器进程的国标 AI 缓存预算，默认 `200` MiB。
- 持久缓存只使用预算的 80%（默认 160 MiB），其余 20% 留给单次决策 memo 和分配器余量。
- 持久配额按 `SUIT/SHANTEN/YIBAN/EFFECTIVE = 10%/50%/30%/10%` 分配，超过配额按 LRU 淘汰。
- `BOT_CPU_WORKERS`：跨房间机器人工作进程数，默认 `min(4, cpu_count-1)` 且至少 1。
- 同一房间的 AI 计算串行，不同房间可并行；主事件循环先生成快照，后台只运行纯计算，结果返回后校验 action tick。
- 多进程部署时缓存预算是每进程预算；总预算 200 MiB 时，应按 worker 数设置 `GUOBIAO_AI_CACHE_MB=200/worker_count`。

## 注意

- 生产手感：先算后补到最低思考墙钟 `_BOT_DELAY=0.5`（`elapsed < 0.5` 才 sleep 补齐；超过不补）。总思考 ≈ `max(算时, 500ms)`；鸣牌后的 `claim_meld_post_gap` 独立叠加，不计入地板。
- 测速用 `fast_sleep` + 压低 `_BOT_DELAY`，反映的是**算力墙钟**，不是线上手感。
