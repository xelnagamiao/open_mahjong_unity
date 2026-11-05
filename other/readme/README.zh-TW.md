# open_mahjong_unity 開發文檔

[简体中文](../../README.md) | [繁體中文](README.zh-TW.md) | [日本語](README.ja.md) | [English](README.en.md) | [Français](README.fr.md) | [Deutsch](README.de.md) | [Русский](README.ru.md)

## 概述

歡迎使用 open_mahjong_unity！

Web端測試網址 [https://salasasa.cn](https://salasasa.cn)

PC端Steam頁 [https://store.steampowered.com/app/4565740/Salasasa/](https://store.steampowered.com/app/4565740/Salasasa/)

Salasasa麻將平臺測試羣 906497522

## 項目簡介

open_mahjong_unity是一款基於unity/python-fastapi的麻將平臺項目，該項目遵循MIT許可協議、免費、開源、支持PC/安卓/Web三端互通；目標是支持全世界所有的麻將規則、並且提供給玩家自定義規則的選項。歡迎加入qq羣參與討論、協助和測試。

## 1.許可說明

本項目採用 MIT 許可證（詳見 LICENSE 文件）。

該MIT許可證授權範圍如下：

除明確排除的部分外，本項目的所有源代碼及文件均可根據 MIT 許可證的條款自由使用、複製、修改、合併、發佈、分發、再許可和/或銷售，前提是保留原始版權聲明和本許可聲明，以下是明確排除部分的詳細描述：

### 1.1 項目貢獻者保留部分權利、或不完整授權的引用資源

資源文件夾下僅授權用於本項目的資產，以及遵循其他聲明或開源協議的借物、包括open_mahjong_unity/Assets/Resources 目錄及其所有子目錄和文件，不適用 MIT 許可證。該目錄中的內容（包括但不限於圖像、音頻、模型、配置文件等資源）僅供本項目內部使用，未經版權所有者事先書面許可，不得被提取、複製、修改、分發、再許可、用於其他項目，或用於任何商業或非商業用途，版權所有者保留對未明確授權內容的全部權利。
若 open_mahjong_unity/Assets/Resources 下的某個子目錄中包含獨立的許可證文件（如 LICENSE、LICENSE.txt 等），則該子目錄中的內容以該獨立許可證爲準，優先於上述限制。

此外，如果您僅以非商業目的在個人服務器或私有環境中部署本項目（包括採用本相同聲明的修改版或分支），則視爲在本項目範圍內獲得了對 Resources 文件夾中資源的使用授權，這一聲明超出上述許可證限制，這代表如果您部署時不考慮任何商業用途，就自動擁有了Resources下任意資源的使用和修改權；但該授權仍然不得擴展至獨立提取或在其他項目中單獨使用這些資源，如果您想要以商業形式部署本項目，需要替換Resources文件夾中未被授權的資產，也可以嘗試聯繫項目維護者或資源提供者、獲得個別資源的書面授權。

### 1.2 第三方規則貢獻者的權利保留聲明

本條款是對 MIT 許可證的特別補充與限制。本項目中包含的由第三方貢獻者提供的特定遊戲規則變體、玩法邏輯及數值配置代碼（通常位於 /calculation/目錄或明確標註規則貢獻者信息的文件），在默認狀態下遵循 MIT 許可證條款，允許使用者進行商業運營、公共服務器部署、任意分發及修改。但是，各規則內容的原始貢獻者仍保留對其獨創玩法邏輯的著作權，擁有單方面收回其商業使用權的權利。
如果您部署的平臺與規則創作者之間產生了某些爭議，或者貢獻者通過項目倉庫公告、代碼移除或書面通知的方式聲明終止授權，使用者須在收到通知後的合理期限內(默認爲30天)，停止基於該特定規則的公共服務與商業發行，這一點屬於不可控因素，希望可以理解。

### 1.3 關於Salasasa名稱和本項目的關係的聲明

“Salasasa” 名稱以及網址 salasasa.cn 是項目示例服務器的標識與域名。在基於 open_mahjong_unity 創建獨立分支或修改版本時，請遵守以下約定：
    1.不得使用 “Salasasa” 原名稱來命名自己的服務器，但以個人學習、維護特定規則爲目的，不以商業形式公開發行或得到臨時許可的除外；
    2.未經許可，不得在“Salasasa”命名以外的客戶端中繼續使用 salasasa.cn 的 API 或服務器服務；
    3.基於 “Salasasa” 命名的客戶端變體允許存在，但必須在獲取許可的同時，在客戶端的版本驗證 API 中顯式標註該版本爲非官方修改版；
    4.如果客戶端變體出現網絡攻擊、惡意行爲或嚴重影響服務器正常運行的情況，salasasa 保留要求停止發佈、封禁相關客戶端，以及採取法律措施維權的權利。

## 2.項目結構

倉庫由 Unity 客戶端、Python 遊戲服務器、Node.js/Vue Web 平臺和 Go 聊天服務器組成，other文件夾保存默認的Ngnix配置、段位配表、美術素材的原始格式等可能會幫助到你的內容。

```text
open_mahjong_unity/
├── open_mahjong_unity/             # Unity 遊戲客戶端
│   ├── Assets/
│   │   ├── Editor/                 # Unity 編輯器擴展
│   │   ├── Plugins/                # 第三方插件
│   │   ├── Prefabs/                # UI、房間及牌桌預製體
│   │   ├── Resources/              # 模型、圖片、字體、音效等運行時資源
│   │   ├── Scenes/                 # Unity 場景
│   │   ├── Scripts/
│   │   │   ├── ChatServer/         # 聊天功能
│   │   │   ├── Config/             # 客戶端配置與用戶數據
│   │   │   ├── GameScene/          # 對局、牌桌、計分及各規則表現
│   │   │   ├── GameSceneConfig/    # 牌桌外觀配置
│   │   │   ├── Network/            # HTTP/WebSocket 通信
│   │   │   ├── Rendering/          # 渲染相關功能
│   │   │   ├── Room/               # 房間創建與管理
│   │   │   └── UI/                 # 主界面與通用 UI
│   │   └── Shaders/                # 着色器
│   ├── Packages/                   # Unity 包配置
│   └── ProjectSettings/            # Unity 項目設置
│
├── open_mahjong_server/            # Python/FastAPI 遊戲服務器
│   ├── main.py                     # 服務啓動入口
│   ├── server/
│   │   ├── server.py               # FastAPI 與 WebSocket 主服務
│   │   ├── chat_server/            # 聊天服務接入
│   │   ├── database/               # 數據庫、統計與牌譜存儲
│   │   ├── event/                  # 賽事功能
│   │   ├── friend/                 # 好友系統
│   │   ├── game_calculation/       # 和牌、聽牌及規則計算
│   │   ├── gamestate/              # 各麻將規則的對局狀態機
│   │   ├── match/                  # 匹配系統
│   │   ├── public/                 # 公共服務與限流等模塊
│   │   ├── room/                   # 房間管理
│   │   └── webapi/                 # Web 計算接口
│   ├── scripts/                    # 維護與遷移腳本
│   ├── load_test/                  # 負載測試
│   ├── pyproject.toml              # Python 項目配置
│   ├── requirements.txt            # Python 依賴
│   ├── uv.lock                     # uv 依賴鎖定
│   └── start_server.bat            # Windows 啓動腳本
│
├── open_mahjong_web/               # Node.js/Vue 3 Web 平臺
│   ├── client/                     # Vue 3 前端
│   │   ├── public/                 # 靜態資源與 Unity WebGL 文件
│   │   ├── src/
│   │   │   ├── api/                # HTTP 客戶端
│   │   │   ├── components/         # 通用組件
│   │   │   ├── composables/        # Vue 組合式邏輯
│   │   │   ├── game2d/             # 2D 對局前端
│   │   │   ├── layouts/            # 頁面佈局
│   │   │   ├── router/             # 路由
│   │   │   ├── stores/             # Pinia 狀態管理
│   │   │   ├── styles/             # 全局樣式
│   │   │   ├── utils/              # 前端工具
│   │   │   └── views/              # 頁面
│   │   ├── package.json
│   │   └── vite.config.js
│   ├── server/                     # Express/Socket.IO 後端
│   │   ├── index.js                # Node.js 服務入口
│   │   ├── botapi/                 # 機器人接口
│   │   ├── config/                 # 數據庫及環境配置
│   │   ├── guessfan/               # 猜番對抗服務
│   │   ├── middleware/             # 鑑權與限流中間件
│   │   ├── routes/                 # Web API 路由
│   │   ├── services/               # 服務層
│   │   └── utils/                  # 服務端工具
│   ├── docs/                       # Web 部署與功能文檔
│   ├── scripts/                    # 構建、部署腳本
│   ├── package.json
│   └── start.bat
│
├── open_mahjong_chatServer/        # Go 聊天服務器
│   ├── Main.go                     # 程序入口
│   ├── ConnectPool.go              # 連接池
│   ├── RoomManager.go              # 聊天房間管理
│   ├── go.mod
│   └── go.sum
│
├── other/                          # 美術源文件、實驗代碼、規則資料與輔助素材
├── LICENSE
└── README.md
```

## 3.技術棧

### 遊戲客戶端 (open_mahjong_unity)

- **引擎**: Unity 6.4 (6000.4.7f1)
- **語言**: C#
- **網絡**: WebSocket (Nativewebsocket)
- **平臺**: PC/Web/Android/iOS

### 遊戲服務器 (open_mahjong_server、open_mahjong_chat_server)

- **框架**: FastAPI
- **語言**: Python 3.12 Golang
- **網絡**: WebSocket, HTTP
- **數據庫**: PostgreSQL 18
- **部署**: supervisor 或 任意您喜歡的任務管理器

### Web 平臺 (open_mahjong_web)

- **前端**: Vue3
- **後端**: Node.js
- **數據庫**: PostgreSQL 18

### 4.交流

- **Salasasa平臺測試羣**: 906497522
- **open_mahjong_unity開發交流羣**: 1084537740
- **Telegram羣組**: t.me/open_mahjong_unity
- **Discord**: [https://discord.gg/RqvxDDgdFH](https://discord.gg/RqvxDDgdFH)
- **項目負責人微信**: q1448826180
- **項目地址**: [https://github.com/xelnagamiao/open_mahjong_unity](https://github.com/xelnagamiao/open_mahjong_unity)
- **語雀文檔(未完成)**: [https://www.yuque.com/xelnaga-yjcgq/zkwfgr/lusmvid200iez36q?singleDoc#](https://www.yuque.com/xelnaga-yjcgq/zkwfgr/lusmvid200iez36q?singleDoc#)
- **開發進度表**: [https://docs.qq.com/sheet/DZkh2a2VBQkpucXNr?tab=BB08J2](https://docs.qq.com/sheet/DZkh2a2VBQkpucXNr?tab=BB08J2)
- **贊助**: q1448826180
- **其他**: 蘇南上海杭州的崗位或定製開發及合作可聯繫qq或微信

### 5.鳴謝

開發團隊：Xelnaga(主程)  雪枫XueFun9(牌面提供者)  Zoe(隨機種子設計)  Natsuki(新編MCR編著者)  坏狗影子(表情包提供者)  GitHub/baisebaoma(高性能羅伯特設計) troooobot(長沙麻將實現)  Zoe(臺灣麻將實現)  二階堂 ヒロ(中心盤設計)  莫莫柴(青雀、2D桌面)  Shq、Zoe、ShiauLo(bug修復)  salasasa数据bot(PCX1078)  大海(k麻計分)  插件開發(新手求教)

自定義規則設計：莫莫柴(青雀)  关兆豪先生(中庸麻將)  Null(虹雀)  K神传说(K神麻將)  恋绘色(香港清章戀繪色魔改)  小林(國標小林改)  蓝十(國標藍十改)  自恧(浪湧麻將)  南瓜饼(南雀)

地方規則考據：上海清混碰(何苏)  古典麻將(莫莫柴、何苏、古明地梦)  宜興麻將(苦鱼)  Mil推廣規則(MIL国际麻将联盟)

直播宣傳：Cloud980Ti  轻轻的飘  等候1234  立直喵  Ce/Est1al  鬼头刀鱼fish  神源之心  方片杰克  Ni9e_9  大柠劳斯  大口啃排骨

贊助：九曜、kiki、健哥、何苏、Null、莫莫柴、恋绘色、Zazaka、中山大学国标麻将同好会、东西喵、山东大学老年人活动中心、Sw123

特別感謝：莫莫柴、码龙、Null、坏狗影子、何苏、FFMJ法国麻将协会(翻譯支持)

支持社團：棋牌游戏研究院、立直麻雀研习社、柴の麻将群、雀渣玩家群、中山大学国标麻将同好会

早期測試：夜色祢 chlorine 陪练的命运

以及遊玩salasasa平臺的所有玩家！

*最後更新：2026年11月3日 dev ver 0.5.87.0*
