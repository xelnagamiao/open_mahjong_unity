export const HUAHUN_SUB_RULE = 'guobiao/huahun'
export const HUAHUN_JOKERS = [51, 52, 53, 54]
export const HUAHUN_DESCRIPTION = '国标麻将花混规则是部分国标麻将资深玩家研发的娱乐玩法。该娱乐规则将麻将花牌中“春、夏、秋、冬”四朵红花取出用作赖子（花混），并相应提高了有赖子手牌的和番限制以及无赖子和牌时的分数奖励。该玩法进一步加快了牌局行进速度，突出了“抢和”理念，丰富了手牌策略变化，可以作为国标麻将竞技规则以外的一种娱乐补充。'
export const HUAHUN_RULE_TEXT = [
  '①起和限制，0赖子和1赖子8番起和，2赖子16番起和，3赖子32番起和，4赖子64番起和。',
  '②无赖子和牌时结算时番数额外加八番或者直接番数翻倍（默认额外加八番）',
  '③赖子只能在摸到手的时候摸切，不准手切，他家点出的癞子无法点和和牌（起手赖子不得打出）',
  '④手中摸到四个赖子可以花杠，和牌时剩下手牌必须符合基本和牌型，花杠算88番。',
  '⑤（四朵红花用作赖子的情况下）补的黑花在结算时一朵算4番，两朵8番，三朵16番，四朵32番',
  '⑥四朵红花是春夏秋冬，四朵黑花是梅兰竹菊，自摸/点和癞子不算绝张，癞子能构成独听条件时构成独听（设置默认关闭癞子多面听）',
]
export function huahunFanPoints(name) {
  const fixed = { '花杠': 88, '无癞子加番': 8, '黑花1朵': 4, '黑花2朵': 8, '黑花3朵': 16, '黑花4朵': 32 }
  if (Object.hasOwn(fixed, name)) return fixed[name]
  const doubled = /^无癞子翻倍\*(\d+)$/.exec(name)
  return doubled ? Number(doubled[1]) : null
}
