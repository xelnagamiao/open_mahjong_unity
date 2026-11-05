# open_mahjong_unity 開発ドキュメント

[简体中文](../../README.md) | [繁體中文](README.zh-TW.md) | [日本語](README.ja.md) | [English](README.en.md) | [Français](README.fr.md) | [Deutsch](README.de.md) | [Русский](README.ru.md)

## 概要

open_mahjong_unity へようこそ！

Web 版テストサイト：[https://salasasa.cn](https://salasasa.cn)

PC 版 Steam ページ：[https://store.steampowered.com/app/4565740/Salasasa/](https://store.steampowered.com/app/4565740/Salasasa/)

Salasasa 麻雀プラットフォームのテスト用 QQ グループ：906497522

## プロジェクト紹介

open_mahjong_unity は Unity と Python/FastAPI を基盤とする麻雀プラットフォームです。MIT ライセンスを採用した無料のオープンソースプロジェクトで、PC・Android・Web 間のクロスプレイに対応しています。世界中のあらゆる麻雀ルールへの対応と、プレイヤーがルールをカスタマイズできる選択肢の提供を目指しています。QQ グループでの議論、開発への協力、テストへの参加を歓迎します。

## 1. ライセンスについて

本プロジェクトは MIT ライセンスを採用しています（LICENSE ファイルを参照）。

この MIT ライセンスの適用範囲は次のとおりです。

明示的に除外された部分を除き、本プロジェクトのすべてのソースコードおよびファイルは、元の著作権表示とライセンス表示を保持することを条件に、MIT ライセンスの規定に従って自由に使用、複製、変更、結合、公開、配布、サブライセンスおよび販売できます。除外される部分の詳細は以下のとおりです。

### 1.1 プロジェクトの貢献者が一部の権利を留保する、または全面的な許諾を行っていない参照リソース

リソースフォルダー内の、本プロジェクトに限って使用が許可されたアセット、および別の声明やオープンソースライセンスに従う借用素材には、MIT ライセンスは適用されません。これには `open_mahjong_unity/Assets/Resources` ディレクトリと、そのすべてのサブディレクトリおよびファイルが含まれます。このディレクトリの内容（画像、音声、モデル、設定ファイルなどを含みますが、これらに限りません）は、本プロジェクト内での使用に限定されます。著作権者による事前の書面での許可なしに、抽出、複製、変更、配布、サブライセンス、他のプロジェクトでの使用、その他営利・非営利を問わずあらゆる目的で使用することはできません。明示的に許諾されていないすべての権利は、著作権者が留保します。
`open_mahjong_unity/Assets/Resources` 内のサブディレクトリに独立したライセンスファイル（LICENSE、LICENSE.txt など）がある場合、そのサブディレクトリの内容には当該ライセンスが適用され、上記の制限よりも優先されます。

また、個人のサーバーまたは私的な環境で、非営利目的に限って本プロジェクトを運用する場合（同じ声明を採用した改変版やフォークを含む）、本プロジェクトの範囲内で Resources フォルダー内のリソースを使用する許諾を得たものとみなします。この声明は上記のライセンス制限を超えるものであり、運用に商用目的がなければ、Resources 内のあらゆるリソースを使用・変更する権利が自動的に認められます。ただし、リソースを独立して抽出したり、他のプロジェクトで単独で使用したりする権利までは含まれません。本プロジェクトを商用で運用する場合は、Resources 内の許諾を得ていないアセットを差し替える必要があります。または、プロジェクトのメンテナーやリソース提供者に連絡し、個々のリソースについて書面での許可を求めることもできます。

### 1.2 第三者のルール貢献者による権利留保

本条項は MIT ライセンスに対する特別な補足および制限です。本プロジェクトに含まれる、第三者の貢献者が提供した特定のルール変種、ゲームロジック、数値設定のコード（通常は `/calculation/` ディレクトリ、またはルールの貢献者が明記されたファイル内）は、原則として MIT ライセンスに従い、商用運営、公開サーバーへの展開、自由な配布および変更が認められます。ただし、元の貢献者は独自に創作したゲームロジックの著作権を保持し、その商用利用の許諾を一方的に撤回する権利を留保します。
運用するプラットフォームとルールの創作者との間で紛争が生じた場合、または貢献者がリポジトリでの告知、コードの削除、書面による通知を通じて許諾の終了を宣言した場合は、通知を受けた後の合理的な期間内（原則 30 日）に、その特定のルールに基づく公開サービスおよび商用リリースを停止する必要があります。これは本プロジェクトでは制御できない要因であることをご理解ください。

### 1.3 Salasasa という名称と本プロジェクトの関係

「Salasasa」という名称および salasasa.cn というドメインは、本プロジェクトのサンプルサーバーの識別名とドメインです。open_mahjong_unity を基に独立したフォークや改変版を作成する際は、以下の事項を守ってください。

1. 自分のサーバーに元の名称「Salasasa」を使用してはいけません。ただし、個人学習や特定ルールの保守を目的とし、商用として一般公開しない場合、または一時的な許可を得ている場合は除きます。
2. 許可なく、「Salasasa」以外の名称のクライアントで salasasa.cn の API やサーバーサービスを継続して使用してはいけません。
3. 「Salasasa」の名称を使用する派生クライアントは認められますが、許可を得たうえで、クライアントのバージョン検証 API に非公式の改変版であることを明示する必要があります。
4. 派生クライアントがネットワーク攻撃、悪意ある行為、またはサーバーの正常な運用を著しく妨げる行為を行った場合、salasasa は配布の停止要求、該当クライアントの遮断、権利保護のための法的措置を講じる権利を留保します。

## 2. プロジェクト構成

リポジトリは Unity クライアント、Python ゲームサーバー、Node.js/Vue Web プラットフォーム、Go チャットサーバーで構成されています。other フォルダーには、標準の Nginx 設定、段位設定表、美術素材の元データなど、開発や運用に役立つ資料が保存されています。

```text
open_mahjong_unity/
├── open_mahjong_unity/             # Unity ゲームクライアント
│   ├── Assets/
│   │   ├── Editor/                 # Unity エディター拡張
│   │   ├── Plugins/                # サードパーティープラグイン
│   │   ├── Prefabs/                # UI、ルーム、卓のプレハブ
│   │   ├── Resources/              # モデル、画像、フォント、音声などの実行時リソース
│   │   ├── Scenes/                 # Unity シーン
│   │   ├── Scripts/
│   │   │   ├── ChatServer/         # チャット機能
│   │   │   ├── Config/             # クライアント設定とユーザーデータ
│   │   │   ├── GameScene/          # 対局、卓、得点計算、各ルールの表示
│   │   │   ├── GameSceneConfig/    # 卓の外観設定
│   │   │   ├── Network/            # HTTP/WebSocket 通信
│   │   │   ├── Rendering/          # 描画関連機能
│   │   │   ├── Room/               # ルームの作成と管理
│   │   │   └── UI/                 # メイン画面と共通 UI
│   │   └── Shaders/                # シェーダー
│   ├── Packages/                   # Unity パッケージ設定
│   └── ProjectSettings/            # Unity プロジェクト設定
│
├── open_mahjong_server/            # Python/FastAPI ゲームサーバー
│   ├── main.py                     # サービスの起動エントリーポイント
│   ├── server/
│   │   ├── server.py               # FastAPI と WebSocket のメインサービス
│   │   ├── chat_server/            # チャットサービスとの連携
│   │   ├── database/               # データベース、統計、牌譜の保存
│   │   ├── event/                  # 大会機能
│   │   ├── friend/                 # フレンド機能
│   │   ├── game_calculation/       # 和了、聴牌、ルールの計算
│   │   ├── gamestate/              # 各麻雀ルールの対局状態機械
│   │   ├── match/                  # マッチング
│   │   ├── public/                 # 共通サービス、レート制限など
│   │   ├── room/                   # ルーム管理
│   │   └── webapi/                 # Web 計算 API
│   ├── scripts/                    # 保守・移行スクリプト
│   ├── load_test/                  # 負荷テスト
│   ├── pyproject.toml              # Python プロジェクト設定
│   ├── requirements.txt            # Python 依存関係
│   ├── uv.lock                     # uv 依存関係のロックファイル
│   └── start_server.bat            # Windows 起動スクリプト
│
├── open_mahjong_web/               # Node.js/Vue 3 Web プラットフォーム
│   ├── client/                     # Vue 3 フロントエンド
│   │   ├── public/                 # 静的リソースと Unity WebGL ファイル
│   │   ├── src/
│   │   │   ├── api/                # HTTP クライアント
│   │   │   ├── components/         # 共通コンポーネント
│   │   │   ├── composables/        # Vue の Composition ロジック
│   │   │   ├── game2d/             # 2D 対局フロントエンド
│   │   │   ├── layouts/            # ページレイアウト
│   │   │   ├── router/             # ルーティング
│   │   │   ├── stores/             # Pinia 状態管理
│   │   │   ├── styles/             # 共通スタイル
│   │   │   ├── utils/              # フロントエンド用ユーティリティー
│   │   │   └── views/              # ページ
│   │   ├── package.json
│   │   └── vite.config.js
│   ├── server/                     # Express/Socket.IO バックエンド
│   │   ├── index.js                # Node.js サービスのエントリーポイント
│   │   ├── botapi/                 # ボット API
│   │   ├── config/                 # データベース・環境設定
│   │   ├── guessfan/               # 翻数当て対戦サービス
│   │   ├── middleware/             # 認証、レート制限などのミドルウェア
│   │   ├── routes/                 # Web API ルーティング
│   │   ├── services/               # サービス層
│   │   └── utils/                  # サーバー用ユーティリティー
│   ├── docs/                       # Web の展開・機能ドキュメント
│   ├── scripts/                    # ビルド・展開スクリプト
│   ├── package.json
│   └── start.bat
│
├── open_mahjong_chatServer/        # Go チャットサーバー
│   ├── Main.go                     # プログラムのエントリーポイント
│   ├── ConnectPool.go              # 接続プール
│   ├── RoomManager.go              # チャットルーム管理
│   ├── go.mod
│   └── go.sum
│
├── other/                          # 美術元データ、実験コード、ルール資料、補助素材
├── LICENSE
└── README.md
```

## 3. 技術スタック

### ゲームクライアント (open_mahjong_unity)

- **エンジン**：Unity 6.4 (6000.4.7f1)
- **言語**：C#
- **ネットワーク**：WebSocket (Nativewebsocket)
- **プラットフォーム**：PC/Web/Android/iOS

### ゲームサーバー (open_mahjong_server、open_mahjong_chat_server)

- **フレームワーク**：FastAPI
- **言語**：Python 3.12、Golang
- **ネットワーク**：WebSocket、HTTP
- **データベース**：PostgreSQL 18
- **運用**：supervisor、またはお好みのプロセスマネージャー

### Web プラットフォーム (open_mahjong_web)

- **フロントエンド**：Vue3
- **バックエンド**：Node.js
- **データベース**：PostgreSQL 18

### 4. コミュニティー・連絡先

- **Salasasa プラットフォームのテスト用 QQ グループ**：906497522
- **open_mahjong_unity 開発交流用 QQ グループ**：1084537740
- **Telegram グループ**：t.me/open_mahjong_unity
- **Discord**：[https://discord.gg/RqvxDDgdFH](https://discord.gg/RqvxDDgdFH)
- **プロジェクト責任者の WeChat**：q1448826180
- **リポジトリ**：[https://github.com/xelnagamiao/open_mahjong_unity](https://github.com/xelnagamiao/open_mahjong_unity)
- **語雀ドキュメント（未完成）**：[https://www.yuque.com/xelnaga-yjcgq/zkwfgr/lusmvid200iez36q?singleDoc#](https://www.yuque.com/xelnaga-yjcgq/zkwfgr/lusmvid200iez36q?singleDoc#)
- **開発進捗表**：[https://docs.qq.com/sheet/DZkh2a2VBQkpucXNr?tab=BB08J2](https://docs.qq.com/sheet/DZkh2a2VBQkpucXNr?tab=BB08J2)
- **支援**：q1448826180
- **その他**：江蘇省南部・上海・杭州での仕事、受託開発、協業については QQ または WeChat でご連絡ください。

### 5. 謝辞

開発チーム：Xelnaga（メインプログラマー）  雪枫XueFun9（牌デザイン提供）  Zoe（乱数シード設計）  Natsuki（新编MCR 著者）  坏狗影子（スタンプ提供）  GitHub/baisebaoma（高性能ボット設計）  troooobot（長沙麻雀の実装）  Zoe（台湾麻雀の実装）  二階堂 ヒロ（中央表示盤の設計）  莫莫柴（青雀、2D 卓）  Shq、Zoe、ShiauLo（バグ修正）  salasasa数据bot（PCX1078）  大海（k 麻雀の得点計算）  プラグイン開発（新手求教）

カスタムルール設計：莫莫柴（青雀）  关兆豪先生（中庸麻将）  Null（虹雀）  K神传说（K神麻将）  恋绘色（香港清章恋绘色魔改）  小林（国标小林改）  蓝十（国标蓝十改）  自恧（浪涌麻将）  南瓜饼（南雀）

地方ルールの調査：上海清混碰（何苏）  古典麻雀（莫莫柴、何苏、古明地梦）  宜興麻雀（苦鱼）  Mil 普及ルール（MIL国际麻将联盟）

配信・宣伝：Cloud980Ti  轻轻的飘  等候1234  立直喵  Ce/Est1al  鬼头刀鱼fish  神源之心  方片杰克  Ni9e_9  大柠劳斯  大口啃排骨

支援者：九曜、kiki、健哥、何苏、Null、莫莫柴、恋绘色、Zazaka、中山大学国标麻将同好会、东西喵、山东大学老年人活动中心、Sw123

特別な感謝：莫莫柴、码龙、Null、坏狗影子、何苏、FFMJ法国麻将协会（翻訳支援）

協力コミュニティー：棋牌游戏研究院、立直麻雀研习社、柴の麻将群、雀渣玩家群、中山大学国标麻将同好会

初期テスト：夜色祢  chlorine  陪练的命运

そして、salasasa プラットフォームで遊んでくださるすべての皆さん！

*最終更新：2026年11月3日 dev ver 0.5.87.0*
