import { ref } from 'vue'
import { getLibraryRule } from '@/constants/libraryRules'

const catalog = ref([])
const phy = ref(null)
const areal = ref(null)
const archives = ref({})
const loaded = ref(false)
const loadError = ref('')

/** 规则 slug → 资料标签 / 文件名关键词（兜底；优先用 source.rules） */
const TAG_MAP = {
  yezi: ['yezi', '叶子', '叶子戏', '叶格'],
  madiao: ['madiao', '马吊'],
  mohe: ['mohe', '默和'],
  penghe: ['penghe', '碰和'],
  tianjiu: ['tianjiu', '牌九', '天九'],
  'proto-mahjong': ['proto', '中发', '叉麻雀', 'chungfa', 'chung fa'],
  classical: ['early-manual', 'classical', '古典', '绘图麻雀', '想定宁波', 'shinbara', 'shen-yifan'],
  'drawing-mahjong': ['drawing', '绘图麻雀'],
  qingzhang: ['qingzhang', '清章', '旧章', 'oldstyle'],
  hongkong: ['hongkong', '港麻', 'hkma'],
  guangdong: ['guangdong', '广东麻将', '新章'],
  jipinghu: ['jipinghu', '鸡平胡'],
  tuidao: ['tuidao', '推倒'],
  macau: ['macau', '澳门麻将', '传统麻雀', '简易麻雀'],
  'hongkong-16': ['hongkong-16', '港式十六', '16-tile', '16tile'],
  'japanese-classical': ['japanese-classical', '日本古典', '名川'],
  riichi: ['riichi', '立直', '天凤', '雀魂', 'tenhou'],
  'riichi-hochi': ['riichi-hochi', '报知', '报知ルール', '途中立直'],
  'japanese-3p': ['japanese-3p', '三人'],
  'korean-3p': ['korean-3p', '韩国三人'],
  babcock: ['babcock', '红皮书'],
  nmjl: ['nmjl', 'National Mah Jongg'],
  'nmjl-joker': ['nmjl-joker', 'joker'],
  'nmjl-card': ['nmjl-card', '年卡'],
  'wright-patterson': ['wright-patterson', 'wright'],
  fuzhou: ['fuzhou', '福州'],
  taiwan: ['taiwan', '台麻'],
  'taiwan-south': ['taiwan-south', '南部无花', '南部台'],
  'taiwan-mingxing': ['taiwan-mingxing', '明星三缺一'],
  shanghai: ['shanghai', '上海麻将', '敲麻'],
  'ningbo-local': ['ningbo-local', '宁波麻将'],
  hangzhou: ['hangzhou', '杭州麻将', '杭麻', '白板财神'],
  'mil-hangzhou': ['mil-hangzhou', '杭州麻将（推广）'],
  huama: ['huama', '花麻', '算花'],
  suzhou: ['suzhou', '苏州麻将', '苏麻'],
  nanjing: ['nanjing', '南京麻将', '论花'],
  wenzhou: ['wenzhou', '温州麻将'],
  'mil-wenzhou': ['mil-wenzhou', '温州麻将（试点）'],
  'sichuan-nochi': ['sichuan-nochi', '去字', '禁吃'],
  sichuan: ['sichuan', 'xuezhan', '川麻', '血战'],
  'sichuan-huansanzhang': ['huansanzhang', '换三张'],
  xueliu: ['xueliu', '血流'],
  'yichang-xueliu': ['yichang-xueliu', '宜昌血流', '血流换三张'],
  duankagen: ['duankagen', '断卡根', '断卡钩'],
  chongqing: ['chongqing', '重庆麻将'],
  guizhou: ['guizhou', '贵州麻将'],
  changsha: ['changsha', '长沙'],
  changde: ['changde', '常德麻将'],
  hongzhong: ['hongzhong', '红中', '常德红中'],
  'sichuan-hongzhong': ['sichuan-hongzhong', '四川红中', '川麻红中'],
  'mil-red-center': ['mil-red-center', '红中麻将（推广）'],
  beijing: ['beijing', '北京麻将', '京麻'],
  tianjin: ['tianjin', '天津麻将', '津麻', '混儿吊', '捉伍'],
  wuhan: ['wuhan', '武汉麻将'],
  kawuxing: ['kawuxing', '卡五星', '掐五星'],
  nanchang: ['nanchang', '南昌麻将'],
  dongbei: ['dongbei', '东北麻将'],
  'dongbei-yaobao': ['yaobao', '摇宝'],
  'dongbei-xiadan': ['xiadan', '下蛋'],
  'dongbei-jiahu': ['jiahu', '夹胡'],
  haerbin: ['haerbin', '哈尔滨麻将', '哈麻'],
  'dalian-qionghu': ['dalian-qionghu', '大连穷胡', '穷胡'],
  shanxi: ['shanxi', '山西麻将', '晋麻'],
  'taiyuan-lisi': ['taiyuan-lisi', '立四', '太原立四'],
  'mil-shanxi': ['mil-shanxi', '山西麻将（推广）'],
  changchun: ['changchun', '长春麻将'],
  'mil-changchun': ['mil-changchun', '长春麻将（推广）'],
  xian: ['xian', '西安麻将', '陕麻'],
  'mil-guizhou': ['mil-guizhou', '贵州麻将（推广）', '捉鸡'],
  kunming: ['kunming', '昆明麻将'],
  singapore: ['singapore', '新加坡'],
  malaysia: ['malaysia', '马来西亚'],
  vietnam: ['vietnam', '越南麻将'],
  guobiao: ['mcr-1998', 'mcr', 'guobiao', '国标'],
  ema: ['ema'],
  'mil-sichuan': ['mil-sichuan', 'sbr'],
  'mil-guangdong': ['mil-guangdong', '广东推广'],
  'mil-tuidao': ['mil-tuidao', '推倒和推广'],
  'mil-riichi': ['mil-riichi'],
  qingque: ['qingque', '青雀'],
  hongque: ['hongque', '虹雀'],
  'guobiao-kobayashi': ['kobayashi', '小林'],
  'guobiao-lanshi': ['lanshi', '蓝十'],
  'guobiao-kshen': ['kshen', 'K神'],
  zungjung: ['zungjung', '中庸'],
  shiyangjin: ['shiyangjin', '十样锦'],
  jiandan: ['jiandan', '南雀'],
}

