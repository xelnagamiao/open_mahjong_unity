# 多语言 SEO：内容与部署约定

用户已明确要求：公开页面只能保留原有内容及忠实翻译。SEO 工作不得自行增加宣传段落、规则摘要、FAQ、承诺或功能说明，也不能在译文中添加原文没有的信息。

首页保持原有布局和介绍：“Salasasa平台是open_mahjong_unity项目的示例服务器，目前支持国标/立直/青雀/川麻/长麻以及一些子规则”。

## 正文来源

独立语言页保留固定 URL，正文由 `client/scripts/original-seo-content.mjs` 从既有源文件生成，不再维护另一套营销文案：

| 页面 | 正文来源 |
| --- | --- |
| `/en/mcr`、`/fr/mcr`、`/zh/mcr` | `Home.vue` 原平台介绍、原游戏入口标签，以及目录中的规则名称 |
| `/en/riichi` | 原立直目录介绍、规则书与首页平台介绍和入口 |
| `/en/mahjong-rules`、`/fr/mahjong-rules`、`/zh/mahjong-rules` | 原规则书标题、原目录中的国标／立直／台湾／四川／长沙介绍及规则书链接 |
| `/en/mahjong/mcr`、`/fr/mahjong/mcr`、`/zh/mahjong/mcr` | `constants/libraryRules.js` 的国标条目 |
| `/en/mahjong/riichi`、`/en/mahjong/taiwanese`、`/en/mahjong/sichuan`、`/en/mahjong/changsha` | 同一目录的对应规则条目 |
| `/en/guide`、`/fr/guide`、`/zh/guide` | `UsageGuide.vue` 全部 15 个原章节，包括机制、赛事、鸣谢和账户规约 |

翻译复用站点的 `i18n/messages.js`，新增翻译必须有对应中文源文本。贡献者名称和引文中需要保留的原名不改写；FFMJ 已提供的番种译名保持原样。不得用自行概括的短文替代完整说明书。

指南通过 Vue 的构建期服务端渲染生成，再按原字典翻译文本节点；复用原说明书样式。此过程使用 Vite middleware mode，不启动监听端口，不影响共享开发服务器。线上只提供静态 HTML，不运行这套生成逻辑。静态指南的规约按钮链接到同页已有规约章节。

## 技术约定

- `src/seo/pages.js` 只保存 17 个页面的 URL、语言、正文来源和搜索元数据，不保存独立创作文案。
- 每页 canonical 指向自身正式 HTTPS URL。有对应翻译的页面按 family 双向设置 hreflang（en、fr、zh-CN、x-default）；不声明不存在的译本。
- 所有正文、链接与标题均在首次 HTML 响应内。语言由 URL 决定，不依赖浏览器语言、登录或 JavaScript。MCR 游戏入口保留明确的 `/2d?lang=...`。
- SPA 内进入这些语言 URL 时，加载同一个完整静态文档，避免产生另一套正文。
- Sitemap 从实际公开路由生成，排除私有页面与未发布语言。不为未知修改时间生成 lastmod。
- JSON-LD 描述 WebPage 及所属 WebSite；不虚构评分、评论、FAQ 或协会背书。MCR 页面继续使用原分享图片。
- Cloudflare Web Analytics 仍从独立生产配置注入；本机预览不发送统计。账户地区功能不由这些静态页处理。
- 现有首页、导航、页脚、图书馆、详细指南及 2D 大厅的布局保持原样。

## 构建和验证

运行 `npm run build`，或者向 Vite 和 `scripts/prerender-seo.mjs` 传递同一个隔离输出目录（放在 Git 忽略的 `.om_workspace/`）。生成原应用、原路由 metadata、17 个固定语言页面和 sitemap；保留部署配置中人工放置的 Unity／手机包。

运行 `node --test tests/analytics.test.mjs tests/mcrSeo.test.mjs tests/translationAudit.test.mjs`。检查原文来源、指南章节完整性、游戏链接、语言正文、canonical、hreflang、sitemap、200/301/404、HEAD 及原 SPA 壳不被改写。英文和法文中的原贡献者姓名不视为漏译。

上线使用现有 Nginx include，核对与仓库 `nginx-mcr-seo.conf` 一致。末尾斜线和 index.html 应单次 301 到正式路径，未发布语言返回 404。只有配置实际变化才需要 nginx -t 和 reload。

上线后逐页抓取并用浏览器检查首页原介绍、英法指南和规则页。抓取成功不能证明搜索引擎已收录或排名提高，仍需由站点所有者在 Search Console 查看实际结果。