# Documentación de desarrollo de open_mahjong_unity

[简体中文](../../README.md) | [繁體中文](README.zh-TW.md) | [日本語](README.ja.md) | [English](README.en.md) | [Français](README.fr.md) | [Deutsch](README.de.md) | [Русский](README.ru.md) | [Español](README.es.md)

## Descripción general

¡Bienvenido a open_mahjong_unity!

Sitio de pruebas Web: [https://salasasa.cn](https://salasasa.cn)

Página de Steam para PC: [https://store.steampowered.com/app/4565740/Salasasa/](https://store.steampowered.com/app/4565740/Salasasa/)

Grupo de pruebas de la plataforma Salasasa en QQ: 906497522

## Introducción al proyecto

open_mahjong_unity es una plataforma de mahjong basada en Unity y Python/FastAPI. Es gratuita, de código abierto bajo la licencia MIT y admite juego multiplataforma entre PC, Android y Web. Su objetivo es admitir todas las reglas de mahjong del mundo y ofrecer a los jugadores opciones para personalizar las reglas. Actualmente ya admite decenas de variantes de mahjong; ¡te invitamos a unirte al grupo de QQ para debatir, colaborar y ayudar con las pruebas!

## 1. Licencia

Este proyecto se distribuye bajo la licencia MIT (consulta el archivo LICENSE para más detalles).

El alcance de esta licencia MIT es el siguiente:

Salvo las partes expresamente excluidas, todo el código fuente y los archivos de este proyecto pueden usarse, copiarse, modificarse, combinarse, publicarse, distribuirse, sublicenciarse y/o venderse libremente conforme a los términos de la licencia MIT, siempre que se conserven el aviso de derechos de autor original y este aviso de licencia. A continuación se detallan las exclusiones explícitas:

### 1.1 Recursos sobre los que los colaboradores reservan ciertos derechos o no otorgan licencia completa

Los recursos de la carpeta de recursos autorizados únicamente para este proyecto, así como los materiales de terceros sujetos a otros avisos o licencias de código abierto —incluido el directorio `open_mahjong_unity/Assets/Resources` y todos sus subdirectorios y archivos—, no están cubiertos por la licencia MIT. El contenido de este directorio (incluidos, entre otros, imágenes, audio, modelos y archivos de configuración) es únicamente para uso interno dentro de este proyecto. Sin permiso previo por escrito del titular de los derechos de autor, no puede extraerse, copiarse, modificarse, distribuirse, sublicenciarse, utilizarse en otros proyectos ni emplearse con fines comerciales o no comerciales. Los titulares de los derechos reservan todos los derechos no concedidos expresamente.
Si algún subdirectorio dentro de `open_mahjong_unity/Assets/Resources` contiene su propio archivo de licencia (como `LICENSE`, `LICENSE.txt`, etc.), dicha licencia independiente regirá el contenido de ese subdirectorio y prevalecerá sobre las restricciones anteriores.

Además, si despliegas este proyecto en un servidor personal o entorno privado únicamente con fines no comerciales (incluida una versión modificada o bifurcación que conserve esta misma declaración), se considerará que cuentas con autorización para utilizar los recursos de la carpeta `Resources` dentro del ámbito de este proyecto. Esta declaración amplía las restricciones anteriores: si tu despliegue no tiene fines comerciales, obtienes automáticamente el derecho a usar y modificar cualquier recurso dentro de `Resources`. Sin embargo, esta autorización no se extiende a la extracción independiente de dichos recursos ni a su uso por separado en otros proyectos, y la carpeta `Resources` puede contener materiales comerciales o no comerciales de otros orígenes o licencias. Si deseas desplegar este proyecto con fines comerciales, deberás reemplazar los activos de `Resources` que no cuenten con autorización, o bien ponerte en contacto con los mantenedores del proyecto o los proveedores de los recursos para obtener permiso por escrito para cada recurso concreto.

### 1.2 Reserva de derechos de los colaboradores de reglas de terceros

Esta cláusula constituye un complemento y limitación especial de la licencia MIT. El código correspondiente a variantes específicas de reglas, lógica de juego y configuraciones numéricas aportadas por colaboradores externos (ubicado normalmente en el directorio `/calculation/` o en archivos que identifican al colaborador de la regla) se rige por defecto por la licencia MIT, permitiendo la explotación comercial, el despliegue en servidores públicos, la distribución y la modificación. No obstante, los colaboradores originales conservan los derechos de autor sobre su lógica de juego original y se reservan el derecho de revocar unilateralmente el permiso de uso comercial.
Si surge alguna disputa entre la plataforma que despliegues y el creador de una regla, o si el colaborador declara la terminación de la autorización mediante un anuncio en el repositorio, la retirada del código o una notificación por escrito, deberás cesar los servicios públicos y la distribución comercial basados en esa regla específica dentro de un plazo razonable tras recibir la notificación (30 días por defecto). Agradecemos tu comprensión ante este factor ajeno al control del proyecto.

### 1.3 Declaración sobre la relación entre el nombre Salasasa y este proyecto

El nombre «Salasasa» y el dominio `salasasa.cn` identifican al servidor de ejemplo del proyecto. Al crear una bifurcación independiente o versión modificada basada en `open_mahjong_unity`, respeta las siguientes normas:

1. No utilices el nombre original «Salasasa» para nombrar tu propio servidor, salvo para aprendizaje personal o mantenimiento de una regla específica sin publicación comercial pública, o cuando se haya concedido permiso temporal;
2. Sin permiso, los clientes con un nombre distinto de «Salasasa» no pueden seguir utilizando la API ni los servicios de servidor de `salasasa.cn`;
3. Se permiten variantes de cliente basadas en el nombre «Salasasa», siempre que se obtenga permiso y se indique explícitamente en la API de verificación de versión del cliente que se trata de una modificación no oficial;
4. Si una variante de cliente realiza ataques de red, incurre en comportamientos maliciosos o afecta gravemente al funcionamiento normal del servidor, Salasasa se reserva el derecho de exigir el cese de su distribución, bloquear los clientes afectados y emprender acciones legales para proteger sus derechos.

## 2. Estructura del proyecto

El repositorio consta de un cliente Unity, un servidor de juego en Python, una plataforma Web en Node.js/Vue y un servidor de chat en Go. La carpeta `other` guarda la configuración predeterminada de Nginx, tablas de rangos, archivos fuente de arte y otros materiales útiles.

```text
open_mahjong_unity/
├── open_mahjong_unity/             # Cliente de juego Unity
│   ├── Assets/
│   │   ├── Editor/                 # Extensiones del editor de Unity
│   │   ├── Plugins/                # Plugins de terceros
│   │   ├── Prefabs/                # Prefabs de UI, salas y mesas
│   │   ├── Resources/              # Modelos, imágenes, fuentes y audio en tiempo de ejecución
│   │   ├── Scenes/                 # Escenas de Unity
│   │   ├── Scripts/
│   │   │   ├── ChatServer/         # Funcionalidad de chat
│   │   │   ├── Config/             # Configuración del cliente y datos de usuario
│   │   │   ├── GameScene/          # Partidas, mesas, puntuación y presentación por regla
│   │   │   ├── GameSceneConfig/    # Configuración visual de la mesa
│   │   │   ├── Network/            # Comunicación HTTP/WebSocket
│   │   │   ├── Rendering/          # Funciones de renderizado
│   │   │   ├── Room/               # Creación y gestión de salas
│   │   │   └── UI/                 # Interfaz principal y UI común
│   │   └── Shaders/                # Shaders
│   ├── Packages/                   # Configuración de paquetes de Unity
│   └── ProjectSettings/            # Configuración del proyecto Unity
│
├── open_mahjong_server/            # Servidor de juego Python/FastAPI
│   ├── main.py                     # Punto de entrada del servicio
│   ├── server/
│   │   ├── server.py               # Servicio principal FastAPI y WebSocket
│   │   ├── chat_server/            # Integración con el servicio de chat
│   │   ├── database/               # Base de datos, estadísticas y almacenamiento de registros
│   │   ├── event/                  # Funcionalidad de torneos y eventos
│   │   ├── friend/                 # Sistema de amigos
│   │   ├── game_calculation/       # Cálculo de manos ganadoras, tenpai y reglas
│   │   ├── gamestate/              # Máquinas de estado de partida por cada regla
│   │   ├── match/                  # Sistema de emparejamiento
│   │   ├── public/                 # Servicios compartidos, limitación de tasa y módulos comunes
│   │   ├── room/                   # Gestión de salas
│   │   └── webapi/                 # Interfaces de cálculo para Web
│   ├── scripts/                    # Scripts de mantenimiento y migración
│   ├── load_test/                  # Pruebas de carga
│   ├── pyproject.toml              # Configuración del proyecto Python
│   ├── requirements.txt            # Dependencias de Python
│   ├── uv.lock                     # Archivo de bloqueo de dependencias uv
│   └── start_server.bat            # Script de inicio para Windows
│
├── open_mahjong_web/               # Plataforma Web Node.js/Vue 3
│   ├── client/                     # Frontend Vue 3
│   │   ├── public/                 # Recursos estáticos y archivos Unity WebGL
│   │   ├── src/
│   │   │   ├── api/                # Clientes HTTP
│   │   │   ├── components/         # Componentes compartidos
│   │   │   ├── composables/        # Lógica composable de Vue
│   │   │   ├── game2d/             # Frontend de partida 2D
│   │   │   ├── layouts/            # Plantillas de página
│   │   │   ├── router/             # Enrutamiento
│   │   │   ├── stores/             # Gestión de estado con Pinia
│   │   │   ├── styles/             # Estilos globales
│   │   │   ├── utils/              # Utilidades del frontend
│   │   │   └── views/              # Vistas / páginas
│   │   ├── package.json
│   │   └── vite.config.js
│   ├── server/                     # Backend Express/Socket.IO
│   │   ├── index.js                # Punto de entrada del servicio Node.js
│   │   ├── botapi/                 # Interfaces de bots
│   │   ├── config/                 # Configuración de base de datos y entorno
│   │   ├── guessfan/               # Servicio de duelo de adivinanza de fan
│   │   ├── middleware/             # Middleware de autenticación y limitación de tasa
│   │   ├── routes/                 # Rutas de la API Web
│   │   ├── services/               # Capa de servicios
│   │   └── utils/                  # Utilidades del servidor
│   ├── docs/                       # Documentación de despliegue y funciones Web
│   ├── scripts/                    # Scripts de compilación y despliegue
│   ├── package.json
│   └── start.bat
│
├── open_mahjong_chatServer/        # Servidor de chat en Go
│   ├── Main.go                     # Punto de entrada del programa
│   ├── ConnectPool.go              # Pool de conexiones
│   ├── RoomManager.go              # Gestión de salas de chat
│   ├── go.mod
│   └── go.sum
│
├── other/                          # Archivos fuente de arte, código experimental, reglas y recursos auxiliares
├── LICENSE
└── README.md
```

## 3. Stack tecnológico

### Cliente de juego (open_mahjong_unity)

- **Motor**: Unity 6.4 (6000.3.28f1)
- **Lenguaje**: C#
- **Red**: WebSocket (Nativewebsocket)
- **Plataformas**: PC/Web/Android/iOS

### Servidores de juego (open_mahjong_server, open_mahjong_chat_server)

- **Framework**: FastAPI
- **Lenguajes**: Python 3.14.2, Golang
- **Red**: WebSocket, HTTP
- **Base de datos**: PostgreSQL 18
- **Despliegue**: supervisor o cualquier gestor de procesos de tu preferencia

### Plataforma Web (open_mahjong_web)

- **Frontend**: Vue 3
- **Backend**: Node.js
- **Base de datos**: PostgreSQL 18

### 4. Comunidad y contacto

- **Grupo de pruebas de Salasasa en QQ**: 906497522
- **Grupo de desarrollo de open_mahjong_unity en QQ**: 1084537740
- **Grupo de Telegram**: t.me/open_mahjong_unity
- **Discord**: [https://discord.gg/RqvxDDgdFH](https://discord.gg/RqvxDDgdFH)
- **WeChat del responsable del proyecto**: q1448826180
- **Repositorio**: [https://github.com/xelnagamiao/open_mahjong_unity](https://github.com/xelnagamiao/open_mahjong_unity)
- **Documentación en Yuque (en progreso)**: [https://www.yuque.com/xelnaga-yjcgq/zkwfgr/lusmvid200iez36q?singleDoc#](https://www.yuque.com/xelnaga-yjcgq/zkwfgr/lusmvid200iez36q?singleDoc#)
- **Hoja de progreso de desarrollo**: [https://docs.qq.com/sheet/DZkh2a2VBQkpucXNr?tab=BB08J2](https://docs.qq.com/sheet/DZkh2a2VBQkpucXNr?tab=BB08J2)
- **Patrocinio**: q1448826180
- **Otras consultas**: Para ofertas de empleo en el sur de Jiangsu, Shanghái o Hangzhou, desarrollo de reglas de mahjong a medida, despliegues privados u otras colaboraciones, contáctanos por QQ o WeChat.

### 5. Agradecimientos

Equipo de desarrollo: Xelnaga (programador principal)  雪枫XueFun9 (diseño de fichas)  Zoe (diseño de semilla aleatoria)  Natsuki (autor de 新编MCR)  坏狗影子 (stickers/emotes)  GitHub/baisebaoma (diseño de bot de alto rendimiento)  troooobot (implementación de Mahjong de Changsha)  Zoe (implementación de Mahjong de Taiwán)  二階堂 ヒロ (diseño del panel central)  莫莫柴 (Qingque, mesa 2D)  Shq, Zoe, ShiauLo (corrección de errores)  PCX1078 (bot de datos de Salasasa)  大海 (puntuación de K-Mahjong)  新手求教 (desarrollo de plugins)

Diseño de reglas personalizadas: 莫莫柴 (青雀)  关兆豪先生 (中庸麻将)  Null (虹雀)  K神传说 (K神麻将)  恋绘色 (香港清章恋绘色魔改)  小林 (国标小林改)  蓝十 (国标蓝十改)  自恧 (浪涌麻将)  南瓜饼 (南雀)  Gostop (新中式麻将)  soka (散花天才麻将)

Investigación de reglas regionales: Shanghai Qing Hun Peng (何苏)  Mahjong clásico (莫莫柴, 何苏, 古明地梦)  Mahjong de Yixing (苦鱼)  Reglas promocionales MIL (MIL国际麻将联盟)  Mahjong vietnamita (Bridget Young)  Mahjong de Jiujiang (芋蔗)

Transmisiones y difusión: Cloud980Ti  轻轻的飘  等候1234  立直喵  Ce/Est1al  鬼头刀鱼fish  神源之心  方片杰克  Ni9e_9  大柠劳斯  大口啃排骨

Patrocinadores: 九曜, soka, kiki, 健哥, 何苏, Null, 莫莫柴, 恋绘色, Zazaka, 中山大学国标麻将同好会, 东西喵, 山东大学老年人活动中心, Sw123

Agradecimiento especial: 莫莫柴, 码龙, Null, 坏狗影子, 何苏, FFMJ法国麻将协会 (apoyo de traducción)

Comunidades colaboradoras: 棋牌游戏研究院, 立直麻雀研习社, 柴の麻将群, 雀渣玩家群, 中山大学国标麻将同好会

Pruebas iniciales: 夜色祢  chlorine  陪练的命运

¡Y a todos los jugadores que disfrutan de la plataforma Salasasa!

*Última actualización: 25 de noviembre de 2026, dev ver 0.5.92.0*
