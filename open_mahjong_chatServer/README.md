# 聊天服务

此目录提交 Go 源码、依赖文件和 Windows 本地测试用的 `open_mahjong_chatServer.exe`。
同一程序也保留在 `open_mahjong_server/server/chat_server/`，供 Python 测试服务自动启动。
这两份程序是仓库提供给使用者的测试配套文件，无需使用者先安装 Go 并自行编译。

Python 的测试配置启用 `auto_create_chatserver` 时，先生成聊天密钥，再从上述
`server/chat_server/` 目录启动程序。运行时生成的 `secret_key.txt` 不需要提交。

修改 Go 源码后，在本目录的 PowerShell 中重新编译并同步两份 Windows 程序：

```powershell
go build -o open_mahjong_chatServer.exe .
Copy-Item -LiteralPath .\open_mahjong_chatServer.exe -Destination ..\open_mahjong_server\server\chat_server\open_mahjong_chatServer.exe
```

其他平台可在本目录自行编译：

```sh
go build -o open_mahjong_chatServer .
```

如需 Python 的本地聊天启动入口，将其他平台的编译结果放到
`open_mahjong_server/server/chat_server/`；这些本机构建结果被 Git 忽略。

程序从工作目录读取 `secret_key.txt`。Python 游戏服务的 `ChatServer` 会生成密钥；
部署独立聊天服务时，让它读取与游戏服务一致的密钥文件。密钥仅留在运行环境，
不提交 Git。生产环境配置由部署环境单独管理。
