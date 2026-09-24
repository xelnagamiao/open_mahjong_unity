# 聊天服务

此目录提交 Go 源码与依赖文件，不提交预编译程序或运行时密钥。

在本目录编译：

```sh
go build -o open_mahjong_chatServer .
```

Windows 可使用 `go build -o open_mahjong_chatServer.exe .`。
如需 Python 的本地聊天启动入口，将编译结果放到
`open_mahjong_server/server/chat_server/`，这些输出均被 Git 忽略。

程序从工作目录读取 `secret_key.txt`。Python 游戏服务的 `ChatServer` 会生成密钥；
部署独立聊天服务时，让它读取与游戏服务一致的密钥文件。密钥仅留在运行环境，
不提交 Git。配置、密钥和编译结果由各运行环境自行管理。
