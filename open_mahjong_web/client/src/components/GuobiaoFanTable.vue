<template>
  <div class="guobiao-fan-table">
    <button
      type="button"
      class="scene-appearance-toggle__button guobiao-fan-table__button"
      :aria-expanded="open"
      @click="open = !open"
    >
      {{ open ? '关闭国标番数表' : '国标番数表' }}
    </button>

    <section v-if="open" class="guobiao-fan-table__panel" aria-label="国标番数表" @click.stop>
      <header class="guobiao-fan-table__header">
        <div>
          <strong>国标番数表</strong>
          <span>新编 MCR 番种与牌例</span>
        </div>
        <button type="button" class="guobiao-fan-table__close" aria-label="关闭国标番数表" @click="open = false">×</button>
      </header>

      <div class="guobiao-fan-table__groups">
        <article v-for="group in groups" :key="group.fan" class="guobiao-fan-table__group">
          <button
            type="button"
            class="guobiao-fan-table__group-header"
            :aria-expanded="expanded.has(group.fan)"
            @click="toggle(group.fan)"
          >
            <span>{{ group.fan }}番</span>
            <span class="guobiao-fan-table__chevron">{{ expanded.has(group.fan) ? '−' : '+' }}</span>
          </button>
          <div v-if="expanded.has(group.fan)" class="guobiao-fan-table__entries">
            <div v-for="item in group.items" :key="item.id" class="guobiao-fan-table__entry">
              <div class="guobiao-fan-table__name">
                <strong>{{ item.names[0] }}</strong>
                <span>{{ group.fan }}番</span>
              </div>
              <p>{{ item.description }}</p>
              <p class="guobiao-fan-table__example"><span>牌例：</span>{{ item.example }}</p>
            </div>
          </div>
        </article>
      </div>
    </section>
  </div>
</template>

<script setup>
import { computed, ref } from 'vue'
import { GUOBIAO } from '@/constants/guessFanCatalog'

const open = ref(false)
const expanded = ref(new Set())