export function useMahjongCatalog() {
  async function load() {
    if (loaded.value) return
    loadError.value = ''
    try {
      const [cRes, pRes, aRes, iRes] = await Promise.all([
        fetch('/rule-research/catalog.json'),
        fetch('/rule-research/phylogeny.json'),
        fetch('/rule-research/areal.json'),
        fetch('/rule-research/index.json'),
      ])
      if (!cRes.ok) throw new Error(`catalog HTTP ${cRes.status}`)
      catalog.value = (await cRes.json()).rules || []
      if (pRes.ok) phy.value = await pRes.json()
      if (aRes.ok) areal.value = await aRes.json()
      const index = iRes.ok ? (await iRes.json()).rules || [] : []
      const packs = await Promise.all(
        index.map(async (item) => {
          const res = await fetch(`/rule-research/${item.slug}.json`)
          if (!res.ok) return [item.slug, null]
          const ct = res.headers.get('content-type') || ''
          if (!ct.includes('json')) return [item.slug, null]
          return [item.slug, await res.json()]
        }),
      )
      archives.value = Object.fromEntries(packs.filter(([, v]) => v))
      loaded.value = true
    } catch (e) {
      loadError.value = e.message || String(e)
    }
  }

  function catalogRow(key) {
    if (!key) return null
    return (
      catalog.value.find((r) => r.slug === key || r.library_key === key) || null
    )
  }

  function libraryEntry(key) {
    const row = catalogRow(key)
    return getLibraryRule(key) || (row?.library_key ? getLibraryRule(row.library_key) : null)
  }

  function pageKey(id) {
    const row = catalogRow(id)
    if (row?.fold_into) return pageKey(row.fold_into)
    return row?.library_key || row?.slug || id
  }

  function ruleHref(id) {
    const row = catalogRow(id)
    if (row?.enter_href) return row.enter_href
    return `/library/${pageKey(id)}`
  }

  function ruleName(id) {
    return catalogRow(id)?.name_zh || getLibraryRule(id)?.label || id
  }

  function parentsOf(slug) {
    const s = catalogRow(slug)?.slug || slug
    const ids = []
    const row = catalogRow(s)
    if (row?.parent && !ids.includes(row.parent)) ids.push(row.parent)
    for (const e of phy.value?.edges || []) {
      if (e.to === s && !ids.includes(e.from)) ids.push(e.from)
    }
    return ids
  }

  function childrenOf(slug) {
    const s = catalogRow(slug)?.slug || slug
    const ids = []
    for (const r of catalog.value) {
      if (r.fold_into) continue
      if (r.parent === s && !ids.includes(r.slug)) ids.push(r.slug)
    }
    for (const e of phy.value?.edges || []) {
      if (e.from === s && !ids.includes(e.to)) ids.push(e.to)
    }
    for (const fam of areal.value?.families || []) {
      if (fam.trunk !== s) continue
      for (const m of fam.members || []) {
        if (m.id !== s && !ids.includes(m.id)) ids.push(m.id)
      }
    }
    return ids.filter((id) => !catalogRow(id)?.fold_into)
  }

  function familiesOf(slug) {
    const s = catalogRow(slug)?.slug || slug
    return (areal.value?.families || []).filter((fam) =>
      (fam.members || []).some((m) => m.id === s) || fam.trunk === s,
    )
  }

  function eraOf(slug) {
    const s = catalogRow(slug)?.slug || slug
    for (const era of phy.value?.eras || []) {
      for (const g of era.groups || []) {
        if ((g.nodes || []).includes(s)) {
          const track = (phy.value?.tracks || []).find((t) => t.id === g.track)
          return { era, track, trackId: g.track }
        }
      }
    }
    return null
  }

  function appearedOf(slug) {
    const s = catalogRow(slug)?.slug || slug
    return phy.value?.appeared?.[s] || ''
  }

  function sourcesFor(key) {
    const row = catalogRow(key)
    const slug = row?.slug || key
    const libKey = row?.library_key || ''
    const folded = catalog.value
      .filter((r) => r.fold_into === slug || r.fold_into === libKey)
      .map((r) => r.slug)
    const aliases = new Set(
      [slug, libKey, key, ...folded].filter(Boolean).map((x) => String(x).toLowerCase()),
    )
    const needles = new Set(
      [slug, libKey, row?.name_zh, ...(row?.names || []), ...(TAG_MAP[slug] || [])]
        .filter(Boolean)
        .map((x) => String(x).toLowerCase()),
    )
    const extraArch = new Set(folded)
    if (slug === 'classical' || libKey === 'classical') extraArch.add('drawing-mahjong')
    const out = []
    const seen = new Set()
    for (const [arch, data] of Object.entries(archives.value)) {
      const takeAll = extraArch.has(arch)
      for (const src of data.sources || []) {
        const tagged = (src.rules || []).some((r) => aliases.has(String(r).toLowerCase()))
        if (src.rules?.length) {
          if (!takeAll && !tagged) continue
        } else {
          const blob = `${src.id} ${src.title || ''} ${(src.tags || []).join(' ')}`.toLowerCase()
          const hit = [...needles].some((n) => n.length >= 2 && blob.includes(n))
          if (!takeAll && !hit) continue
        }
        const k = src.url || src.id
        if (seen.has(k)) continue
        seen.add(k)
        out.push(src)
      }
    }
    return out
  }

  const FAMILY_ACCENT = {
    'pre-paper': '#c4a35a',
    'pre-domino': '#8a8174',
    'ningbo-trunk': '#2f6f5e',
    'yue-fan': '#3d8b57',
    'macau-casino': '#8b6914',
    seasia: '#5a8f6a',
    'riichi-circle': '#b45a4a',
    'sixteen-tile': '#3a8a90',
    'jiangnan-caishen': '#1f8a6a',
    'huama-hua': '#7a4f8a',
    qiaoma: '#b45a6a',
    'chuanyu-108': '#c45c3a',
    duankagen: '#6b4f3a',
    'jing-jin': '#5a7a8a',
    'xiang-258': '#ec4899',
    'changde-hz': '#c45a2a',
    'dongbei-family': '#4a6670',
    kawuxing: '#a0673a',
    'laizi-belt': '#a35a3a',
    'north-west': '#6b5744',
    'american-card': '#4a5f9e',
    'mcr-family': '#3b82f6',
    constructed: '#a16207',
    'min-jin': '#2e8b8b',
    'jiangsu-other': '#8a6a3a',
    'anhui-baba': '#6a8a3a',
    'jiangxi-jing': '#a37a3a',
    'hebei-hun': '#5a6a8a',
    'guangxi-guiliu': '#3a9a5a',
    'henan-hun': '#8a5a5a',
    'shandong-258': '#d97706',
  }

  function catalogFamilies() {
    const playable = new Set()
    for (const r of catalog.value) {
      if (isPlayable(r.library_key || r.slug)) playable.add(r.slug)
    }
    const seenGlobal = new Set()
    return (areal.value?.families || [])
      .map((fam) => {
        const tags = []
        const seen = new Set()
        for (const m of fam.members || []) {
          const row = catalogRow(m.id)
          if (!row || row.fold_into || playable.has(row.slug) || seen.has(row.slug)) continue
          seen.add(row.slug)
          seenGlobal.add(row.slug)
          tags.push({
            key: row.slug,
            label: row.name_zh,
            short: (row.names && row.names[0]) || '',
            accent: FAMILY_ACCENT[fam.id] || '#1f6b52',
            note: m.note || row.blurb || '',
          })
        }
        return {
          id: fam.id,
          label: fam.label,
          region: fam.region,
          relatedness: fam.relatedness,
          accent: FAMILY_ACCENT[fam.id] || '#1f6b52',
          tags,
        }
      })
      .filter((f) => f.tags.length)
      .concat(
        (() => {
          const leftover = catalog.value.filter(
            (r) =>
              !r.fold_into &&
              !playable.has(r.slug) &&
              !seenGlobal.has(r.slug) &&
              !isArchiveOnly(r.slug),
          )
          if (!leftover.length) return []
          return [
            {
              id: 'ungrouped',
              label: '其他',
              region: '',
              relatedness: '',
              accent: '#1f6b52',
              tags: leftover.map((row) => ({
                key: row.slug,
                label: row.name_zh,
                short: (row.names && row.names[0]) || '',
                accent: '#1f6b52',
                note: row.blurb || '',
              })),
            },
          ]
        })(),
      )
  }

  function isPlayable(key) {
    return !!libraryEntry(key)?.categories?.includes('platform')
  }

  function isArchiveOnly(slug) {
    return (
      slug === 'mahjong-phylogeny' ||
      slug === 'mahjong-studies' ||
      slug === 'ningbo-classical' ||
      slug === 'chuanyu' ||
      slug === 'yuegang' ||
      slug === 'shiliuzhang' ||
      slug === 'jiangnan-caishen' ||
      slug === 'xiang' ||
      slug === 'dongbei-sanchen' ||
      slug === 'hongzhong-laizi' ||
      slug === 'lizhi' ||
      slug === 'nanyang' ||
      slug === 'american-mj' ||
      slug === 'jingjin' ||
      slug === 'jinshan' ||
      slug === 'qian' ||
      slug === 'qiaoma' ||
      slug === 'suhu-huama' ||
      slug === 'huazhong'
    )
  }

  return {
    catalog,
    phy,
    areal,
    archives,
    loaded,
    loadError,
    load,
    catalogRow,
    libraryEntry,
    pageKey,
    ruleHref,
    ruleName,
    parentsOf,
    childrenOf,
    familiesOf,
    eraOf,
    appearedOf,
    sourcesFor,
    isPlayable,
    isArchiveOnly,
    catalogFamilies,
  }
}
