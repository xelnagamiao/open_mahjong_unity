
Debug = True

if Debug:
    host = 'localhost'
    user = 'postgres'
    password = 'qwe123'
    database = 'open_mahjong'
    port = 5432
    auto_create_chatserver = True
else:
    # 在项目进入生产环境部署时请自定义local_config文件
    import local_config
    host = local_config.host
    user = local_config.user
    password = local_config.password
    database = local_config.database
    port = local_config.port
    auto_create_chatserver = False