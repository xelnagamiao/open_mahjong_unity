# 海外 MCR 多语言入口：维护与部署

研究与实现日期：2026-10-06。2026-10-07 已验证生产英法静态页及 Nginx 路由；同日维护法语入口文案、游戏语言参数和结构化数据。每次发布仍需重新验收；Search Console 抓取、收录与排名不由本地测试保证。

## 现状与方案

网页位于 `open_mahjong_web/client`，采用 Vue 3、Vue Router history 路由和 Vite。`/2d` 是 Vue/Pixi 大厅，`/game-unity` 是独立的 Unity 入口。加入 2D 游戏需要网站账户；目前支持简繁中文、英文、日文和法语界面。

已有 `src/seo.js` 集中配置各路由的标题、描述；原 `scripts/prerender-seo.mjs` 只输出 head，正文保留空的 `#app`，属于 metadata 预生成，不能视为完整 SSR。修改前直接请求线上 `/en/mcr`，得到 HTTP 200、中文首页标题、`lang="zh-CN"`、指向首页的 canonical 和空应用节点。原 sitemap 手工维护，统一使用 2026-08-08 日期，已与预生成路由有差异。

服务端代码确认生产前端由 Nginx 提供，Express 负责 API；初次实现时未取得线上 Nginx 的实际配置，生产部署时已核对配置并启用独立的 MCR 路由 include。采用现有构建输出完整静态页面 `/en/mcr`、`/fr/mcr`，保留主站与游戏架构。落地页不加载 Vue 主包或 Unity，正文与导航无需 JavaScript。英文页的两个 CTA 打开 `/2d?lang=en`，法语页的两个 CTA 打开 `/2d?lang=fr`，在存储语言和浏览器语言之前明确选用对应游戏语言。

Vue 中同时保留对应路由，使用与静态页相同的内容渲染器；首页增加正常 HTML 链接。页面语言由 URL 决定，游戏语言独立设置，不自动按浏览器语言跳转落地页。

浏览器检查同时发现英文登录入口的“用户名 / 邮箱”和“忘记密码？”缺少翻译，已补齐，并补齐登录、注册、重置密码页的英文标题。账户规约原文仍为中文，本次未改写其内容。

## 六种语言的术语依据

以下证据确认协会与俱乐部的用语。查询建议由这些用语和“在线玩、免费、浏览器”意图组合而来，**不是搜索量排名**。本次没有付费关键词数据、Search Console 查询数据或各国市场份额数据。

