import { SUPPORTED_LOCALES } from '../i18n/locale-utils.js'

/** URL language is independent of the game's supported UI languages.
 * Only complete, enabled pages belong in routes, alternates and the sitemap.
 */
export const MCR_LOCALES = [
  { locale: 'en', path: '/en/mcr', label: 'English', ogLocale: 'en_US', published: true },
  { locale: 'fr', path: '/fr/mcr', label: 'Français', ogLocale: 'fr_FR', published: true },
  { locale: 'de', path: '/de/mcr', label: 'Deutsch', ogLocale: 'de_DE', published: false },
  { locale: 'nl', path: '/nl/mcr', label: 'Nederlands', ogLocale: 'nl_NL', published: false },
  { locale: 'es', path: '/es/mcr', label: 'Español', ogLocale: 'es_ES', published: false },
  { locale: 'it', path: '/it/mcr', label: 'Italiano', ogLocale: 'it_IT', published: false },
]

const COPY = {
  en: {
    title: 'Play MCR Mahjong Online — Free in Your Browser | Salasasa',
    description: 'Play MCR Mahjong online for free on Salasasa. Chinese Official Mahjong in your browser, with multiplayer tables, automatic scoring and replays. No download.',
    h1: 'Play MCR Mahjong Online — Free in Your Browser',
    eyebrow: 'Chinese Official Mahjong · Mahjong Competition Rules',
    intro: 'A four-player mahjong table, right in your browser. Play MCR on Salasasa with automatic scoring and game replays, without installing a client.',
    cta: 'Open 2D Mode — Play MCR',
    gameNote: 'The 2D game opens in English. A Salasasa account is required to join games.',
    languageLabel: 'Page language',
    mainSite: 'Main website',
    featuresTitle: 'Free browser MCR, with the tools to review your play',
    features: [
      { title: 'Multiplayer tables', text: 'Join the 2D lobby to find games or create a room with your own settings.' },
      { title: 'Automatic scoring', text: 'The platform calculates winning-hand combinations and points, so you can focus on your decisions.' },
      { title: 'Game replays', text: 'Return to your games in the replay viewer and review how a hand developed.' },
    ],
    rulesTitle: 'MCR, Chinese Official Mahjong and Guobiao',
    rulesText: 'MCR stands for Mahjong Competition Rules. This four-player rules family is also known as Chinese Official Mahjong or Guobiao Mahjong. Players build scoring combinations, called fan, by drawing and claiming tiles.',
    rulesNote: 'Salasasa uses its published Guobiao/MCR reference and configurable room settings. Check the platform reference before playing; for European tournaments, consult the EMA Green Book and the organiser’s regulations.',
    platformRules: 'Salasasa rule reference (Chinese)',
    communityRules: 'EMA MCR rules (English)',
    communityUrl: 'https://mahjong-europe.org/portal/index.php?Itemid=167&id=31&option=com_content&view=article',
    startTitle: 'How to play MCR online on Salasasa',
    steps: [
      'Open 2D Mode below. The lobby loads directly in your browser, in English.',
      'Log in with your Salasasa account, or register through the website login screen.',
      'Connect your account to the game, then join a table or create a room. Check the room’s rules and settings.',
    ],
    faqTitle: 'Before your first game',
    faq: [
      { question: 'Is MCR mahjong a tile-matching solitaire game?', answer: 'MCR is a four-player mahjong game. You draw and discard tiles, claim tiles from other players, and build a winning hand with scoring combinations.' },
      { question: 'Can I play Mahjong Competition Rules online without downloading anything?', answer: 'Yes. Salasasa’s 2D mode runs in a web browser. Open the lobby and log in to join games; no Unity client download is needed.' },
      { question: 'Is it free, and do I need an account?', answer: 'Browser play on Salasasa is free. You need a Salasasa account to join games. This account is shared with the main website.' },
      { question: 'Which languages does the 2D game support?', answer: 'The 2D interface supports Simplified and Traditional Chinese, English, Japanese and French. This page opens the game in English; you can change its language in the lobby.' },
    ],
    sourceTitle: 'Open source, from table to tools',
    sourceText: 'Salasasa is the example server for open_mahjong_unity, an open-source mahjong platform. Explore the source code or use the website’s scoring and rule-reference tools alongside your games.',
    sourceLink: 'View the source on GitHub',
    imageAlt: 'Salasasa — play MCR Mahjong online in your browser',
  },
  fr: {
    title: 'Mahjong MCR en ligne gratuit, sans téléchargement | Salasasa',
    description: 'Jouez au mahjong MCR en ligne gratuitement sur Salasasa : tables multijoueurs, calcul des points et relecture des parties. Sans téléchargement, jeu en français.',
    h1: 'Jouez au mahjong MCR en ligne, gratuitement',
    eyebrow: 'Mahjong Competition Rules · Règles MCR',
    intro: 'Retrouvez une table de mahjong à quatre dans votre navigateur. Salasasa propose le jeu MCR en ligne, le calcul automatique des points et la relecture des parties, sans installer de logiciel.',
    cta: 'Jouer au MCR — ouvrir le mode 2D',
    gameNote: 'Le jeu 2D s\'ouvre en français. Un compte Salasasa est nécessaire pour rejoindre une partie.',
    languageLabel: 'Langue de la page',
    mainSite: 'Site principal',
    featuresTitle: 'Mahjong MCR gratuit dans votre navigateur',
    features: [
      { title: 'Tables multijoueurs', text: 'Accédez au salon 2D pour trouver une partie ou créer une table avec vos propres paramètres.' },
      { title: 'Calcul automatique des points', text: 'La plateforme calcule les combinaisons de la main gagnante et les points : vous pouvez vous concentrer sur vos choix de jeu.' },
      { title: 'Relecture des parties', text: 'Retrouvez vos parties dans le lecteur de replays pour revoir le déroulement de chaque main.' },
    ],
    rulesTitle: 'MCR : les règles de compétition du mahjong',
    rulesText: 'MCR signifie Mahjong Competition Rules. Cette famille de règles pour le mahjong à quatre est aussi appelée « mah-jong chinois officiel » ou Guobiao. Les joueurs piochent, défaussent et réclament des tuiles pour former des combinaisons qui rapportent des points.',
    rulesNote: 'Salasasa utilise sa référence Guobiao/MCR et des paramètres de table configurables. Consultez cette référence avant de jouer. Pour les tournois européens, reportez-vous au Green Book de l\'EMA et au règlement de l\'organisateur.',
    platformRules: 'Référence des règles Salasasa (en chinois)',
    communityRules: 'Règles MCR et ressources de la FFMJ',
    communityUrl: 'https://www.ffmahjong.fr/FFMJ-site/mahjong.php?ancien=0',
    startTitle: 'Comment jouer au mahjong MCR en ligne sur Salasasa',
    steps: [
      'Ouvrez le mode 2D ci-dessous : le salon se charge directement dans votre navigateur, en français.',
      'Connectez-vous avec votre compte Salasasa ou inscrivez-vous depuis l\'écran de connexion du site.',
      'Connectez votre compte au jeu, puis rejoignez une table ou créez-en une. Vérifiez les règles et les paramètres de la table.',
    ],
    faqTitle: 'Avant votre première partie',
    faq: [
      { question: 'Le mahjong MCR est-il un jeu de solitaire ?', answer: 'Le MCR est un jeu de mahjong à quatre. Vous piochez et défaussez des tuiles, réclamez celles des autres joueurs et construisez une main gagnante avec des combinaisons qui rapportent des points.' },
      { question: 'Peut-on jouer au mahjong MCR sans téléchargement ?', answer: 'Oui. Le mode 2D de Salasasa fonctionne dans un navigateur. Ouvrez le salon et connectez-vous pour rejoindre une partie ; il n\'est pas nécessaire de télécharger un client Unity.' },
      { question: 'Le jeu est-il gratuit et faut-il un compte ?', answer: 'Le jeu sur navigateur est gratuit. Un compte Salasasa est nécessaire pour rejoindre une partie. Ce compte est commun au jeu et au site principal.' },
      { question: 'Le jeu est-il disponible en français ?', answer: 'Oui. L\'interface du jeu 2D est disponible en français, ainsi qu\'en chinois simplifié et traditionnel, en anglais et en japonais. Le bouton ouvre le jeu en français ; vous pouvez changer sa langue dans le salon.' },
    ],
    sourceTitle: 'Une plateforme de mahjong open source',
    sourceText: 'Salasasa est le serveur d\'exemple du projet open_mahjong_unity. Vous pouvez consulter son code source et utiliser les outils de calcul des points et les références de règles du site pour accompagner vos parties.',
    sourceLink: 'Consulter le code sur GitHub',
    imageAlt: 'Salasasa — mahjong MCR gratuit en ligne, dans votre navigateur',
  },
}

