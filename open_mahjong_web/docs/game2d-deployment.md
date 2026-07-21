# Salasasa 2D（Vue + Pixi）部署

2D 国标客户端已经并入现有 Vue 应用，执行一次前端构建即可同时生成主站与 2D 页面：

```bash
cd open_mahjong_web/client
npm ci
npm run build
```

构建产物仍然是 `client/dist`。以下路由都由同一个 Vue `index.html` 提供：

- `/2d`
- `/2d/game`
- `/2d/player/:id`

Node 已将 `/2d/api/*` 内部改写为已有 `/api/*`，不需要复制 API 路由。Nginx 需要把游戏 WebSocket 单独转发给 Python 游戏服务：

```nginx
root /srv/www/salasasa;

location /2d/api/ {
    proxy_pass http://127.0.0.1:3000;
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

其中 `/2d/ws/{connectionId}` 会映射到现有 Python `/game/{connectionId}`。Python 服务端不需要修改。

本地开发由 `client/vite.config.js` 完成同样的转发：Node 默认使用 `3000`，Python 游戏服务默认使用 `8081`。
