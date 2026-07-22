# Salasasa 2D（Vue + Pixi）部署

2D 国标客户端已经并入现有 Vue 应用，执行一次前端构建即可同时生成主站与 2D 页面：

```bash
cd open_mahjong_web/client
npm ci
npm run build
```

构建产物仍然是 `client/dist`。`unity-game` / `ios-game` / `android-game` 为人工安放的包（见仓库根旁 `deploy.config.json` 的 `manualGamePackages`）：Vite 构建不会从 `public/` 拷贝它们，生产同步静态文件时也必须跳过，避免覆盖线上 Unity。

以下路由都由同一个 Vue `index.html` 提供：

- `/2d`
- `/2d/game`
- `/2d/player/:id`

浏览器固定使用 `/2d/api/*` 和 `/2d/ws/*`。Nginx 在反向代理时分别映射到已有 Node `/api/*` 与 Python `/game/*`，不需要复制 API 路由：

```nginx
root /srv/www/salasasa;

location /2d/api/ {
    proxy_pass http://127.0.0.1:3000/api/;
    proxy_set_header Host $host;
    proxy_set_header X-Real-IP $remote_addr;
    proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
    proxy_set_header X-Forwarded-Proto $scheme;
}

location /2d/ws/ {
    proxy_pass http://127.0.0.1:8081/game/;
    proxy_http_version 1.1;
    proxy_set_header Upgrade $http_upgrade;
    proxy_set_header Connection "upgrade";
    proxy_set_header Host $host;
    proxy_read_timeout 3600s;
    proxy_send_timeout 3600s;
}

location /api/ {
    proxy_pass http://127.0.0.1:3000;
}

location / {
    try_files $uri $uri/ /index.html;
}
```

其中 `/2d/ws/{connectionId}` 会映射到现有 Python `/game/{connectionId}`。

## 与主站共享登录

2D 大厅读取主站 `player_token`，通过 WebSocket `login` 消息的 `token` 字段换游戏会话。游戏服需配置与网站一致的 JWT 密钥：

- 环境变量 `PLAYER_JWT_SECRET`（或回退 `ADMIN_JWT_SECRET`）
- 或在 `local_config.py` / `test_config.py` 中设置 `player_jwt_secret`

密钥需与 `open_mahjong_web` 的 `PLAYER_JWT_SECRET`（缺省则 `ADMIN_JWT_SECRET`）一致，`aud` 为 `player`。

本地开发由 `client/vite.config.js` 完成同样的转发：Node 默认使用 `3000`，Python 游戏服务默认使用 `8081`。

独立 React 项目 `open_mahjong_2d` 已废弃并移除，请只维护本 Vue 客户端。
