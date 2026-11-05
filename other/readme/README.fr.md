# Documentation de développement d’open_mahjong_unity

[简体中文](../../README.md) | [日本語](README.ja.md) | [English](README.en.md) | [Français](README.fr.md) | [Deutsch](README.de.md) | [Русский](README.ru.md)

## Présentation

Bienvenue dans open_mahjong_unity !

Site de test de la version Web : [https://salasasa.cn](https://salasasa.cn)

Page Steam de la version PC : [https://store.steampowered.com/app/4565740/Salasasa/](https://store.steampowered.com/app/4565740/Salasasa/)

Groupe QQ de test de la plateforme de mahjong Salasasa : 906497522

## À propos du projet

open_mahjong_unity est une plateforme de mahjong reposant sur Unity et Python/FastAPI. Ce projet gratuit et open source, sous licence MIT, permet de jouer ensemble sur PC, Android et Web. Il vise à prendre en charge toutes les règles de mahjong du monde et à proposer aux joueurs des options de personnalisation des règles. Rejoignez notre groupe QQ pour discuter, contribuer au projet et participer aux tests.

## 1. Licence

Ce projet utilise la licence MIT (voir le fichier LICENSE).

Le champ d’application de cette licence MIT est le suivant :

À l’exception des éléments expressément exclus, l’ensemble du code source et des fichiers de ce projet peut être librement utilisé, copié, modifié, fusionné, publié, distribué, sous-licencié et/ou vendu conformément à la licence MIT, à condition de conserver la mention de droit d’auteur et la mention de licence d’origine. Les exclusions sont détaillées ci-dessous.

### 1.1 Ressources pour lesquelles les contributeurs conservent certains droits ou n’accordent pas une autorisation complète

Les ressources dont l’utilisation est autorisée uniquement dans le cadre de ce projet, ainsi que les ressources empruntées soumises à d’autres déclarations ou licences open source, notamment le répertoire `open_mahjong_unity/Assets/Resources` et tous ses sous-répertoires et fichiers, ne sont pas couverts par la licence MIT. Le contenu de ce répertoire, y compris, sans s’y limiter, les images, sons, modèles et fichiers de configuration, est réservé à un usage interne à ce projet. Sans l’autorisation écrite préalable du titulaire des droits, il ne peut être extrait, copié, modifié, distribué, sous-licencié, utilisé dans d’autres projets ou exploité à des fins commerciales ou non commerciales. Les titulaires des droits se réservent tous les droits qui ne sont pas expressément accordés.
Si un sous-répertoire de `open_mahjong_unity/Assets/Resources` contient son propre fichier de licence, par exemple LICENSE ou LICENSE.txt, cette licence s’applique au contenu du sous-répertoire et prévaut sur les restrictions ci-dessus.

En outre, si vous déployez ce projet sur un serveur personnel ou dans un environnement privé à des fins exclusivement non commerciales, y compris une version modifiée ou un fork reprenant la même déclaration, vous êtes réputé disposer de l’autorisation d’utiliser les ressources du dossier Resources dans le cadre de ce projet. Cette déclaration étend les autorisations au-delà des restrictions ci-dessus : si votre déploiement n’a aucune finalité commerciale, vous disposez automatiquement du droit d’utiliser et de modifier toute ressource située dans Resources. Cette autorisation ne permet toutefois pas d’extraire ces ressources de manière indépendante ni de les utiliser séparément dans d’autres projets. Pour déployer le projet à des fins commerciales, vous devez remplacer les ressources de Resources pour lesquelles vous n’avez pas obtenu d’autorisation, ou contacter les responsables du projet ou les fournisseurs des ressources afin d’obtenir une autorisation écrite pour chaque ressource concernée.

### 1.2 Réserve de droits des contributeurs tiers aux règles

Cette clause constitue un complément et une restriction particuliers à la licence MIT. Le code fourni par des contributeurs tiers pour certaines variantes de règles, logiques de jeu et configurations numériques, généralement situé dans le répertoire `/calculation/` ou dans des fichiers identifiant le contributeur aux règles, est soumis par défaut à la licence MIT. L’exploitation commerciale, le déploiement sur des serveurs publics, la distribution et la modification sont autorisés. Toutefois, les contributeurs d’origine conservent les droits d’auteur sur leurs logiques de jeu originales et se réservent le droit de révoquer unilatéralement l’autorisation de les utiliser à des fins commerciales.
Si un différend survient entre votre plateforme déployée et le créateur d’une règle, ou si un contributeur annonce la fin de son autorisation par une annonce dans le dépôt, une suppression de code ou une notification écrite, vous devez arrêter les services publics et les distributions commerciales reposant sur cette règle dans un délai raisonnable après réception de la notification, fixé par défaut à 30 jours. Merci de comprendre qu’il s’agit d’un facteur indépendant de la volonté du projet.

### 1.3 Relation entre le nom Salasasa et ce projet

Le nom « Salasasa » et le domaine salasasa.cn désignent le serveur d’exemple du projet. Si vous créez un fork indépendant ou une version modifiée d’open_mahjong_unity, veuillez respecter les conditions suivantes :

1. N’utilisez pas le nom d’origine « Salasasa » pour votre propre serveur, sauf dans le cadre d’un apprentissage personnel ou de la maintenance d’une règle particulière sans diffusion publique commerciale, ou si une autorisation temporaire a été accordée ;
2. Sans autorisation, les clients portant un autre nom que « Salasasa » ne peuvent pas continuer à utiliser l’API ou les services du serveur salasasa.cn ;
3. Les variantes du client portant le nom « Salasasa » sont autorisées, à condition d’obtenir une autorisation et d’indiquer explicitement dans l’API de vérification de version du client qu’il s’agit d’une version modifiée non officielle ;
4. Si une variante du client effectue des attaques réseau, adopte un comportement malveillant ou perturbe gravement le fonctionnement normal du serveur, salasasa se réserve le droit d’exiger l’arrêt de sa diffusion, de bloquer les clients concernés et d’engager des poursuites pour protéger ses droits.

## 2. Structure du projet

Le dépôt comprend un client Unity, un serveur de jeu Python, une plateforme Web Node.js/Vue et un serveur de discussion Go. Le dossier other contient la configuration Nginx par défaut, les tableaux de classement, les fichiers originaux des ressources graphiques et d’autres éléments susceptibles de vous être utiles.

```text
open_mahjong_unity/
├── open_mahjong_unity/             # Client de jeu Unity
│   ├── Assets/
│   │   ├── Editor/                 # Extensions de l’éditeur Unity
│   │   ├── Plugins/                # Plugins tiers
│   │   ├── Prefabs/                # Préfabriqués de l’interface, des salons et des tables
│   │   ├── Resources/              # Modèles, images, polices et sons utilisés à l’exécution
│   │   ├── Scenes/                 # Scènes Unity
│   │   ├── Scripts/
│   │   │   ├── ChatServer/         # Fonctions de discussion
│   │   │   ├── Config/             # Configuration du client et données utilisateur
│   │   │   ├── GameScene/          # Parties, tables, scores et présentation des règles
│   │   │   ├── GameSceneConfig/    # Apparence des tables
│   │   │   ├── Network/            # Communications HTTP/WebSocket
│   │   │   ├── Rendering/          # Fonctions de rendu
│   │   │   ├── Room/               # Création et gestion des salons
│   │   │   └── UI/                 # Interface principale et éléments communs
│   │   └── Shaders/                # Shaders
│   ├── Packages/                   # Configuration des packages Unity
│   └── ProjectSettings/            # Paramètres du projet Unity
│
├── open_mahjong_server/            # Serveur de jeu Python/FastAPI
│   ├── main.py                     # Point d’entrée du service
│   ├── server/
│   │   ├── server.py               # Service principal FastAPI et WebSocket
│   │   ├── chat_server/            # Intégration du service de discussion
│   │   ├── database/               # Base de données, statistiques et enregistrements des parties
│   │   ├── event/                  # Fonctions de tournoi
│   │   ├── friend/                 # Système d’amis
│   │   ├── game_calculation/       # Calcul des mains gagnantes, du tenpai et des règles
│   │   ├── gamestate/              # Machines à états pour chaque règle de mahjong
│   │   ├── match/                  # Recherche de parties
│   │   ├── public/                 # Services communs, limitation de débit et autres modules
│   │   ├── room/                   # Gestion des salons
│   │   └── webapi/                 # API Web de calcul
│   ├── scripts/                    # Scripts de maintenance et de migration
│   ├── load_test/                  # Tests de charge
│   ├── pyproject.toml              # Configuration du projet Python
│   ├── requirements.txt            # Dépendances Python
│   ├── uv.lock                     # Fichier de verrouillage des dépendances uv
│   └── start_server.bat            # Script de démarrage Windows
│
├── open_mahjong_web/               # Plateforme Web Node.js/Vue 3
│   ├── client/                     # Frontend Vue 3
│   │   ├── public/                 # Ressources statiques et fichiers Unity WebGL
│   │   ├── src/
│   │   │   ├── api/                # Clients HTTP
│   │   │   ├── components/         # Composants communs
│   │   │   ├── composables/        # Logique de composition Vue
│   │   │   ├── game2d/             # Frontend des parties en 2D
│   │   │   ├── layouts/            # Mises en page
│   │   │   ├── router/             # Routage
│   │   │   ├── stores/             # Gestion d’état Pinia
│   │   │   ├── styles/             # Styles globaux
│   │   │   ├── utils/              # Utilitaires frontend
│   │   │   └── views/              # Pages
│   │   ├── package.json
│   │   └── vite.config.js
│   ├── server/                     # Backend Express/Socket.IO
│   │   ├── index.js                # Point d’entrée du service Node.js
│   │   ├── botapi/                 # API des bots
│   │   ├── config/                 # Configuration de la base de données et de l’environnement
│   │   ├── guessfan/               # Service de compétition de devinettes sur les fans
│   │   ├── middleware/             # Authentification, limitation de débit et autres middlewares
│   │   ├── routes/                 # Routes des API Web
│   │   ├── services/               # Couche de services
│   │   └── utils/                  # Utilitaires serveur
│   ├── docs/                       # Documentation du déploiement Web et des fonctionnalités
│   ├── scripts/                    # Scripts de compilation et de déploiement
│   ├── package.json
│   └── start.bat
│
├── open_mahjong_chatServer/        # Serveur de discussion Go
│   ├── Main.go                     # Point d’entrée du programme
│   ├── ConnectPool.go              # Pool de connexions
│   ├── RoomManager.go              # Gestion des salons de discussion
│   ├── go.mod
│   └── go.sum
│
├── other/                          # Sources graphiques, code expérimental, règles et ressources complémentaires
├── LICENSE
└── README.md
```

## 3. Technologies

### Client de jeu (open_mahjong_unity)

- **Moteur** : Unity 6.4 (6000.4.7f1)
- **Langage** : C#
- **Réseau** : WebSocket (Nativewebsocket)
- **Plateformes** : PC/Web/Android/iOS

### Serveurs de jeu (open_mahjong_server, open_mahjong_chat_server)

- **Framework** : FastAPI
- **Langages** : Python 3.12, Golang
- **Réseau** : WebSocket, HTTP
- **Base de données** : PostgreSQL 18
- **Déploiement** : supervisor ou tout gestionnaire de processus de votre choix

### Plateforme Web (open_mahjong_web)

- **Frontend** : Vue3
- **Backend** : Node.js
- **Base de données** : PostgreSQL 18

### 4. Communauté et contacts

- **Groupe QQ de test de la plateforme Salasasa** : 906497522
- **Groupe QQ de développement d’open_mahjong_unity** : 1084537740
- **Groupe Telegram** : t.me/open_mahjong_unity
- **Discord** : [https://discord.gg/RqvxDDgdFH](https://discord.gg/RqvxDDgdFH)
- **Responsable du projet sur WeChat** : q1448826180
- **Dépôt du projet** : [https://github.com/xelnagamiao/open_mahjong_unity](https://github.com/xelnagamiao/open_mahjong_unity)
- **Documentation Yuque (inachevée)** : [https://www.yuque.com/xelnaga-yjcgq/zkwfgr/lusmvid200iez36q?singleDoc#](https://www.yuque.com/xelnaga-yjcgq/zkwfgr/lusmvid200iez36q?singleDoc#)
- **Suivi du développement** : [https://docs.qq.com/sheet/DZkh2a2VBQkpucXNr?tab=BB08J2](https://docs.qq.com/sheet/DZkh2a2VBQkpucXNr?tab=BB08J2)
- **Soutien financier** : q1448826180
- **Autres demandes** : Pour des offres d’emploi dans le sud du Jiangsu, à Shanghai ou à Hangzhou, du développement sur mesure ou des collaborations, contactez-nous sur QQ ou WeChat.

### 5. Remerciements

Équipe de développement : Xelnaga (programmeur principal)  雪枫XueFun9 (illustrations des tuiles)  Zoe (conception des graines aléatoires)  Natsuki (auteur de 新编MCR)  坏狗影子 (émoticônes)  GitHub/baisebaoma (conception du bot haute performance)  troooobot (implémentation du mahjong de Changsha)  Zoe (implémentation du mahjong taïwanais)  二階堂 ヒロ (conception de l’affichage central)  莫莫柴 (Qingque, table 2D)  Shq, Zoe, ShiauLo (corrections de bugs)  salasasa数据bot (PCX1078)  大海 (calcul des scores du k-mahjong)  Développement de plugins (新手求教)

Conception des règles personnalisées : 莫莫柴 (青雀)  关兆豪先生 (中庸麻将)  Null (虹雀)  K神传说 (K神麻将)  恋绘色 (香港清章恋绘色魔改)  小林 (国标小林改)  蓝十 (国标蓝十改)  自恧 (浪涌麻将)  南瓜饼 (南雀)

Recherches sur les règles régionales : Shanghai Qing Hun Peng (何苏)  Mahjong classique (莫莫柴, 何苏, 古明地梦)  Mahjong de Yixing (苦鱼)  Règles de promotion Mil (MIL国际麻将联盟)

Diffusions en direct et promotion : Cloud980Ti  轻轻的飘  等候1234  立直喵  Ce/Est1al  鬼头刀鱼fish  神源之心  方片杰克  Ni9e_9  大柠劳斯  大口啃排骨

Soutiens financiers : 九曜, kiki, 健哥, 何苏, Null, 莫莫柴, 恋绘色, Zazaka, 中山大学国标麻将同好会, 东西喵, 山东大学老年人活动中心, Sw123

Remerciements particuliers : 莫莫柴, 码龙, Null, 坏狗影子, 何苏, FFMJ法国麻将协会 (aide à la traduction)

Communautés partenaires : 棋牌游戏研究院, 立直麻雀研习社, 柴の麻将群, 雀渣玩家群, 中山大学国标麻将同好会

Premiers tests : 夜色祢  chlorine  陪练的命运

Ainsi qu’à toutes les personnes qui jouent sur la plateforme salasasa !

*Dernière mise à jour : 3 novembre 2026, dev ver 0.5.87.0*
