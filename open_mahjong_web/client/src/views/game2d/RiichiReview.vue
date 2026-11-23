<template>
  <section class="replay-section riichi-review">
    <h2>{{ tr('逐手复盘') }}</h2>
    <small v-if="hasAI">{{ tr('Satori 复盘') }} · {{ saved.roundCount }}{{ tr('小局') }}</small>
    <p v-if="cacheError">{{ cacheError }}</p>
    <p v-if="review.error">{{ review.error }}</p>
    <template v-else>
      <p v-if="current && !hasAI">{{ tr('实际选择') }} · {{ current.action }}</p>
      <p v-if="!current">{{ tr('跳到当前视角的实际选择') }}</p>
      <div class="review-nav">
        <button :disabled="locked || !previous" @click="$emit('seek', previous)">{{ tr('上一手') }}</button>
        <button :disabled="locked || !next" @click="$emit('seek', next)">{{ tr('下一手') }}</button>
      </div>
      <SatoriDecision v-for="entry in atNode" :key="entry.round + '-' + entry.eventIndex" :entry="entry" />
      <template v-if="current && !hasAI">
        <p>{{ tr('Satori 推荐') }} · {{ tr('暂无分析结果') }}</p>
        <template v-if="ukeire">
          <p>{{ tr('本地进张') }} · {{ ukeire.shanten }}{{ tr('向听') }} · {{ ukeire.total_accept }}{{ tr('枚') }}</p>
          <div class="review-tiles">
            <span v-for="item in ukeire.accept" :key="item.tile">{{ reviewTile(item.tile) }} × {{ item.remaining }}</span>
          </div>
        </template>
      </template>
    </template>
  </section>
</template>

<script setup>
import { computed, shallowRef, ref, watch } from 'vue'
import SatoriDecision from './SatoriDecision.vue'
import { publicApiGet } from '@/game2d/salasasa/api'
import { tr } from '@/i18n'
import { riichiReviewChoices, riichiDiscardUkeire, reviewTile, riichiReviewSignature } from '@/utils/riichiReview.js'

const props = defineProps(['record', 'gameId', 'viewer', 'round', 'node', 'locked'])
defineEmits(['seek'])
const saved = shallowRef(null)
const cacheError = ref('')
watch(() => [props.gameId, props.record], async ([id, record], _old, onCleanup) => {
  const controller = new AbortController()
  onCleanup(() => controller.abort())
  saved.value = null
  cacheError.value = ''
  if (!id) return
  try {
    const report = await publicApiGet('/platform/record/' + encodeURIComponent(id) + '/riichi-review', controller.signal)
    if (!report) return
    const signature = await riichiReviewSignature(record)
    if (controller.signal.aborted) return
    if (signature !== report.signature) throw new Error('牌谱版本与分析缓存不一致')
    saved.value = report
  } catch (error) {
    if (!controller.signal.aborted) cacheError.value = error.message
  }
}, { immediate: true })
const hasAI = computed(() => saved.value?.perspective === props.viewer && !!saved.value)
const atNode = computed(() => hasAI.value ? saved.value.entries.filter(e => e.round === props.round && e.node === props.node) : [])
const review = computed(() => {
  if (hasAI.value) return { points: saved.value.entries }
  try { return { points: riichiReviewChoices(props.record, props.viewer) } }
  catch (error) { return { points: [], error: error.message } }
})
const current = computed(() => review.value.points.find(p => p.round === props.round && p.node === props.node))
const previous = computed(() => review.value.points.findLast(p => p.round < props.round || (p.round === props.round && p.node < props.node)))
const next = computed(() => review.value.points.find(p => p.round > props.round || (p.round === props.round && p.node > props.node)))
const ukeire = computed(() => {
  try { return riichiDiscardUkeire(current.value) }
  catch { return null }
})
</script>

<style scoped>
.riichi-review h2, .riichi-review button { font-weight: 400; }
.riichi-review p { margin: 6px 0; font-size: 13px; }
.review-nav, .review-tiles { display: flex; flex-wrap: wrap; gap: 5px 10px; }
.review-tiles { max-height: 5.5em; overflow: auto; color: #ced7d8; font-size: 12px; }
</style>
