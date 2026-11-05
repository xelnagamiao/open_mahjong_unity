# Entwicklungsdokumentation für open_mahjong_unity

[简体中文](../../README.md) | [日本語](README.ja.md) | [English](README.en.md) | [Français](README.fr.md) | [Deutsch](README.de.md) | [Русский](README.ru.md)

## Überblick

Willkommen bei open_mahjong_unity!

Testseite der Webversion: [https://salasasa.cn](https://salasasa.cn)

Steam-Seite der PC-Version: [https://store.steampowered.com/app/4565740/Salasasa/](https://store.steampowered.com/app/4565740/Salasasa/)

QQ-Testgruppe der Mahjong-Plattform Salasasa: 906497522

## Über das Projekt

open_mahjong_unity ist eine Mahjong-Plattform auf Basis von Unity und Python/FastAPI. Das Projekt ist kostenlos, quelloffen und unter der MIT-Lizenz verfügbar. Es unterstützt gemeinsames Spielen auf PC, Android und im Web. Ziel ist es, alle Mahjong-Regelwerke der Welt zu unterstützen und Spielern Möglichkeiten zur individuellen Anpassung der Regeln zu bieten. In unserer QQ-Gruppe sind Diskussionen, Mithilfe und die Teilnahme an Tests willkommen.

## 1. Lizenz

Dieses Projekt verwendet die MIT-Lizenz (siehe Datei LICENSE).

Der Geltungsbereich dieser MIT-Lizenz ist wie folgt festgelegt:

Mit Ausnahme der ausdrücklich ausgenommenen Bestandteile dürfen sämtliche Quelltexte und Dateien dieses Projekts gemäß den Bedingungen der MIT-Lizenz frei verwendet, kopiert, geändert, zusammengeführt, veröffentlicht, verbreitet, unterlizenziert und/oder verkauft werden, sofern der ursprüngliche Urheberrechtsvermerk und der Lizenzhinweis erhalten bleiben. Die Ausnahmen werden nachfolgend erläutert.

### 1.1 Ressourcen, an denen Projektmitwirkende bestimmte Rechte vorbehalten oder keine vollständige Nutzungserlaubnis erteilen

Für Ressourcen, deren Nutzung ausschließlich für dieses Projekt gestattet ist, sowie für übernommene Ressourcen mit eigenen Erklärungen oder Open-Source-Lizenzen gilt die MIT-Lizenz nicht. Dies umfasst das Verzeichnis `open_mahjong_unity/Assets/Resources` sowie alle darin enthaltenen Unterverzeichnisse und Dateien. Die Inhalte dieses Verzeichnisses, einschließlich, aber nicht beschränkt auf Bilder, Audiodateien, Modelle und Konfigurationsdateien, sind ausschließlich zur Verwendung innerhalb dieses Projekts bestimmt. Ohne vorherige schriftliche Zustimmung des Rechteinhabers dürfen sie weder extrahiert, kopiert, geändert, verbreitet, unterlizenziert noch in anderen Projekten oder für sonstige kommerzielle oder nichtkommerzielle Zwecke verwendet werden. Die Rechteinhaber behalten sich alle nicht ausdrücklich eingeräumten Rechte vor.
Enthält ein Unterverzeichnis von `open_mahjong_unity/Assets/Resources` eine eigene Lizenzdatei, etwa LICENSE oder LICENSE.txt, gilt diese Lizenz für den Inhalt des Unterverzeichnisses und hat Vorrang vor den oben genannten Einschränkungen.

Wenn Sie dieses Projekt ausschließlich zu nichtkommerziellen Zwecken auf einem persönlichen Server oder in einer privaten Umgebung betreiben, einschließlich einer geänderten Version oder eines Forks mit derselben Erklärung, gilt die Nutzung der Ressourcen im Ordner Resources im Rahmen dieses Projekts als gestattet. Diese Erklärung gewährt Rechte über die oben genannten Beschränkungen hinaus: Wenn Ihr Betrieb keinerlei kommerziellen Zweck verfolgt, dürfen Sie automatisch sämtliche Ressourcen unter Resources verwenden und ändern. Diese Erlaubnis erstreckt sich jedoch nicht auf eine unabhängige Extraktion oder die gesonderte Nutzung in anderen Projekten. Für einen kommerziellen Betrieb müssen Sie die Ressourcen in Resources ersetzen, für die keine entsprechende Erlaubnis vorliegt. Alternativ können Sie die Projektverantwortlichen oder die Anbieter der Ressourcen kontaktieren und eine schriftliche Genehmigung für einzelne Ressourcen einholen.

### 1.2 Rechtevorbehalt externer Regelautoren

Diese Klausel ist eine besondere Ergänzung und Einschränkung der MIT-Lizenz. Der von Dritten beigesteuerte Code für bestimmte Regelvarianten, Spiellogik und Zahlenkonfigurationen, der sich gewöhnlich im Verzeichnis `/calculation/` oder in Dateien mit einem ausdrücklichen Hinweis auf den Regelautor befindet, unterliegt standardmäßig der MIT-Lizenz. Kommerzieller Betrieb, Bereitstellung auf öffentlichen Servern, Verbreitung und Änderungen sind gestattet. Die ursprünglichen Mitwirkenden behalten jedoch das Urheberrecht an ihrer eigenständig entwickelten Spiellogik und das Recht, die Erlaubnis zur kommerziellen Nutzung einseitig zurückzunehmen.
Kommt es zu einem Streit zwischen Ihrer betriebenen Plattform und einem Regelautor oder erklärt ein Mitwirkender durch eine Mitteilung im Repository, das Entfernen von Code oder eine schriftliche Benachrichtigung das Ende der Erlaubnis, müssen Sie öffentliche Dienste und kommerzielle Veröffentlichungen auf Grundlage dieser Regel innerhalb einer angemessenen Frist nach Erhalt der Mitteilung einstellen. Diese Frist beträgt standardmäßig 30 Tage. Bitte haben Sie Verständnis dafür, dass dieser Faktor außerhalb der Kontrolle des Projekts liegt.

### 1.3 Verhältnis zwischen dem Namen Salasasa und diesem Projekt

Der Name „Salasasa“ und die Domain salasasa.cn bezeichnen den Beispielserver des Projekts. Wenn Sie einen eigenständigen Fork oder eine geänderte Version von open_mahjong_unity erstellen, beachten Sie bitte Folgendes:

1. Verwenden Sie den ursprünglichen Namen „Salasasa“ nicht für Ihren eigenen Server. Ausgenommen sind persönliches Lernen oder die Pflege eines bestimmten Regelwerks ohne öffentliche kommerzielle Veröffentlichung sowie Fälle, in denen eine vorübergehende Genehmigung erteilt wurde;
2. Ohne Genehmigung dürfen Clients unter einem anderen Namen als „Salasasa“ die API oder Serverdienste von salasasa.cn nicht weiterverwenden;
3. Clientvarianten unter dem Namen „Salasasa“ sind zulässig, sofern eine Genehmigung vorliegt und die Versionsprüfungs-API des Clients die Version ausdrücklich als inoffizielle Änderung kennzeichnet;
4. Führt eine Clientvariante Netzwerkangriffe aus, verhält sie sich böswillig oder beeinträchtigt sie den normalen Serverbetrieb erheblich, behält sich salasasa das Recht vor, die Einstellung der Verbreitung zu verlangen, betroffene Clients zu sperren und rechtliche Schritte zum Schutz seiner Rechte einzuleiten.

## 2. Projektstruktur

Das Repository besteht aus einem Unity-Client, einem Python-Spielserver, einer Webplattform mit Node.js/Vue und einem Go-Chatserver. Der Ordner other enthält die Standardkonfiguration für Nginx, Rangtabellen, Originaldateien der Grafikressourcen und weitere möglicherweise hilfreiche Materialien.

```text
open_mahjong_unity/
├── open_mahjong_unity/             # Unity-Spielclient
│   ├── Assets/
│   │   ├── Editor/                 # Erweiterungen für den Unity-Editor
│   │   ├── Plugins/                # Plugins von Drittanbietern
│   │   ├── Prefabs/                # UI-, Raum- und Tisch-Prefabs
│   │   ├── Resources/              # Modelle, Bilder, Schriften und Audio zur Laufzeit
│   │   ├── Scenes/                 # Unity-Szenen
│   │   ├── Scripts/
│   │   │   ├── ChatServer/         # Chatfunktionen
│   │   │   ├── Config/             # Clientkonfiguration und Benutzerdaten
│   │   │   ├── GameScene/          # Partien, Tische, Wertung und Darstellung der Regeln
│   │   │   ├── GameSceneConfig/    # Einstellungen zum Aussehen der Tische
│   │   │   ├── Network/            # HTTP-/WebSocket-Kommunikation
│   │   │   ├── Rendering/          # Rendering-Funktionen
│   │   │   ├── Room/               # Erstellen und Verwalten von Räumen
│   │   │   └── UI/                 # Hauptoberfläche und gemeinsame UI-Elemente
│   │   └── Shaders/                # Shader
│   ├── Packages/                   # Unity-Paketkonfiguration
│   └── ProjectSettings/            # Unity-Projekteinstellungen
│
├── open_mahjong_server/            # Python-/FastAPI-Spielserver
│   ├── main.py                     # Einstiegspunkt des Dienstes
│   ├── server/
│   │   ├── server.py               # Hauptdienst für FastAPI und WebSocket
│   │   ├── chat_server/            # Anbindung des Chatdienstes
│   │   ├── database/               # Datenbank, Statistiken und Speicherung von Spielaufzeichnungen
│   │   ├── event/                  # Turnierfunktionen
│   │   ├── friend/                 # Freundesystem
│   │   ├── game_calculation/       # Berechnung von Gewinnhänden, Tenpai und Regeln
│   │   ├── gamestate/              # Zustandsautomaten für die einzelnen Mahjong-Regeln
│   │   ├── match/                  # Spielervermittlung
│   │   ├── public/                 # Gemeinsame Dienste, Ratenbegrenzung und weitere Module
│   │   ├── room/                   # Raumverwaltung
│   │   └── webapi/                 # Web-APIs für Berechnungen
│   ├── scripts/                    # Wartungs- und Migrationsskripte
│   ├── load_test/                  # Lasttests
│   ├── pyproject.toml              # Python-Projektkonfiguration
│   ├── requirements.txt            # Python-Abhängigkeiten
│   ├── uv.lock                     # uv-Sperrdatei für Abhängigkeiten
│   └── start_server.bat            # Windows-Startskript
│
├── open_mahjong_web/               # Node.js-/Vue-3-Webplattform
│   ├── client/                     # Vue-3-Frontend
│   │   ├── public/                 # Statische Ressourcen und Unity-WebGL-Dateien
│   │   ├── src/
│   │   │   ├── api/                # HTTP-Clients
│   │   │   ├── components/         # Gemeinsame Komponenten
│   │   │   ├── composables/        # Vue-Kompositionslogik
│   │   │   ├── game2d/             # Frontend für 2D-Partien
│   │   │   ├── layouts/            # Seitenlayouts
│   │   │   ├── router/             # Routing
│   │   │   ├── stores/             # Zustandsverwaltung mit Pinia
│   │   │   ├── styles/             # Globale Stile
│   │   │   ├── utils/              # Frontend-Hilfsfunktionen
│   │   │   └── views/              # Seiten
│   │   ├── package.json
│   │   └── vite.config.js
│   ├── server/                     # Express-/Socket.IO-Backend
│   │   ├── index.js                # Einstiegspunkt des Node.js-Dienstes
│   │   ├── botapi/                 # Bot-APIs
│   │   ├── config/                 # Datenbank- und Umgebungskonfiguration
│   │   ├── guessfan/               # Dienst für Wettbewerbe zum Erraten der Fan-Zahl
│   │   ├── middleware/             # Authentifizierung, Ratenbegrenzung und weitere Middleware
│   │   ├── routes/                 # Web-API-Routen
│   │   ├── services/               # Dienstschicht
│   │   └── utils/                  # Server-Hilfsfunktionen
│   ├── docs/                       # Dokumentation zu Webbereitstellung und Funktionen
│   ├── scripts/                    # Build- und Bereitstellungsskripte
│   ├── package.json
│   └── start.bat
│
├── open_mahjong_chatServer/        # Go-Chatserver
│   ├── Main.go                     # Programmeinstiegspunkt
│   ├── ConnectPool.go              # Verbindungspool
│   ├── RoomManager.go              # Verwaltung der Chaträume
│   ├── go.mod
│   └── go.sum
│
├── other/                          # Grafikquelldateien, experimenteller Code, Regeln und ergänzende Ressourcen
├── LICENSE
└── README.md
```

## 3. Verwendete Technologien

### Spielclient (open_mahjong_unity)

- **Engine**: Unity 6.4 (6000.4.7f1)
- **Sprache**: C#
- **Netzwerk**: WebSocket (Nativewebsocket)
- **Plattformen**: PC/Web/Android/iOS

### Spielserver (open_mahjong_server, open_mahjong_chat_server)

- **Framework**: FastAPI
- **Sprachen**: Python 3.12, Golang
- **Netzwerk**: WebSocket, HTTP
- **Datenbank**: PostgreSQL 18
- **Betrieb**: supervisor oder ein Prozessmanager Ihrer Wahl

### Webplattform (open_mahjong_web)

- **Frontend**: Vue3
- **Backend**: Node.js
- **Datenbank**: PostgreSQL 18

### 4. Community und Kontakt

- **QQ-Testgruppe der Salasasa-Plattform**: 906497522
- **QQ-Entwicklergruppe für open_mahjong_unity**: 1084537740
- **Telegram-Gruppe**: t.me/open_mahjong_unity
- **Discord**: [https://discord.gg/RqvxDDgdFH](https://discord.gg/RqvxDDgdFH)
- **Projektleitung auf WeChat**: q1448826180
- **Projekt-Repository**: [https://github.com/xelnagamiao/open_mahjong_unity](https://github.com/xelnagamiao/open_mahjong_unity)
- **Yuque-Dokumentation (unvollständig)**: [https://www.yuque.com/xelnaga-yjcgq/zkwfgr/lusmvid200iez36q?singleDoc#](https://www.yuque.com/xelnaga-yjcgq/zkwfgr/lusmvid200iez36q?singleDoc#)
- **Entwicklungsfortschritt**: [https://docs.qq.com/sheet/DZkh2a2VBQkpucXNr?tab=BB08J2](https://docs.qq.com/sheet/DZkh2a2VBQkpucXNr?tab=BB08J2)
- **Finanzielle Unterstützung**: q1448826180
- **Weitere Anfragen**: Für Stellenangebote im südlichen Jiangsu, in Shanghai oder Hangzhou sowie für individuelle Softwareentwicklung oder Zusammenarbeit kontaktieren Sie uns bitte über QQ oder WeChat.

### 5. Danksagung

Entwicklungsteam: Xelnaga (Hauptprogrammierer)  雪枫XueFun9 (Spielsteingrafiken)  Zoe (Entwurf der Zufallsstartwerte)  Natsuki (Autor von 新编MCR)  坏狗影子 (Emotes)  GitHub/baisebaoma (Entwurf des leistungsstarken Bots)  troooobot (Implementierung von Changsha-Mahjong)  Zoe (Implementierung von taiwanischem Mahjong)  二階堂 ヒロ (Entwurf der zentralen Anzeige)  莫莫柴 (Qingque, 2D-Tisch)  Shq, Zoe, ShiauLo (Fehlerbehebungen)  salasasa数据bot (PCX1078)  大海 (Wertung für k-Mahjong)  Pluginentwicklung (新手求教)

Entwurf eigener Regelwerke: 莫莫柴 (青雀)  关兆豪先生 (中庸麻将)  Null (虹雀)  K神传说 (K神麻将)  恋绘色 (香港清章恋绘色魔改)  小林 (国标小林改)  蓝十 (国标蓝十改)  自恧 (浪涌麻将)  南瓜饼 (南雀)

Recherche regionaler Regeln: Shanghai Qing Hun Peng (何苏)  Klassisches Mahjong (莫莫柴, 何苏, 古明地梦)  Yixing-Mahjong (苦鱼)  Mil-Förderregeln (MIL国际麻将联盟)

Livestreams und Öffentlichkeitsarbeit: Cloud980Ti  轻轻的飘  等候1234  立直喵  Ce/Est1al  鬼头刀鱼fish  神源之心  方片杰克  Ni9e_9  大柠劳斯  大口啃排骨

Finanzielle Unterstützer: 九曜, kiki, 健哥, 何苏, Null, 莫莫柴, 恋绘色, Zazaka, 中山大学国标麻将同好会, 东西喵, 山东大学老年人活动中心, Sw123

Besonderer Dank: 莫莫柴, 码龙, Null, 坏狗影子, 何苏, FFMJ法国麻将协会 (Unterstützung bei Übersetzungen)

Unterstützende Gemeinschaften: 棋牌游戏研究院, 立直麻雀研习社, 柴の麻将群, 雀渣玩家群, 中山大学国标麻将同好会

Frühe Tests: 夜色祢  chlorine  陪练的命运

Und alle, die auf der salasasa-Plattform spielen!

*Zuletzt aktualisiert: 3. November 2026, dev ver 0.5.87.0*
