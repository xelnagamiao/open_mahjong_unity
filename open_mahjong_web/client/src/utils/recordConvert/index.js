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
    hint: '粘贴雀渣 raw（含 script）/ 解码 step / session+records JSON。',
    reliability: '完整（推荐）',
    approxRows: [
      {
        field: '手牌 / 牌山 / 摸切吃碰杠和',
        status: '完整',
        how: '按雀渣网页脚本位域原样解码，不猜测。',
        example: '摸 1 万 → ["d", 11]；手切 3 饼 → ["c", 23, "F"]'
      },
      {
        field: '番种 y → hu_fan',
        status: '完整',
        how: '用网页 FAN[] 表把番种编号还原成中文名。',
        example: '索引 51 → "混一色"；花牌×2 → "花牌*2"'
      },
      {
        field: 'p0_uid … p3_uid',
        status: '近似',
        how: '雀渣玩家 id 是短字符串，salasasa 要整数，用字符串稳定哈希生成占位 uid。',
        example: '雀渣 "pm6Eet01" → p0_uid: 955674184（不是真实 salasasa 账号）'
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
    hint: '输出解码态 records（step），勿当作雀渣官网下载的原谱。',
    reliability: '近似（勿当原谱）',
    approxRows: [
      {
        field: 'step.w（牌墙 hex）',
        status: '近似',
        how: '用本局 p0~p3 初始手牌 + tiles_list 拼成 144 张再转 hex；不是雀渣原始洗牌墙，也不还原真实骰子切牌起点。',
        example: '原谱可能从第 47 张起摸；导出墙从下标 0 起排，顺序会不同'
      },
      {
        field: 'step.d（骰子）',
        status: '近似',
        how: '固定写成占位值 0x1111（四个 1），不还原真实骰子。',
        example: '真实可能是 (3,5,2,4)；导出恒为 (1,1,1,1)'
      },
      {
        field: '每张牌的实例号（0~143 里同点数的第几张）',
        status: '近似',
        how: 'salasasa 同点数牌都是同一个数字（如四张 15）。导出时按出现顺序分配 0,1,2,3 号实例；超过 4 张则循环复用。',
        example: '四张五万一律是 15 → 导出可能是 16,17,18,19；与原雀渣实例号对不上'
      },
      {
        field: '摸牌动作里的座位（谁摸的）',
        status: '近似',
        how: 'salasasa 的 ["d", tile] 不写座位。导出时向后看下一手切牌/鸣牌，猜「摸牌者 = 随后切牌的人」。',
        example: '["d", 26] 后面是东家切牌 → 猜东家摸；若中间有吃碰再切，可能猜错'
      },
      {
        field: '切牌动作里的座位',
        status: '近似',
        how: '["c", tile, "T|F"] 也不写座位。向前找最近一次吃/碰/杠的座位，或「上一手切牌者的下家」。',
        example: '南家碰后 ["c", 18, "F"] → 猜南家切；没有鸣牌记录时按上家+1 推'
      },
      {
        field: 'step.a 时间戳',
        status: '近似',
        how: '每步 +1000ms 占位，不是真实思考时间。',
        example: '原谱 823ms；导出 1000, 2000, 3000…'
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
    hint: '输出 Botzone 风格 lines + text。',
    reliability: '基本完整（受 Botzone 协议限制）',
    approxRows: [
      {
        field: '他人摸牌',
        status: '格式限制',
        how: 'Botzone 规定其他人摸牌只能写「3 seat DRAW」，不能写摸到哪张。源谱里的牌面会丢掉。',
        example: 'salasasa ["d", 26]（别人摸）→ "3 2 DRAW"（没有 26）'
      },
      {
        field: '发牌行（1 …）',
        status: '近似',
        how: 'Botzone 每人只看见自己的 13 张。导出时把 p0 手牌写进发牌行，并注明「上帝视角合并」；不是标准单座视角。',
        example: '"1 0 0 0 0 W1 W2 …" 实际混有多座信息，给 Botzone 机器人直接喂可能不对'
      },
      {
        field: '吃/碰后的打出',
        status: '完整',
        how: '若下一 tick 是切牌，合并成 Botzone 一条 CHI/PENG + 打出牌。',
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
    hint: 'Botzone 协议文本或 {games/lines/text} JSON。',
    reliability: '近似（隐藏信息很多）',
    approxRows: [
      {
        field: 'p1_tiles / p2_tiles / p3_tiles（他人手牌）',
        status: '缺失',
        how: '协议不告诉你别人开局拿了什么。通常只填 p0（或公开的那一手），其余为空数组 []。',
        example: '输入只有自己 13 张 → 输出 "p1_tiles": []'
      },
      {
        field: 'tiles_list（剩余牌山）',
        status: '缺失',
        how: '协议没有整副牌山，输出恒为 []。',
        example: '"tiles_list": []'
      },
      {
        field: '他人摸牌 ["d", tile]',
        status: '缺失',
        how: '别人摸牌只有 "3 x DRAW"，没有牌面，转换时直接跳过，不生成 d tick。',
        example: '"3 2 DRAW" → （无对应 salasasa 摸牌记录）'
      },
      {
        field: '吃的左右中 ["cl"|"cm"|"cr", …, h1, h2]',
        status: '近似',
        how: 'Botzone 只给「顺子中间那张 + 打出」。手牌里另外两张用「中间牌 ±1」推算；赤宝等细节没有。',
        example: '"3 1 CHI T2 W3"（吃后打出）→ 猜手牌为 1条+3条，可能与真实手牌（含赤）不符'
      },
      {
        field: '碰/明杠的手牌真实 id',
        status: '近似',
        how: '协议不给出你手里那两/三张的具体实例，用被鸣牌点数重复填充。',
        example: '"3 2 PENG B5" → ["p", 25, 2, 25, 25]（两张手牌都写成 25）'
      },
      {
        field: '暗杠 ["ag", …]',
        status: '近似',
        how: 'Botzone "GANG" 在上一手是自己摸牌时表示暗杠，但不写杠的哪门牌；导出常用占位。',
        example: '"3 0 GANG" → 可能写成 ["g", 11, 0, 11, 11, 11] 之类占位'
      },
      {
        field: '和牌番种 / 分数',
        status: '缺失/近似',
        how: '标准对局流里 HU 往往不带番种列表。若原文没有 "# fans …" 注释行，则 hu_fan=[]、score 为 0。',
        example: '"3 0 HU" → ["hu_self", 0, 8, [], [0,0,0,0]]'
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
    hint: '输出 events 与 ndjson。',
    reliability: '基本完整',
    approxRows: [
      {
        field: 'hora.pai（和了哪张）',
        status: '近似',
        how: 'salasasa 的 hu_riichi tick 不单独存「和了牌」字段。导出时常写成 "?"。',
        example: '["hu_riichi", 1, "hu_first", …] → {"type":"hora","pai":"?"}'
      },
      {
        field: '摸牌/切牌的 actor',
        status: '近似',
        how: '["d"] / ["c"] 无座位时，用「上一手弃牌者的下家」或「随后切牌者」推断。一般能对，复杂鸣牌链偶发偏差。',
        example: '东切后 ["d", 26] → 猜南家 tsumo'
      },
      {
        field: '普通动作（立直、宝牌、吃碰杠、流局分差）',
        status: '完整',
        how: '有明确字段则直接映射。',
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
    hint: 'MJAI NDJSON（每行一个事件）或 events 数组 JSON。',
    reliability: '近似（看源事件是否齐全）',
    approxRows: [
      {
        field: 'tiles_list（剩余牌山）',
        status: '缺失',
        how: '标准 MJAI 对局流通常不给整座牌山，输出恒为 []。',
        example: '"tiles_list": []'
      },
      {
        field: '庄家第 14 张（开局多摸）',
        status: '近似',
        how: 'start_kyoku.tehais 一般是 13 张。若紧跟着庄家 tsumo，会把那张补进 p{oya}_tiles。',
        example: 'tehais[0] 13 张 + {"tsumo","actor":0,"pai":"5m"} → p0_tiles 变成 14 张含 15'
      },
      {
        field: '和牌张 / 和牌细节',
        status: '近似',
        how: 'hora.pai 若是 "?"，salasasa 无法写入具体和牌张（tick 里也没有单独字段）。番符有则写入 hu_riichi。',
        example: '{"hora","pai":"?"} → ["hu_riichi", …]（无单独和牌 id）'
      },
      {
        field: '吃的 cl/cm/cr',
        status: '近似',
        how: '用「叫牌相对顺子中间张的位置」判断左/中/右吃。',
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
