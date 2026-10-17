<template>
  <details class="changsha-state" :open="wideScreen">
    <summary>{{ tr('长沙开杠与结算') }}</summary>
    <p>{{ tr(info.kamo ? '卡摸（小胡仅自摸）' : '小胡可点和') }} · {{ capLabel }}</p>
    <template v-if="info.exchange">
      <p>换六张 · 第{{ info.exchange.round }}轮 · 换牌骰子 {{ info.exchange.dice.join(' + ') || '未掷' }}</p>
      <p v-if="info.exchange.dealDice.length">抓牌骰子 {{ info.exchange.dealDice.join(' + ') }} · {{ winds[info.exchange.forbidden] }}禁普通小胡</p>
      <p v-if="info.exchange.ready.length">报听：{{ info.exchange.ready.map(s => winds[s]).join('、') }}</p>
      <p v-if="info.exchange.open.length">明牌：{{ info.exchange.open.map(s => winds[s]).join('、') }}</p>
      <p v-if="info.exchange.disqualified.length">三花色换牌失去和牌资格：{{ info.exchange.disqualified.map(s => winds[s]).join('、') }}</p>
      <p v-for="(joy,i) in info.exchange.fourJoy" :key="`joy-${i}`">{{ winds[joy.seat] }}已报四喜 · 骰子 {{ joy.dice.join(' + ') }}</p>
      <section v-for="win in info.exchange.settlements" :key="win.winner">
        <strong>{{ winds[win.winner] }}和牌 +{{ win.payment }}分</strong>
        <p v-for="(group,i) in win.groups" :key="i">第{{ i+1 }}组：{{ group.fans.join(' + ') || '小胡' }}</p>
        <p v-for="payer in win.payer_details" :key="payer.payer">{{ winds[payer.payer] }}支付 {{ payer.group_bases.join(' + ') }}分 × {{ 2 ** payer.hit_count }} = {{ payer.payment }}分</p>
      </section>
    </template>
    <template v-if="info.batch">
      <p>{{ tr('第 {count} 批', { count: info.batch.sequence }) }} · {{ tr(winds[info.batch.player]) }} · {{ tr('骰子') }} {{ info.batch.dice.join(' + ') }}</p>
      <div class="tiles" :aria-label="tr('开杠补牌')">
        <span v-for="(tile, index) in info.batch.tiles" :key="index" class="tile"><TileFaceImage :src="tileAsset(toMmcr(tile))" :face-id="mmcrFaceId(toMmcr(tile))" /></span>
      </div>
      <p v-if="!info.batch.tiles.length">{{ tr('可用牌不足，本次未补牌') }}</p>
    </template>
    <template v-if="info.birds.length">
      <p>{{ tr('鸟牌（原位翻开）') }}</p>
      <div class="tiles" :aria-label="tr('鸟牌')">
        <span v-for="(tile, index) in info.birds" :key="index" class="tile"><TileFaceImage :src="tileAsset(toMmcr(tile))" :face-id="mmcrFaceId(toMmcr(tile))" /></span>
      </div>
    </template>
  </details>
</template>
<script setup>
import TileFaceImage from '@/views/game2d/TileFaceImage.vue'
import { salasasaTileToMmcr as toMmcr } from '@/game2d/salasasa/gameAdapter'
import { mmcrFaceId } from '@/game2d/lib/tileFaceAsset'
import { computed, ref, onMounted, onBeforeUnmount } from 'vue'
import { tr } from '@/i18n'
const props = defineProps({ info: { type: Object, required: true }, tileAsset: { type: Function, required: true } })
const wideScreen = ref(true)
let screenQuery
const updateScreen = () => { wideScreen.value = screenQuery.matches }
onMounted(() => {
  screenQuery = window.matchMedia('(min-width: 701px)')
  updateScreen()
  screenQuery.addEventListener('change', updateScreen)
})
onBeforeUnmount(() => screenQuery?.removeEventListener('change', updateScreen))
const winds = ['东家', '南家', '西家', '北家']
const capLabel = computed(() => props.info.capType === 'big_hu'
  ? tr('{count}大胡封顶（鸟另计）', { count: props.info.capValue })
  : props.info.capType === 'score' ? tr('{count}分封顶（含鸟）', { count: props.info.capValue }) : tr('不封顶'))
</script>
<style scoped>
.changsha-state { position: absolute; right: 14px; top: 58px; z-index: 45; max-width: min(360px, calc(100% - 28px)); max-height: 55%; overflow: auto; padding: 10px 14px; color: #f3eee5; background: #242421f2; border: 1px solid #a99057; border-radius: 6px; font-size: 13px; }
summary { cursor: pointer; font-weight: 600; }
p { margin: 8px 0; line-height: 1.5; }
.tiles { display: flex; gap: 3px; }
.tile { flex: 0 0 28px; width: 28px; height: 38px; background: #f7f7f0; border-radius: 3px; }
@media (max-width: 700px) { .changsha-state { top: 158px; max-height: 145px; font-size: 12px; } }
</style>
