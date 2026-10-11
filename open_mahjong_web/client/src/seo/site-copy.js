/** Search and share metadata only; never used to render the visible page. */
const COPY = {
  "zh-CN": {
    "title": "Salasasa国标麻将对战平台-萨拉飒飒",
    "description": "在 Salasasa 免费在线玩国标麻将 MCR，支持自动算番、段位匹配和牌谱复盘。浏览器 2D 无需下载，另有多规则客户端与麻将学习工具。",
    "gameTitle": "免费在线玩国标麻将 MCR | Salasasa",
    "gameDescription": "在浏览器中免费进行国标麻将 MCR 对局，支持自动算番、段位匹配与牌谱复盘，界面提供简繁中文、英语、日语和法语。"
  },
  "en": {
    "title": "Play MCR Mahjong Online Free | Salasasa",
    "description": "Play MCR Mahjong online for free on Salasasa. Browser multiplayer, automatic scoring, ranked games and replays, plus rules guides and scoring tools.",
    "gameTitle": "Play MCR Mahjong Online Free | Salasasa",
    "gameDescription": "Play free four-player MCR Mahjong in your browser with automatic scoring, ranked games and replays. The 2D interface supports English, French, Chinese and Japanese."
  },
  "fr": {
    "title": "Mahjong MCR en ligne gratuit, sans téléchargement | Salasasa",
    "description": "Jouez au mahjong MCR gratuitement sur Salasasa : tables multijoueurs, calcul des points, parties classées et replays dans votre navigateur.",
    "gameTitle": "Mahjong MCR en ligne gratuit | Salasasa",
    "gameDescription": "Jouez au MCR à quatre dans votre navigateur : calcul automatique des points, parties classées et replays. Interface en français, anglais, chinois et japonais."
  }
}

export function siteCopy(locale) {
  return COPY[locale] || null
}

export function localizedRouteCopy(path, locale) {
  const copy = siteCopy(locale)
  if (!copy) return null
  if (path === '/') return { title: copy.title, description: copy.description }
  if (path === '/2d') return { title: copy.gameTitle, description: copy.gameDescription }
  return null
}
