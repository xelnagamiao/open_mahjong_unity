import { locale, tr } from './index'

// English terms match Assets/Scripts/Config/FanTextDictionary.cs in the Unity client.
const english = {
  大四喜: 'Big Four Winds', 大三元: 'Big Three Dragons', 绿一色: 'All Green', 九莲宝灯: 'Nine Gates',
  四杠: 'Four Kongs', 连七对: 'Seven Shifted Pairs', 十三幺: 'Thirteen Orphans',
  清幺九: 'All Terminals', 小四喜: 'Little Four Winds', 小三元: 'Little Three Dragons',
  字一色: 'All Honors', 四暗刻: 'Four Concealed Pungs', 一色双龙会: 'Pure Terminal Chows',
  一色四同顺: 'Quadruple Chow', 一色四节高: 'Four Pure Shifted Pungs',
  一色四步高: 'Four Pure Shifted Chows', 三杠: 'Three Kongs', 混幺九: 'All Terminals and Honors',
  七对: 'Seven Pairs', 七星不靠: 'Greater Honors and Knitted Tiles', 全双刻: 'All Even Pungs',
  清一色: 'Full Flush', 一色三同顺: 'Pure Triple Chow', 一色三节高: 'Pure Shifted Pungs',
  全大: 'Upper Tiles', 全中: 'Middle Tiles', 全小: 'Lower Tiles', 清龙: 'Pure Straight',
  三色双龙会: 'Three-Suited Terminal Chows', 一色三步高: 'Pure Shifted Chows', 全带五: 'All Fives',
  三同刻: 'Triple Pung', 三暗刻: 'Three Concealed Pungs',
  全不靠: 'Lesser Honors and Knitted Tiles', 组合龙: 'Knitted Straight', 大于五: 'Upper Five',
  小于五: 'Lower Five', 三风刻: 'Big Three Winds', 花龙: 'Mixed Straight',
  推不倒: 'Reversible Tiles', 三色三同顺: 'Mixed Triple Chow', 三色三节高: 'Mixed Shifted Pungs',
  无番和: 'Chicken Hand', 妙手回春: 'Last Tile Draw', 海底捞月: 'Last Tile Claim',
  杠上开花: 'Out with Replacement Tile', 抢杠和: 'Robbing The Kong', 碰碰和: 'All Pungs',
  混一色: 'Half Flush', 三色三步高: 'Mixed Shifted Chows', 五门齐: 'All Types',
  全求人: 'Melded Hand', 双暗杠: 'Two Concealed Kongs', 双箭刻: 'Two Dragon Pungs',
  全带幺: 'Outside Hand', 不求人: 'Fully Concealed Hand', 双明杠: 'Two Melded Kongs',
  和绝张: 'Last Tile', 箭刻: 'Dragon Pung', 圈风刻: 'Prevalent Wind', 门风刻: 'Seat Wind',
  门前清: 'Concealed Hand', 平和: 'All Chows', 双暗刻: 'Two Concealed Pungs',
  暗杠: 'Concealed Kong', 断幺: 'All Simples', 老少副: 'Two Terminal Chows',
  明杠: 'Melded Kong', 缺一门: 'One Voided Suit', 无字: 'No Honors', 边张: 'Edge Wait',
  嵌张: 'Closed Wait', 单钓将: 'Single Waiting', 自摸: 'Self-Drawn',
  明暗杠: 'Mixed Exposed-Concealed Kong', 错和: 'Wrong Win',
  四归一: 'Tile Hog', 双同刻: 'Double Pung', 一般高: 'Pure Double Chow',
  喜相逢: 'Mixed Double Chow', 幺九刻: 'Pung of Terminals or Honors', 连六: 'Short Straight',
  花牌: 'Flower Tiles',
}

const japanese = {
  大四喜: '大四喜', 大三元: '大三元', 绿一色: '緑一色', 九莲宝灯: '九蓮宝燈', 四杠: '四槓',
  连七对: '連七対', 十三幺: '十三幺', 清幺九: '清幺九', 小四喜: '小四喜', 小三元: '小三元',
  字一色: '字一色', 四暗刻: '四暗刻', 一色双龙会: '一色双龍会', 一色四同顺: '一色四同順',
  一色四节高: '一色四節高', 一色四步高: '一色四歩高', 三杠: '三槓', 混幺九: '混幺九',
  七对: '七対', 七星不靠: '七星不靠', 全双刻: '全双刻', 清一色: '清一色',
  一色三同顺: '一色三同順', 一色三节高: '一色三節高', 全大: '全大', 全中: '全中', 全小: '全小',
  清龙: '清龍', 三色双龙会: '三色双龍会', 一色三步高: '一色三歩高', 全带五: '全帯五',
  三同刻: '三同刻', 三暗刻: '三暗刻', 全不靠: '全不靠', 组合龙: '組合龍',
  大于五: '大于五', 小于五: '小于五', 三风刻: '三風刻', 花龙: '花龍', 推不倒: '推不倒',
  三色三同顺: '三色三同順', 三色三节高: '三色三節高', 无番和: '無番和',
  妙手回春: '妙手回春', 海底捞月: '海底撈月', 杠上开花: '槓上開花', 抢杠和: '搶槓和',
  碰碰和: '碰碰和', 混一色: '混一色', 三色三步高: '三色三歩高', 五门齐: '五門斉',
  全求人: '全求人', 双暗杠: '双暗槓', 双箭刻: '双箭刻', 全带幺: '全帯幺',
  不求人: '不求人', 双明杠: '双明槓', 和绝张: '和絶張', 箭刻: '箭刻', 圈风刻: '圏風刻',
  门风刻: '門風刻', 门前清: '門前清', 平和: '平和', 双暗刻: '双暗刻', 暗杠: '暗槓',
  断幺: '断幺', 老少副: '老少副', 明杠: '明槓', 缺一门: '欠一門', 无字: '無字',
  边张: '辺張', 嵌张: '嵌張', 单钓将: '単釣将', 自摸: '自摸', 明暗杠: '明暗槓',
  错和: '錯和', 四归一: '四帰一', 双同刻: '双同刻', 一般高: '一般高',
  喜相逢: '喜相逢', 幺九刻: '幺九刻', 连六: '連六', 花牌: '花牌',
}

export function translateFanName(value, targetLocale = locale.value) {
  const source = String(value || '')
  if (!source || targetLocale === 'zh-CN') return source
  const match = source.match(/^(.*?)(\*\d+)$/)
  const base = match ? match[1] : source
  const suffix = match ? match[2] : ''
  if (targetLocale === 'en') return `${english[base] || base}${suffix}`
  if (targetLocale === 'ja') return `${japanese[base] || base}${suffix}`
  return tr(source, {}, targetLocale)
}

export function formatFanCount(value, targetLocale = locale.value) {
  const amount = String(value ?? 0)
  return targetLocale === 'en' ? `${amount} Fan` : `${amount}${tr('番', {}, targetLocale)}`
}
