export const NEW_CHINESE_DESCRIPTION = '新中式麻将是融合了立直麻将，国标麻将和东北麻将的宝牌衍生出的一种玩法，取精华去糟粕，已有十年以上的实战验证基础，兼具竞技性与娱乐性，牌型难度和对应的番数做过大量优化使其合理，欢迎加入，微信15513635592'

export const newChineseRulebooks = [
  { title: '新中式麻将规则说明书', url: '/rulebooks/新中式麻将规则说明书.html', filename: '新中式麻将规则说明书.html' },
  { title: '新中式麻将规则说明书原件', url: '/rulebooks/新中式麻将规则说明书.docx', filename: '新中式麻将规则说明书.docx' },
]

export const SANHUA_DESCRIPTION = '散花天才麻将是一群热血少年的原创四人麻将变体，使用136 张无花标准麻将牌，融合皮子‑癞子‑红中特殊杠体系、配对差异化算分、报清机制、多重牌型，是一套规则密度很高、策略博弈极强的自定义麻将玩法。'
export const customRulebooks = [
  { subRule: 'new_chinese/standard', label: '新中式麻将', description: NEW_CHINESE_DESCRIPTION, docs: newChineseRulebooks },
  { subRule: 'new_chinese/sanhua', label: '散花天才麻将', description: SANHUA_DESCRIPTION, docs: [
    { title: '散花天才麻将规则说明书', url: '/rulebooks/sanhua/rules.html', filename: '散花天才麻将规则说明书.html' },
    { title: '散花天才麻将规则说明书（含补充）', url: '/rulebooks/sanhua/散花天才麻将规则说明书.docx', filename: '散花天才麻将规则说明书.docx' },
  ] },
]
