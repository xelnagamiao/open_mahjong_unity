# open_mahjong_unity Development Documentation

[简体中文](../../README.md) | [繁體中文](README.zh-TW.md) | [日本語](README.ja.md) | [English](README.en.md) | [Français](README.fr.md) | [Deutsch](README.de.md) | [Русский](README.ru.md)

## Overview

Welcome to open_mahjong_unity!

Web test site: [https://salasasa.cn](https://salasasa.cn)

PC version on Steam: [https://store.steampowered.com/app/4565740/Salasasa/](https://store.steampowered.com/app/4565740/Salasasa/)

Salasasa mahjong platform testing group on QQ: 906497522

## Project Introduction

open_mahjong_unity is a mahjong platform built with Unity and Python/FastAPI. It is free and open source under the MIT license, with cross-platform play across PC, Android and Web. The goal is to support every mahjong ruleset in the world and give players options to customize the rules. Join our QQ group to discuss the project, contribute and help test it.

## 1. Licensing

This project uses the MIT license (see the LICENSE file).

The scope of this MIT license is as follows:

Except for the explicitly excluded portions, all source code and files in this project may be freely used, copied, modified, merged, published, distributed, sublicensed and/or sold under the terms of the MIT license, provided that the original copyright notice and license notice are retained. The exclusions are detailed below.

### 1.1 Resources for which project contributors retain certain rights or do not grant a full license

Assets in the resources folder that are licensed only for this project, as well as borrowed resources governed by other notices or open-source licenses, including the `open_mahjong_unity/Assets/Resources` directory and all its subdirectories and files, are not covered by the MIT license. The contents of this directory, including but not limited to images, audio, models and configuration files, are for use within this project only. Without prior written permission from the copyright holder, they may not be extracted, copied, modified, distributed, sublicensed, used in other projects, or used for any commercial or non-commercial purpose. Copyright holders reserve all rights not expressly granted.
If a subdirectory under `open_mahjong_unity/Assets/Resources` contains its own license file, such as LICENSE or LICENSE.txt, that license governs the contents of the subdirectory and takes precedence over the restrictions above.

In addition, if you deploy this project on a personal server or in a private environment solely for non-commercial purposes, including a modified version or fork carrying the same declaration, you are deemed to have permission to use the resources in the Resources folder within the scope of this project. This declaration extends beyond the restrictions above: if your deployment has no commercial purpose, you automatically have the right to use and modify any resource under Resources. This permission still does not extend to extracting those resources independently or using them separately in other projects. For commercial deployment, you must replace assets in Resources for which you have not obtained permission, or contact the project maintainers or resource providers to obtain written permission for individual resources.

### 1.2 Reservation of rights by third-party ruleset contributors

This clause is a special supplement to and limitation of the MIT license. Code for specific game rule variants, gameplay logic and numerical configurations supplied by third-party contributors, usually located in the `/calculation/` directory or in files that identify the ruleset contributor, is by default governed by the MIT license. Commercial operation, deployment on public servers, distribution and modification are permitted. However, the original contributors retain copyright in their original gameplay logic and reserve the right to unilaterally revoke permission for its commercial use.
If a dispute arises between your deployed platform and a ruleset creator, or a contributor declares the termination of permission through a repository announcement, removal of code or written notice, you must stop public services and commercial releases based on that specific ruleset within a reasonable period after receiving notice, which is 30 days by default. Please understand that this is a factor outside the project's control.

### 1.3 Statement on the relationship between the Salasasa name and this project

The name “Salasasa” and the domain salasasa.cn identify the project's example server. When creating an independent fork or modified version of open_mahjong_unity, please observe the following:

1. Do not use the original name “Salasasa” for your own server, except for personal learning or maintaining a specific ruleset without a public commercial release, or where temporary permission has been granted;
2. Without permission, clients using a name other than “Salasasa” may not continue using the API or server services at salasasa.cn;
3. Client variants using the “Salasasa” name are permitted, provided that permission is obtained and the client's version verification API explicitly identifies the version as an unofficial modification;
4. If a client variant launches network attacks, behaves maliciously or seriously disrupts normal server operation, salasasa reserves the right to demand that distribution stop, block the relevant clients and take legal action to protect its rights.

## 2. Project Structure

The repository consists of a Unity client, a Python game server, a Node.js/Vue Web platform and a Go chat server. The other folder contains the default Nginx configuration, rank tables, original artwork files and other material that may be useful.

```text
open_mahjong_unity/
├── open_mahjong_unity/             # Unity game client
│   ├── Assets/
│   │   ├── Editor/                 # Unity editor extensions
│   │   ├── Plugins/                # Third-party plugins
│   │   ├── Prefabs/                # UI, room and table prefabs
│   │   ├── Resources/              # Runtime models, images, fonts and audio
│   │   ├── Scenes/                 # Unity scenes
│   │   ├── Scripts/
│   │   │   ├── ChatServer/         # Chat functionality
│   │   │   ├── Config/             # Client configuration and user data
│   │   │   ├── GameScene/          # Matches, tables, scoring and ruleset presentation
│   │   │   ├── GameSceneConfig/    # Table appearance settings
│   │   │   ├── Network/            # HTTP/WebSocket communication
│   │   │   ├── Rendering/          # Rendering functionality
│   │   │   ├── Room/               # Room creation and management
│   │   │   └── UI/                 # Main interface and common UI
│   │   └── Shaders/                # Shaders
│   ├── Packages/                   # Unity package configuration
│   └── ProjectSettings/            # Unity project settings
│
├── open_mahjong_server/            # Python/FastAPI game server
│   ├── main.py                     # Service entry point
│   ├── server/
│   │   ├── server.py               # Main FastAPI and WebSocket service
│   │   ├── chat_server/            # Chat service integration
│   │   ├── database/               # Database, statistics and game record storage
│   │   ├── event/                  # Tournament functionality
│   │   ├── friend/                 # Friends system
│   │   ├── game_calculation/       # Winning hands, tenpai and rule calculations
│   │   ├── gamestate/              # Game state machines for each ruleset
│   │   ├── match/                  # Matchmaking
│   │   ├── public/                 # Shared services, rate limiting and related modules
│   │   ├── room/                   # Room management
│   │   └── webapi/                 # Web calculation APIs
│   ├── scripts/                    # Maintenance and migration scripts
│   ├── load_test/                  # Load tests
│   ├── pyproject.toml              # Python project configuration
│   ├── requirements.txt            # Python dependencies
│   ├── uv.lock                     # uv dependency lockfile
│   └── start_server.bat            # Windows startup script
│
├── open_mahjong_web/               # Node.js/Vue 3 Web platform
│   ├── client/                     # Vue 3 frontend
│   │   ├── public/                 # Static assets and Unity WebGL files
│   │   ├── src/
│   │   │   ├── api/                # HTTP clients
│   │   │   ├── components/         # Shared components
│   │   │   ├── composables/        # Vue composition logic
│   │   │   ├── game2d/             # 2D game frontend
│   │   │   ├── layouts/            # Page layouts
│   │   │   ├── router/             # Routing
│   │   │   ├── stores/             # Pinia state management
│   │   │   ├── styles/             # Global styles
│   │   │   ├── utils/              # Frontend utilities
│   │   │   └── views/              # Pages
│   │   ├── package.json
│   │   └── vite.config.js
│   ├── server/                     # Express/Socket.IO backend
│   │   ├── index.js                # Node.js service entry point
│   │   ├── botapi/                 # Bot APIs
│   │   ├── config/                 # Database and environment configuration
│   │   ├── guessfan/               # Fan-guessing competition service
│   │   ├── middleware/             # Authentication, rate limiting and middleware
│   │   ├── routes/                 # Web API routes
│   │   ├── services/               # Service layer
│   │   └── utils/                  # Server utilities
│   ├── docs/                       # Web deployment and feature documentation
│   ├── scripts/                    # Build and deployment scripts
│   ├── package.json
│   └── start.bat
│
├── open_mahjong_chatServer/        # Go chat server
│   ├── Main.go                     # Program entry point
│   ├── ConnectPool.go              # Connection pool
│   ├── RoomManager.go              # Chat room management
│   ├── go.mod
│   └── go.sum
│
├── other/                          # Artwork source files, experimental code, rules and supporting assets
├── LICENSE
└── README.md
```

## 3. Technology Stack

### Game client (open_mahjong_unity)

- **Engine**: Unity 6.4 (6000.4.7f1)
- **Language**: C#
- **Networking**: WebSocket (Nativewebsocket)
- **Platforms**: PC/Web/Android/iOS

### Game servers (open_mahjong_server, open_mahjong_chat_server)

- **Framework**: FastAPI
- **Languages**: Python 3.12, Golang
- **Networking**: WebSocket, HTTP
- **Database**: PostgreSQL 18
- **Deployment**: supervisor or any process manager you prefer

### Web platform (open_mahjong_web)

- **Frontend**: Vue3
- **Backend**: Node.js
- **Database**: PostgreSQL 18

### 4. Community and Contact

- **Salasasa platform testing group on QQ**: 906497522
- **open_mahjong_unity development group on QQ**: 1084537740
- **Telegram group**: t.me/open_mahjong_unity
- **Discord**: [https://discord.gg/RqvxDDgdFH](https://discord.gg/RqvxDDgdFH)
- **Project lead on WeChat**: q1448826180
- **Repository**: [https://github.com/xelnagamiao/open_mahjong_unity](https://github.com/xelnagamiao/open_mahjong_unity)
- **Yuque documentation (unfinished)**: [https://www.yuque.com/xelnaga-yjcgq/zkwfgr/lusmvid200iez36q?singleDoc#](https://www.yuque.com/xelnaga-yjcgq/zkwfgr/lusmvid200iez36q?singleDoc#)
- **Development progress**: [https://docs.qq.com/sheet/DZkh2a2VBQkpucXNr?tab=BB08J2](https://docs.qq.com/sheet/DZkh2a2VBQkpucXNr?tab=BB08J2)
- **Sponsorship**: q1448826180
- **Other inquiries**: For job opportunities in southern Jiangsu, Shanghai or Hangzhou, custom development or collaboration, please contact us on QQ or WeChat.

### 5. Acknowledgments

Development team: Xelnaga (lead programmer)  雪枫XueFun9 (tile artwork)  Zoe (random seed design)  Natsuki (author of 新编MCR)  坏狗影子 (emotes)  GitHub/baisebaoma (high-performance bot design)  troooobot (Changsha mahjong implementation)  Zoe (Taiwanese mahjong implementation)  二階堂 ヒロ (center display design)  莫莫柴 (Qingque, 2D table)  Shq, Zoe, ShiauLo (bug fixes)  salasasa数据bot (PCX1078)  大海 (k-mahjong scoring)  Plugin development (新手求教)

Custom ruleset design: 莫莫柴 (青雀)  关兆豪先生 (中庸麻将)  Null (虹雀)  K神传说 (K神麻将)  恋绘色 (香港清章恋绘色魔改)  小林 (国标小林改)  蓝十 (国标蓝十改)  自恧 (浪涌麻将)  南瓜饼 (南雀)

Research into regional rules: Shanghai Qing Hun Peng (何苏)  Classical mahjong (莫莫柴, 何苏, 古明地梦)  Yixing mahjong (苦鱼)  Mil promotion rules (MIL国际麻将联盟)

Livestreaming and promotion: Cloud980Ti  轻轻的飘  等候1234  立直喵  Ce/Est1al  鬼头刀鱼fish  神源之心  方片杰克  Ni9e_9  大柠劳斯  大口啃排骨

Sponsors: 九曜, kiki, 健哥, 何苏, Null, 莫莫柴, 恋绘色, Zazaka, 中山大学国标麻将同好会, 东西喵, 山东大学老年人活动中心, Sw123

Special thanks: 莫莫柴, 码龙, Null, 坏狗影子, 何苏, FFMJ法国麻将协会 (translation support)

Supporting communities: 棋牌游戏研究院, 立直麻雀研习社, 柴の麻将群, 雀渣玩家群, 中山大学国标麻将同好会

Early testing: 夜色祢  chlorine  陪练的命运

And everyone who plays on the salasasa platform!

*Last updated: November 3, 2026, dev ver 0.5.87.0*