export const MCR_PAGES = MCR_LOCALES.filter((entry) => entry.published).map((entry) => {
  if (!COPY[entry.locale]) throw new Error(`Missing MCR copy for ${entry.locale}`)
  const gameLocale = SUPPORTED_LOCALES.includes(entry.locale) ? entry.locale : 'en'
  return { ...entry, ...COPY[entry.locale], gameLocale, gamePath: `/2d?lang=${gameLocale}` }
})

export function mcrPageForPath(path) {
  const normalized = String(path).replace(/\/+$/, '')
  return MCR_PAGES.find((page) => page.path === normalized) || null
}

export function mcrAlternates(domain) {
  return [
    ...MCR_PAGES.map((page) => ({ language: page.locale, href: domain + page.path })),
    { language: 'x-default', href: domain + '/en/mcr' },
  ]
}

export function mcrStructuredData(page, domain) {
  const url = domain + page.path
  return {
    '@context': 'https://schema.org',
    '@graph': [
      {
        '@type': 'WebPage', '@id': url + '#page', url,
        name: page.h1, description: page.description, inLanguage: page.locale,
        isPartOf: { '@type': 'WebSite', '@id': domain + '/#website', name: 'Salasasa', url: domain + '/' },
        about: { '@id': domain + '/2d#application' },
      },
      {
        '@type': 'WebApplication', '@id': domain + '/2d#application',
        name: 'Salasasa 2D — MCR Mahjong', url: domain + '/2d',
        applicationCategory: 'GameApplication', operatingSystem: 'Web browser',
        isAccessibleForFree: true,
        offers: { '@type': 'Offer', price: '0', priceCurrency: 'USD' },
        inLanguage: [...SUPPORTED_LOCALES],
      },
    ],
  }
}
