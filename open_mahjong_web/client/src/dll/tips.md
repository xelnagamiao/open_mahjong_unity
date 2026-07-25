# 听牌 / 牌型计算

- **2D 国标对局提示**：本地 `client/src/game2d/calc/guobiao/`（`tingpaiCheck` / `hepaiCheck` / `buildLocalWaitData`），不依赖游戏服下发。
- Unity 端提示仍用 C# `GBtingpai` / `GBhepai` + `TipsContainer`。
- 工具页国标算分 / 听牌仍走 Node 代理 `CALC_SERVER`（`/api/mahjong/gb/*`），与对局提示无关。