const examples = {
  dasixi: '东、南、西、北分别组成四组刻子或杠子，另有一对将。',
  dasanyuan: '中、发、白三组箭牌刻子或杠子，另有一组面子和将。',
  lvyise: '仅使用 23468 条与发牌，例如 234678 条刻顺组合。',
  jiulianbaodeng: '同一门 1112345678999 加该门任一张牌。',
  sigang: '四组杠子，剩余一对将牌组成和牌。',
  lianqidui: '同一门连续七个对子，例如 11223344556677 万。',
  shisanyao: '一、九万饼条与东南西北中发白各一张，再成一对。',
  qingyaojiu: '只由万、饼、条的一九牌组成刻子、杠子和将牌。',
  xiaosixi: '东南西北中三组刻子或杠子，第四组为对子。',
  xiaosanyuan: '中、发、白中两组刻子，一组对子，另有两组面子。',
  ziyise: '手牌全部由东南西北中发白组成。',
  sianke: '四组暗刻或暗杠，另有一对将牌。',
  yiseshuanglonghui: '一门 112233、556677、99 的七对结构。',
  yisesitongshun: '同一门四组完全相同的顺子，例如四组 123 万。',
  yisesijiegao: '同一门四组连续递增的刻子，例如 111、222、333、444。',
  yisesibugao: '同一门四组连续递进的顺子。',
  sangang: '三组杠子，另有一组面子和将牌。',
  hunyaojiu: '万、饼、条的一九刻子与字牌刻子混合组成全副牌。',
  qiduizi: '七个不同对子，例如 11223344556677。',
  qixingbukao: '七张字牌齐全，另有三门各三张不相连序数牌。',
  quanshuangke: '全部由 2、4、6、8 序数牌组成的刻子和将牌。',
  qingyise: '整副牌只使用万、饼或条中的一门。',
  yisesantongshun: '同一门三组完全相同的顺子。',
  yisesanjiegao: '同一门三组连续递增的刻子。',
  quanda: '所有序数牌均为 7、8、9。',
  quanzhong: '所有序数牌均为 4、5、6。',
  quanxiao: '所有序数牌均为 1、2、3。',
  qinglong: '同一门 123、456、789 三组顺子。',
  sanseshuanglonghui: '三门各有 112233、556677、99 的双龙七对结构。',
  yisesanbugao: '同一门三组按相同步长递进的顺子。',
  quandaiwu: '每组面子和将牌都含有 5，例如 123、456、789。',
  santongke: '三门各有同一序数的刻子，例如 555 万、555 饼、555 条。',
  sananke: '三组暗刻或暗杠，另有一组面子和将牌。',
  quanbukao: '三门序数牌互不相连，且不含重复，配字牌组成特殊牌型。',
  zuhelong: '三门各取 147、258、369 之一组成组合龙。',
  dayuwu: '全部序数牌均为 6、7、8、9。',
  xiaoyuwu: '全部序数牌均为 1、2、3、4。',
  sanfengke: '东南西北中任取三组风牌刻子或杠子。',
  hualong: '三门各有一组相连的顺子，合起来形成 123、456、789。',
  tuibudao: '只使用牌面旋转后仍有对应关系的牌，如 123689 饼及白板。',
  sansesantongshun: '三门各有同一数字的顺子，例如 123 万、123 饼、123 条。',
  sansesanjiegao: '三门各有同一数字的刻子，例如 555 万、555 饼、555 条。',
  wufanhe: '满足和牌条件但全部番种被规则排除，计作无番和。',
  miaoshouhuichun: '在牌山最后一张自摸和牌。',
  haidilaoyue: '在牌山最后一张被他家打出后点和。',
  gangshangkaihua: '杠后从补牌处自摸和牌。',
  qiangganghe: '他家加杠时，和其正在加杠的那张牌。',
  pengpenghe: '四组刻子或杠子加一对将牌。',
  hunyise: '一门序数牌加任意字牌组成。',
  sansesanbugao: '三门顺子按相同步长依次递进。',
  wumenqi: '万、饼、条三门和东南西北中发白均有牌。',
  quanqiuren: '四组副露面子，最后一张牌单钓并点和。',
  shuangangang: '两组暗杠，另有两组面子和将牌。',
  shuangjianke: '两组箭牌刻子或杠子。',
  quandaiyao: '每组面子和将牌都含一、九或字牌。',
  buqiuren: '门前清状态下自摸和牌，且无副露。',
  shuangminggang: '两组明杠。',
  hejuezhang: '和牌张的第四张已经在手牌、碰牌或弃牌中可见。',
  jianke: '中、发、白任一组刻子或杠子。',
  quanfengke: '与圈风相同的风牌刻子或杠子。',
  menfengke: '与门风相同的风牌刻子或杠子。',
  menqianqing: '没有副露的门前清和牌。',
  pinghe: '四组顺子，序数牌作将，和牌不为边张、嵌张或单钓。',
  siguiyi: '同一张牌在四组面子或对子中出现，形成四归一。',
  shuangtongke: '两门各有同一序数的刻子。',
  shuanganke: '两组暗刻或暗杠。',
  angang: '一组暗杠。',
  duanyao: '全副牌不含一、九和字牌。',
  yibangao: '同一门两组完全相同的顺子。',
  xixiangfeng: '两门各有同一数字的顺子。',
  lianliu: '同一门出现相连的两组顺子，如 123 与 456。',
  laoshaofu: '同一门出现 123 与 789 两组顺子。',
  yaojiuke: '一、九或字牌的一组刻子或杠子。',
  minggang: '一组明杠。',
  queyimen: '万、饼、条三门中缺少其中一门。',
  wuzi: '整副牌没有东南西北中发白。',
  bianzhang: '以 1 等待 3，或以 9 等待 7 的边张和牌。',
  qianzhang: '以嵌张等待，例如 2、4 等 3。',
  dandiaojiang: '单钓将牌和牌。',
  zimo: '从牌山摸到和牌张自摸和牌。',
  huapai: '和牌时持有花牌并按规则计花。',
  mingangang: '一组明杠与一组暗杠。',
}

const groups = computed(() => {
  const grouped = new Map()
  for (const item of GUOBIAO) {
    const fan = Number(item.fan)
    if (!Number.isFinite(fan)) continue
    if (!grouped.has(fan)) grouped.set(fan, [])
    grouped.get(fan).push({
      ...item,
      description: `${item.names[0]}：${descriptionFor(item.names[0])}`,
      example: examples[item.id.split(':')[1]] || '按新编 MCR 牌型定义构成该番种。',
    })
  }
  return [...grouped.entries()]
    .sort(([left], [right]) => left - right)
    .map(([fan, items]) => ({ fan, items }))
})

function descriptionFor(name) {
  const descriptions = {
    大四喜: '四组风牌刻子或杠子。', 大三元: '三组箭牌刻子或杠子。', 绿一色: '只使用绿色牌张。',
    九莲宝灯: '同一门的九莲宝灯形。', 四杠: '四组杠子。', 连七对: '同一门连续七个对子。', 十三幺: '十三种幺九字牌齐全并成一对。',
    清幺九: '只由一、九牌组成。', 小四喜: '三组风牌刻子和一组风牌对子。', 小三元: '两组箭牌刻子和一组箭牌对子。', 字一色: '全部由字牌组成。',
    四暗刻: '四组暗刻或暗杠。', 清一色: '只使用一门序数牌。', 七对: '七个不同的对子。', 清龙: '同一门 123、456、789。',
    平和: '四组顺子加序数牌将。', 自摸: '自摸和牌。', 花牌: '和牌时的花牌计分。',
  }
  return descriptions[name] || '满足新编 MCR 对该番种的牌型与条件定义。'
}

function toggle(fan) {
  const next = new Set(expanded.value)
  if (next.has(fan)) next.delete(fan)
  else next.add(fan)
  expanded.value = next
}
</script>