| 语言 | 当地术语证据 | 搜索入口建议 | 避免混淆 |
| --- | --- | --- | --- |
| 英语 | [EMA](https://mahjong-europe.org/portal/index.php?Itemid=167&id=31&option=com_content&view=article) 使用 Mahjong Competition Rules (MCR)、Green Book；[英文游戏入口](https://kmahjong.ai/en/chinese-mahjong)也使用 Chinese Official Mahjong | play MCR online；MCR Mahjong online；Chinese Official Mahjong online；Mahjong Competition Rules online；free browser MCR | 不以未限定规则的 mahjong online 为主，以免吸引消除麻将意图；不用生硬的 national standard mahjong |
| 法语 | [FFMJ](https://www.ffmahjong.fr/FFMJ-site/mahjong.php?ancien=0) 使用 Règles MCR、Mahjong Competition Rules、mah-jong，并提供在线 MCR 资源 | mahjong MCR en ligne；jouer au mahjong MCR；mahjong MCR gratuit；mah-jong MCR；mahjong sans téléchargement | 不把直译的“官方中国规则”作为唯一主词；法语游戏入口使用明确语言参数 |
| 德语 | [DMJL](https://dmjl.de/mah-jongg/mahjong-competition-rules/) 保留英文规则名；[汉诺威分会](https://dmjl.de/?p=391) 使用 MCR aka CO，协会名称采用 Mah-Jongg | MCR Mahjong online spielen；MCR Mahjong kostenlos；Mahjong Competition Rules；Mah-Jongg MCR | klassisches Mahjong 是另一规则体系 |
| 荷兰语 | [ENMV Rotterdam](https://www.enmv.nl/nl/club/) 使用 Mahjong Competitie Regels (MCR)、Officiële Chinese regels；[Haagse Kringen](https://www.mahjongdenhaag.nl/spelregels) 使用 Spelregels MCR | MCR mahjong online spelen；gratis MCR mahjong；MCR spelregels；Officiële Chinese regels | NTS 是不同的荷兰比赛规则 |
| 西班牙语 | [FEMJ Wiki](https://wiki.femj.es/index.php?title=Reglas_de_Competici%C3%B3n_de_Mahjong) 使用 Reglas de Competición de Mahjong、Mahjong Competition Rules、MCR | mahjong MCR en línea；jugar mahjong MCR online；mahjong MCR gratis；Reglas de Competición de Mahjong | 不直译 Guobiao 作为主词；不以泛词 mahjong gratis 为唯一目标 |
| 意大利语 | [FIMJ](https://www.fimj.it/?page_id=744) 使用 Regolamento Internazionale – MCR，协会名称采用 Mah Jong | mahjong MCR online；giocare a mahjong MCR；mahjong MCR gratis；regolamento internazionale MCR | mahjong solitario 属于消除游戏 |

## 英法页面文案

### `/en/mcr`

- Title：Play MCR Mahjong Online — Free in Your Browser | Salasasa
- Description：Play MCR Mahjong online for free on Salasasa. Chinese Official Mahjong in your browser, with multiplayer tables, automatic scoring and replays. No download.
- H1：Play MCR Mahjong Online — Free in Your Browser
- CTA：Open 2D Mode — Play MCR → `/2d?lang=en`
- 入口提示：The 2D game opens in English. A Salasasa account is required to join games.
- 正文自然解释 MCR、Mahjong Competition Rules、Chinese Official Mahjong、Guobiao，并回答免费、下载、账户、四人麻将与消除游戏的区别。

### `/fr/mcr`

- Title：Mahjong MCR en ligne gratuit, sans téléchargement | Salasasa
- Description：Jouez au mahjong MCR en ligne gratuitement sur Salasasa : tables multijoueurs, calcul des points et relecture des parties. Sans téléchargement, jeu en français.
- H1：Jouez au mahjong MCR en ligne, gratuitement
- CTA：Jouer au MCR — ouvrir le mode 2D → `/2d?lang=fr`
- 入口提示：Le jeu 2D s’ouvre en français. Un compte Salasasa est nécessaire pour rejoindre une partie.
- FAQ：明确说明 2D 游戏支持法语，保留其他已支持语言和大厅语言切换说明；英文页的语言 FAQ 同样列出 French。
- 以 MCR / règles MCR 为主，正文带 mah-jong chinois officiel 别名，并链接 FFMJ 资源。

平台图书馆包含 Natsuki 的《新编 MCR》和各改编规则。此次没有逐条验证计分实现与 EMA 比赛版是否完全一致。因此文案要求查看平台规则与房间设置，并区分欧洲赛事所用规则；没有添加协会背书、官方认证、即时匹配或免账户游玩的承诺。法语界面说明仅对应已实现的 2D 游戏界面，不宣称全部规则资料已翻译。

## 后续四种语言的设计草案

`src/seo/mcr-pages.js` 已预留四种语言的 URL、名称和 OG locale，设置 `published: false`。目前不会生成这些页面，也不会加入 Vue 路由、语言切换、hreflang 或 sitemap。下列文案需母语审校后补齐完整正文。

| URL | 草案 Title | 草案 H1 |
| --- | --- | --- |
| `/de/mcr` | MCR Mahjong online spielen – kostenlos \| Salasasa | MCR Mahjong online spielen – kostenlos im Browser |
| `/nl/mcr` | MCR mahjong online spelen, gratis in je browser \| Salasasa | Speel MCR mahjong online, gratis in je browser |
| `/es/mcr` | Mahjong MCR en línea gratis \| Salasasa | Juega al mahjong MCR gratis en tu navegador |
| `/it/mcr` | Mahjong MCR online gratis, senza download \| Salasasa | Gioca a mahjong MCR online gratis nel browser |

Description 草案：

- 德语：Spiele MCR Mahjong kostenlos im Browser: Mehrspielertische, automatische Punkteberechnung und Partien zum Nachspielen. Ohne Download; Spieloberfläche auf Englisch.
- 荷兰语：Speel MCR mahjong online op Salasasa: multiplayer, automatische puntentelling en partijen terugkijken. Geen download nodig; de spelinterface is in het Engels.
- 西班牙语：Juega al mahjong MCR gratis en Salasasa: mesas multijugador, cálculo automático de puntos y repeticiones. Sin descargas; la interfaz del juego está en inglés.
- 意大利语：Gioca a mahjong MCR su Salasasa: tavoli multigiocatore, calcolo automatico dei punti e replay delle partite. Gratis, senza download; interfaccia di gioco in inglese.

扩展时在 `COPY` 中加入完整正文，新增 `public/seo/mcr-语言.png`（1200×630），审校后将对应 `published` 改为 true。路由、静态正文、互链与 sitemap 自动从已启用集合生成。游戏语言与落地页语言保持分开，实际支持前继续以英文进入。

## 技术 SEO 约定

- **Canonical**：每页指向 `https://salasasa.cn/语言/mcr`，无末尾斜线、查询参数；翻译页不指向英文页或中文首页。
- **hreflang**：已启用页包含 en、fr 与 x-default，默认英文页。绝对 URL、双向互指、包含自身。使用语言代码覆盖跨国玩家；中文首页不是等价翻译，不纳入这一组。
- **Open Graph / Twitter**：独立标题、描述、URL、locale（en_US/fr_FR）、另一语言 locale、1200×630 PNG 与替代文字。应用内部导航时同步更新；离开落地页后清理专属标签。
- **JSON-LD**：语言对应的 WebPage、所属 WebSite、免费 2D 游戏的 WebApplication；应用的 inLanguage 复用实际 SUPPORTED_LOCALES（含 fr），避免与界面支持列表分离。不虚构评分、评论、背书。FAQ 作为可见正文，不承诺 FAQ 富结果。
- **Sitemap**：构建从 PRERENDER_PATHS 自动生成，排除 NOINDEX_PAGES，附双向语言关联。删除手写 `public/sitemap.xml`；开发服务也提供同源 XML。不给未知修改时间写 lastmod。
- **Robots**：原 Allow: / 已允许落地页和图片；原账户、后台、游戏记录限制保留，生成的 sitemap 排除这些私有页面。
- **可索引正文**：完整标题、H1、介绍、功能、规则说明、步骤、FAQ 和 CTA 位于 HTTP 响应正文。不依赖登录、API、存储偏好或 JavaScript 才显示内容。

以上依据 [Google 多语言版本说明](https://developers.google.com/search/docs/specialty/international/localized-versions)和 [JavaScript SEO 说明](https://developers.google.com/search/docs/crawling-indexing/javascript/javascript-seo-basics)。可抓取正文解决技术入口问题，不代表已经收录或保证排名。

## 部署与验收

继续在 `client` 执行现有 `npm run build`。构建会同时输出原应用、原路由 TDK 壳、两种语言完整 HTML 和 `dist/sitemap.xml`。按原部署配置保留人工安装的 Unity/手机包。

首次部署时将 `nginx-mcr-seo.conf` 合并到现有 Nginx **server 块内部**，放在其他正则 location 之前，继承原 dist root。若已有 `/sitemap.xml` 的精确 location，请合并而非重复定义。先执行 `nginx -t` 再 reload。生产已启用此 include 时，单纯更新页面和文案不需重复插入路由或重启后端。

上线验收：

1. `/en/mcr`、`/fr/mcr` 返回 200，源 HTML 中含完整对应语言正文；不要仅用浏览器执行 JS 后的截图证明可索引。
2. `/en/mcr/`、`/en/mcr/index.html` 单次 301 到 `/en/mcr`，保留查询参数；法语同样处理。
3. 未启用的 de/nl/es/it 入口返回 404，避免回落到中文首页并返回 200。
4. 两张分享图片返回 image/png；sitemap 为有效 XML，并含英法 URL 与互指；核对 robots 与 CDN/WAF 的实际抓取策略。
5. 将大厅语言先选为中文，再从法文页点击 CTA；URL 应为 `/2d?lang=fr`，语言选择为 Français，标题为 Salon MCR。英文页 CTA 仍进入 `/2d?lang=en` 与 MCR Lobby；两个入口均优先于存储语言。登录和注册继续走已有网站流程。
6. 在站点所有者的 Google Search Console 提交 sitemap，检查两个 live URL 的抓取正文、选择的 canonical，以及后续 MCR 查询的曝光与点击。本次未做真实跨地区延迟测量。

`npm run test:seo` 验证正文与 metadata 生成、语言关联、草稿/私有页排除、原有壳页保留和明确游戏语言选择。验证产物位于被忽略的 `.om_workspace/`；生产 Vite 构建也可指定该目录下的隔离 outDir，再将同一路径传给 `scripts/prerender-seo.mjs`，避免触碰已有游戏包。

## 人工术语确认

- 法语：mahjong / mah-jong 的编辑拼法；relecture des parties / replays；salon / lobby；mah-jong chinois officiel 是否适合作为 FFMJ 受众的次要别名。
- 英语：Chinese Official Mahjong / Chinese Official 的次要标签，以及向 EMA 玩家说明平台 MCR 规则资料时采用的正式名称与版本。
- 德语：Mahjong / Mah-Jongg；继续使用 MCR / Mahjong Competition Rules，不能与 classical 混用。
- 荷兰语：Officiële Chinese regels / Chinees Officieel；Mahjong Competitie Regels 的当地拼法。
- 西班牙语：en línea / online；保留 FEMJ 的 Reglas de Competición de Mahjong。
- 意大利语：mahjong / Mah Jong；保留 FIMJ 的 Regolamento Internazionale – MCR。

Google 将 [.cn 视为明确的国家信号](https://developers.google.com/search/docs/specialty/international/managing-multi-regional-sites)。这次沿用现有域名；若海外获客成为主方向，可另行评估通用域名策略，不应临时增加 canonical 冲突的镜像站。
