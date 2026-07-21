# Salasasa 2D

基于朋友授权的 `mmcr14.online` 前端代码与视觉资产改造的 Salasasa 国标 2D 客户端，保留其 logo、内嵌字体、SVG 牌面、音效和 PixiJS 牌桌实现。它不引入新服务端，也不修改现有服务端；Unity 3D 和本客户端共用同一登录连接、匹配队列及国标对局实例。

## 功能范围

- 已保留：账号登录、12 个国标排位队列、排行榜、公开玩家资料、2D 对局、断线恢复。
- 未提供：自定义房间、其他规则对局、牌谱和独立统计页面。
- 公共数据继续来自本站已有玩家 API。

## 本地运行

复制 `.env.example` 为 `.env.local`，按本机实际端口调整两个上游，然后运行：

```bash
npm ci
npm run dev
```

开发页面为 `http://localhost:5173/2d/`。Vite 会执行以下纯代理映射：

- `/2d/api/*` → 现有 Web 服务 `/api/*`
- `/2d/ws/{connectionId}` → 现有游戏服务 `/game/{connectionId}`

## 生产部署

运行 `npm run build`，将 `dist` 内容部署到 `/srv/www/salasasa/2d/`（或按实际站点目录调整）。Nginx 示例见 `deploy/nginx-2d.conf.example`。代理配置是站点入口层配置，不需要改 Python 或 Node 服务端代码。

浏览器会在当前标签页的 `sessionStorage` 保存登录凭据，以便同一条游戏 WebSocket 断线后自动重新登录和恢复牌局；关闭标签页后会清除。
