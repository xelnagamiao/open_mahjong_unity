# 本地隔离工作区（勿提交内容）

本目录用于存放**不进 Git** 的本地临时物与密钥。根目录 `.gitignore` 已忽略除本 README 外的全部内容。

## 目录约定

| 路径 | 用途 |
|---|---|
| `secrets/` | SSH 私钥等（如 `salasasa.pem`） |
| `deploy/` | 部署打包暂存（`*.tar.gz`、远端脚本） |
| `upstream/` | 上游参考 clone（如 mmcr14、riichi-mahjong-tiles） |
| `tmp/` | 其它临时文件（含牌谱转换调试样例等） |

`tmp/tziakcha/` 现含：fetcher 包、`tziakcha_to_salasasa` 脚本、抓取的 HTML、样例 `*.json` / `*.salasasa.json`。正式转换逻辑在 `open_mahjong_web/client/src/utils/recordConvert/`。

当前上游参考：

- `upstream/_upstream_mmcr14` ← https://github.com/SlinkierApple13/mmcr14.online.git
- `upstream/_upstream_riichi_tiles` ← https://github.com/fluffystuff/riichi-mahjong-tiles.git

运行时不依赖这些目录；2D 牌面已 vendored 到 `open_mahjong_web/client/public/game2d-assets/`。

## 部署

打包与上传请使用 `deploy/`，**不要**再使用仓库根下的 `.deploy_staging/`（已废弃，且已加入 ignore）。

上传目标仍为远端 `/tmp/om_deploy/`；部署完成后可清理本目录 `deploy/` 内大文件。

## SSH 示例

```powershell
ssh -i "d:\open_mahjong_unity\.om_workspace\secrets\salasasa.pem" -o IdentitiesOnly=yes root@101.132.237.2 "命令"
```

私钥 ACL 须仅当前用户可读。勿把私钥内容写入规则、提交到 git 或贴进聊天。
