import { tziakchaToSalasasa, salasasaToTziakcha } from './tziakchaGuobiao.js'
import { salasasaToBotzone, botzoneToSalasasa } from './botzoneGuobiao.js'
import { salasasaToMjaiRecord, mjaiToSalasasaRecord } from './mjaiRiichi.js'
import { prettyJson } from './tiles.js'

/**
 * approxRows: { field, status, how, example }[]
 * status: '完整' | '近似' | '缺失' | '格式限制'
 */
export const CONVERT_MODES = [
  {
    id: 'tz2sala',
    label: '雀渣 → salasasa（国标）',
    group: 'guobiao',
    accept: '.json,application/json,text/plain',
    hint: '支持雀渣链接、牌谱 ID，以及 script、step 或 session+records JSON。',
    approxRows: [
      {
        field: '手牌 / 牌山 / 摸切吃碰杠和',
        status: '完整',
        how: '按雀渣脚本位域解码。',
        example: '摸 1 万 → ["d", 11]；切 3 饼 → ["c", 23, "F"]'
      },
      {
        field: '番种 y → hu_fan',
        status: '完整',
        how: '按网页 FAN[] 表还原番种名称。',
        example: '索引 51 → "混一色"；花牌×2 → "花牌*2"'
      },
      {
        field: 'p0_uid … p3_uid',
        status: '近似',
        how: '保留原始用户名和 ID，并标记为外部玩家。数字 UID 仅用于回放。',
        example: '"pm6Eet01" → 955674184'
      }
    ],
    convert: async (text) => prettyJson(await tziakchaToSalasasa(text)),
    filename: 'salasasa-guobiao.json'
  },
  {
    id: 'sala2tz',
    label: 'salasasa → 雀渣（国标）',
    group: 'guobiao',
    accept: '.json,application/json',
    hint: '输入 salasasa 单局 JSON，输出雀渣 step 数据。',
    approxRows: [
      {
        field: 'step.w（牌墙 hex）',
        status: '近似',
        how: '由初始手牌和 tiles_list 组成 144 张牌墙，不还原真实洗牌顺序。',
        example: '导出牌墙从下标 0 开始。'
      },
      {
        field: 'step.d（骰子）',
        status: '近似',
        how: '骰子固定为 0x1111。',
        example: '真实骰子不保留。'
      },
      {
        field: '每张牌的实例号（0~143 里同点数的第几张）',
        status: '近似',
        how: '按出现顺序分配实例号，超过四张时循环使用。',
        example: '实例号按导出顺序生成，与原谱不同。'
      },
      {
        field: '摸牌动作里的座位（谁摸的）',
        status: '近似',
        how: '摸牌没有座位，按后续切牌或鸣牌推断。',
        example: '复杂鸣牌时座位不准。'
      },
      {
        field: '切牌动作里的座位',
        status: '近似',
        how: '切牌没有座位，按鸣牌记录和出牌顺序推断。',
        example: '没有鸣牌记录时按出牌顺序推断。'
      },
      {
        field: 'step.a 时间戳',
        status: '近似',
        how: '时间戳按每步 1000 ms 递增。',
        example: '不含真实思考时间。'
      }
    ],
    convert: async (text) => prettyJson(await salasasaToTziakcha(text, { compress: false })),
    filename: 'tziakcha-guobiao.json'
  },
  {
    id: 'sala2bz',
    label: 'salasasa → Botzone（国标）',
    group: 'guobiao',
    accept: '.json,application/json',
    hint: '输入 salasasa 单局 JSON，输出 Botzone lines 与 text。',
    approxRows: [
      {
        field: '他人摸牌',
        status: '格式限制',
        how: 'Botzone 只记录其他玩家摸牌动作，不记录牌面。',
        example: '"3 2 DRAW"（无牌面）'
      },
      {
        field: '发牌行（1 …）',
        status: '近似',
        how: '发牌行写入 p0 手牌，包含多座信息。',
        example: '不适合直接作为单座输入。'
      },
      {
        field: '吃/碰后的打出',
        status: '完整',
        how: '吃/碰后出牌合并为一条 CHI/PENG 记录。',
        example: '["p", 41, 3, 41, 41] + ["c", 18, "F"] → "3 3 PENG W8"'
      }
    ],
    convert: async (text) => prettyJson(salasasaToBotzone(text)),
    filename: 'botzone-guobiao.json'
  },
  {
    id: 'bz2sala',
    label: 'Botzone → salasasa（国标）',
    group: 'guobiao',
    accept: '.json,.txt,text/plain,application/json',
    hint: '支持 Botzone 协议文本，或包含 games、lines、text 字段的 JSON。',
    approxRows: [
      {
        field: 'p1_tiles / p2_tiles / p3_tiles（他人手牌）',
        status: '缺失',
        how: '协议不含其他玩家起手牌，p1~p3 为空数组。',
        example: '"p1_tiles": []'
      },
      {
        field: 'tiles_list（剩余牌山）',
        status: '缺失',
        how: '协议不含牌山，tiles_list 为空。',
        example: '"tiles_list": []'
      },
      {
        field: '他人摸牌 ["d", tile]',
        status: '缺失',
        how: '其他玩家摸牌无牌面，忽略该动作。',
        example: '"3 2 DRAW" → 无对应摸牌记录'
      },
      {
        field: '吃的左右中 ["cl"|"cm"|"cr", …, h1, h2]',
        status: '近似',
        how: '只提供顺子中张，另外两张按 ±1 补齐；赤宝信息不保留。',
        example: '吃牌两侧按中张 ±1 生成。'
      },
      {
        field: '碰/明杠的手牌真实 id',
        status: '近似',
        how: '鸣牌手牌没有实例号，按牌面重复填充。',
        example: '"3 2 PENG B5" → ["p", 25, 2, 25, 25]'
      },
      {
        field: '暗杠 ["ag", …]',
        status: '近似',
        how: 'GANG 不含杠牌面，使用占位牌。',
        example: '杠牌使用固定占位值。'
      },
      {
        field: '和牌番种 / 分数',
        status: '缺失/近似',
        how: 'HU 通常不含番种；无 # fans 时，hu_fan 为空、score 为 0。',
        example: '"3 0 HU" → hu_fan=[]、score=0'
      }
    ],
    convert: async (text) => prettyJson(botzoneToSalasasa(text)),
    filename: 'salasasa-from-botzone.json'
  },
  {
    id: 'sala2mjai',
    label: 'salasasa → MJAI（日麻）',
    group: 'riichi',
    accept: '.json,application/json',
    hint: '输入 salasasa 日麻单局 JSON，输出 MJAI events 与 ndjson。',
    approxRows: [
      {
        field: 'hora.pai（和了哪张）',
        status: '近似',
        how: 'salasasa 未单独记录和了牌，hora.pai 写为 "?"。',
        example: 'hora.pai = "?"'
      },
      {
        field: '摸牌/切牌的 actor',
        status: '近似',
        how: '摸牌和切牌无座位，按出牌顺序推断。',
        example: '复杂鸣牌时座位不准。'
      },
      {
        field: '普通动作（立直、宝牌、吃碰杠、流局分差）',
        status: '完整',
        how: '按对应字段映射。',
        example: '["riichi", 2, 0] → {"type":"reach","actor":2}'
      }
    ],
    convert: async (text) => prettyJson(salasasaToMjaiRecord(text)),
    filename: 'mjai.mjson.json'
  },
  {
    id: 'mjai2sala',
    label: 'MJAI → salasasa（日麻）',
    group: 'riichi',
    accept: '.json,.mjson,.txt,application/json,text/plain',
    hint: '支持 MJAI .mjson/NDJSON，或包含事件数组的 JSON。',
    approxRows: [
      {
        field: '隐藏牌（?）',
        status: '缺失',
        how: '未知摸牌不写入 action_ticks；未知起手牌保留为空。',
        example: '{"type":"tsumo","pai":"?"} → 不生成摸牌 tick'
      },
      {
        field: 'tiles_list（剩余牌山）',
        status: '缺失',
        how: 'MJAI 对局流通常不含牌山，tiles_list 为空。',
        example: '"tiles_list": []'
      },
      {
        field: '庄家第 14 张（开局多摸）',
        status: '近似',
        how: '庄家先手多摸牌时写入首个摸牌 tick。',
        example: '开局 13 张 + tsumo → 首个 d tick'
      },
      {
        field: '和牌张 / 和牌细节',
        status: '近似',
        how: 'hora.pai 为 "?" 时不记录和了牌；番符写入 hu_riichi。',
        example: 'hora.pai = "?" → 无和了牌 ID'
      },
      {
        field: '吃的 cl/cm/cr',
        status: '近似',
        how: '按副露牌位置判断 cl/cm/cr。',
        example: '叫 3m、consumed [2m,4m] → ["cm", 13, …]'
      }
    ],
    convert: async (text) => prettyJson(mjaiToSalasasaRecord(text)),
    filename: 'salasasa-riichi.json'
  }
]

export function getMode(id) {
  return CONVERT_MODES.find((m) => m.id === id)
}

export {
  tziakchaToSalasasa,
  salasasaToTziakcha,
  salasasaToBotzone,
  botzoneToSalasasa,
  salasasaToMjaiRecord,
  mjaiToSalasasaRecord
}
