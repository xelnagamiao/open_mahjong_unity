export const HONGQUE_W_SUB_RULE = 'hongque/w-v1.0'
export const HONGQUE_W_DESCRIPTION = '虹雀W第1.0版使用7种花色、9种数字各2张的虹雀牌，最先将手牌全部组成牌组的玩家赢得一局。彩虹顺子的存在给游戏带来了很多变数，是入门虹雀的最佳选择。'
export const HONGQUE_2_DESCRIPTION = '虹雀是由Null设计的一款以彩虹为主题的拉密类桌游，使用十四种花色、九种数字各一张的麻将牌，最先将手牌全部组成顺子或刻子的玩家赢得一局。牌组的种类千变万化，各种起手都存在无限的可能。游戏尚在测试阶段，如对本规则感兴趣或有任何建议都可以添加虹雀官方Q群389832188一同交流。'

export const hongqueRulebooks = [
  {
    subRule: 'hongque/v1.6', label: '虹雀² v1.7', title: '虹雀² v1.7 规则书',
    description: HONGQUE_2_DESCRIPTION,
    desc: '虹雀² v1.7 规则书，包含基本规则、番种说明及牌例。',
    url: '/rulebooks/hongque-v1.7.pdf', filename: '虹雀² v1.7.pdf',
  },
  {
    subRule: HONGQUE_W_SUB_RULE, label: '虹雀 W v1.0', title: '虹雀 W v1.0 规则书',
    description: HONGQUE_W_DESCRIPTION, desc: '',
    url: '/rulebooks/hongque-w-v1.0.pdf', filename: '虹雀W v1.0.pdf',
  },
]

export function hongqueRulebook(subRule) {
  return hongqueRulebooks.find(book => book.subRule === subRule) ?? hongqueRulebooks[0]
}
