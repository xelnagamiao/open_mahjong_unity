## Default Config
## 未配置环境的默认配置

class Config:
    host = 'localhost'  #
    user = 'postgres'
    password = 'qwe123'
    database = 'open_mahjong'  # 注意：数据库名是 open_mahjong，不是 postgres
    port = 5432
    auto_create_chatserver = True
    # 是否输出日志到控制台
    logging_do_stream_handler = True
    release_version = 18
    # 与 open_mahjong_web/.env 中 PLAYER_JWT_SECRET（缺省则 ADMIN_JWT_SECRET）保持一致，供网站 token 登录游戏服
    player_jwt_secret = 'dev-admin-jwt-change-in-production'
    player_jwt_audience = 'player'
