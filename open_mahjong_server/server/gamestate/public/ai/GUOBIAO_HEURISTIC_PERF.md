# 高性能罗伯特：速度优化

来源：哈基明（github.com/baisebaoma）。相对初版移植的半庄测速约 **25×**；决策语义不变。算法行为画像见 [`GUOBIAO_HEURISTIC_BOT.md`](./GUOBIAO_HEURISTIC_BOT.md)；一般型向听出处见 [`ATTRIBUTION.md`](./ATTRIBUTION.md)。

## 背景

近听时假想番会多次调用 `Chinese_Hepai_Check`（`guobiao_hepai_check.py`）。初版移植有两处拖慢半庄：

1. **缺一向听 memo**：合法听前瞻与假想番路径重复算向听 / 够番权重。
2. **检番入口无条件 `print`**：热路径刷屏 I/O，墙钟被放大。

说明：PyPI `mahjong` 是**牌效罗伯特**用的日麻向听库，与高性能罗伯特无关。本 bot 向听为自写 `guobiao_shanten`（一般型 DP 见 ATTRIBUTION / kobalab）。

## 优化（两轮合并）

| 层 | 改动 | 文件 |
|----|------|------|
| 检番 | 入口无条件 `print` → `debug_print`（仅 `debug=True` 输出） | `game_calculation/guobiao_hepai_check.py` |
| 启发式 | 一向听 `shanten_memo` / `qualifies_memo`；假想番 `fan_memo`（单手决策内）；`Chinese_Hepai_Check` 单例（**不做**跨决策全局检番结果缓存） | `guobiao_heuristic_logic.py` |
| 向听热路径 | 套装 DP 缓存；稀疏 / 增量 pack；`effective_tiles` 候选剪枝；死一向听就地扫描（保语义） | `guobiao_shanten.py` 等 |

未改决策优先级、假想番规则或起和番门闩；向听剪枝/缓存与全量枚举一致。

**语义修正（相对曾用 `specials=False` 的厚度扫描）**：死一向听重塑厚度改回完整 `specials=True`，与主决策一致。另：检番入口对 `combination_list` / `way` 做拷贝，避免 `暗转明` 等就地修改污染后续假想番与全局 scorer 缓存。

## 测速

**口径**（与 smoke 一致）：四席高性能罗伯特、`guobiao/standard`、`hepai_limit=8`、`tactical_call=true`、`game_round=2`（半庄）、seed=`72001`、`fast_sleep`（测速时压低 `_BOT_DELAY`）。

| 阶段 | 墙钟（约） |
|------|-----------|
| 优化前 | **324.5 s** |
| 检番 + 启发式 memo / 缓存 | **48 s** |
| + 向听热路径 | **12.7 s** |

相对原约 **25×**。同种子终局分仍为 **`[56, 39, -27, -68]`**。

单测：`test_guobiao_heuristic.py` **28 passed**。

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

## 注意

- 生产手感：先算后补到最低思考墙钟 `_BOT_DELAY=0.5`（`elapsed < 0.5` 才 sleep 补齐；超过不补）。总思考 ≈ `max(算时, 500ms)`；鸣牌后的 `claim_meld_post_gap` 独立叠加，不计入地板。
- 测速用 `fast_sleep` + 压低 `_BOT_DELAY`，反映的是**算力墙钟**，不是线上手感。
