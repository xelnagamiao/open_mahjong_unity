<template>
  <div class="exercise">
    <div class="row-label">十三张手牌</div>
    <div class="hand" role="group" aria-label="十三张门清手牌">
      <TileChip v-for="(tile, i) in hand" :key="i" :tile-id="tile" size="lg" />
    </div>
    <div class="row-label">待牌（多选）</div>
    <div class="choices" role="group" aria-label="选择全部待牌">
      <button v-for="tile in options" :key="tile" type="button"
        :aria-label="TILE_NAME[tile]" :aria-pressed="picked.includes(tile)" :disabled="locked"
        :class="{ picked: picked.includes(tile), hit: waits?.includes(tile), miss: waits && picked.includes(tile) && !waits.includes(tile) }"
        @click="toggle(tile)">
        <TileChip :tile-id="tile" size="lg" aria-hidden="true" />
      </button>
    </div>
    <button v-if="allowNoWait" type="button" class="no-wait" :aria-pressed="noWaitPicked"
      :disabled="locked" @click="toggleNoWait">未听牌</button>
    <div v-if="waits" class="reveal" aria-live="polite">
      <div v-if="answer" class="answer-row">
        <span class="verdict" :class="{ incorrect: !answer.correct }">{{ answer.correct ? '正确' : '错误' }}</span>
        <template v-if="!answer.correct">
          <span>你的答案</span>
          <TileChip v-for="tile in [...answer.tiles].sort((a, b) => a - b)" :key="tile" :tile-id="tile" size="sm" />
          <span v-if="!answer.tiles.length">未听牌</span>
        </template>
      </div>
      <div class="answer-row">
        <span>正确待牌</span>
        <TileChip v-for="tile in waits" :key="tile" :tile-id="tile" size="sm" />
        <span v-if="!waits.length">未听牌</span>
      </div>
    </div>
    <div v-if="!waits || $slots.default" class="controls">
      <button v-if="!waits" type="button" class="submit" :disabled="locked || (!draft.length && !noWait)" @click="$emit('answer', [...draft])">
        {{ answer ? '已提交' : '提交答案' }}
      </button>
      <slot />
    </div>
  </div>
</template>

<script setup>
import { computed, ref } from 'vue'
import TileChip from '@/components/TileChip.vue'
import { TILE_NAME } from '@/composables/useMahjongTiles'
import { candidates } from '../../../../shared/menchin.mjs'

const props = defineProps({
  hand: { type: Array, required: true },
  answer: Object,
  waits: Array,
  disabled: Boolean,
  allowNoWait: Boolean,
})
defineEmits(['answer'])
const draft = ref([])
const noWait = ref(false)
const noWaitPicked = computed(() => props.answer ? props.answer.tiles.length === 0 : noWait.value)
const picked = computed(() => props.answer?.tiles ?? draft.value)
const locked = computed(() => Boolean(props.disabled || props.answer || props.waits))
const options = computed(() => candidates(props.hand))
function toggle(tile) {
  noWait.value = false
  draft.value = draft.value.includes(tile) ? draft.value.filter(id => id !== tile) : [...draft.value, tile]
}
function toggleNoWait() {
  draft.value = []
  noWait.value = !noWait.value
}
</script>

<style scoped>
.row-label { color: #555; font-size: 14px; margin-bottom: 8px; }
.hand {
  display: grid;
  grid-template-columns: repeat(13, minmax(0, 1fr));
  gap: 2px;
  width: min(100%, 650px);
  margin-bottom: 22px;
}
.exercise :deep(.tile-chip) { pointer-events: none; }
.exercise .hand :deep(.tile-chip.lg), .exercise .choices :deep(.tile-chip.lg) {
  box-sizing: border-box;
  min-width: 0;
  max-width: 100%;
  width: 100%;
  height: auto;
  aspect-ratio: 43 / 60;
}
.choices { display: grid; grid-template-columns: repeat(9, minmax(0, 56px)); gap: 5px; }
.choices button {
  min-width: 0; display: flex; padding: 3px; border: 1px solid #ddd;
  border-bottom: 4px solid #ddd; background: white; cursor: pointer;
}
.choices button:hover:enabled, .choices .picked { border-color: #409eff; background: #ecf5ff; }
.choices button:focus-visible { outline: 2px solid #165ac6; outline-offset: 1px; }
.choices button:disabled { cursor: default; }
.choices .hit { border-color: #429d45; background: #edf7ee; }
.choices .miss { border-color: #c0392b; background: #fff0ef; }
.controls, .answer-row { display: flex; flex-wrap: wrap; align-items: center; gap: 8px; font-size: 14px; }
.controls { margin-top: 16px; }
.controls:empty { display: none; }
.controls > span { color: #666; }
.submit { min-height: 40px; border: 0; background: #2475b9; padding: 8px 14px; color: white; font: inherit; cursor: pointer; }
.submit:disabled { opacity: .5; cursor: default; }
.no-wait { margin-top: 8px; padding: 7px 12px; border: 1px solid #ccc; color: #333; background: white; font: inherit; font-size: 14px; cursor: pointer; }
.no-wait[aria-pressed="true"] { border-color: #409eff; background: #ecf5ff; }
.reveal { display: grid; gap: 8px; margin-top: 16px; border-top: 1px solid #eee; padding-top: 12px; }
.verdict { color: #28743c; }
.verdict.incorrect { color: #a6302a; }
@media (max-width: 600px) {
  .hand { gap: 1px; }
  .choices { grid-template-columns: repeat(5, minmax(0, 48px)); }
}
</style>
