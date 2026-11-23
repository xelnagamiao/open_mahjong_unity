<template>
  <article class="satori-decision">
    <small>{{ phaseLabel }}</small>
    <p>{{ tr('实际选择') }} · {{ label(actualKey) }}</p>
    <p>{{ tr('Satori 推荐') }} · {{ label(react.action) }}（{{ percent(react.pi[react.action]) }}）</p>
    <label class="satori-candidates">
      {{ tr('候选动作') }}
      <select v-model="selected">
        <option v-for="[key, weight] in candidates" :key="key" :value="key">
          {{ label(key) }} · {{ percent(weight) }}{{ key === actualKey ? ' · ' + tr('实战') : '' }}
        </option>
      </select>
    </label>
    <p v-if="row?.action_name === 'agari'">{{ tr('和牌') }}</p>
    <template v-else-if="row">
      <p>{{ tr('Satori 进张') }} · {{ row.shanten }}{{ tr('向听') }} · {{ row.effective_count }}{{ tr('枚') }}</p>
      <div class="satori-tiles">
        <span v-for="item in row.effective_tiles" :key="item.tile">{{ tileText(item.tile) }} × {{ item.count }}</span>
      </div>
    </template>
    <p v-else>{{ tr('该候选没有进张数据') }}</p>
  </article>
</template>

<script setup>
import { computed, ref, watch } from 'vue'
import { tr } from '@/i18n'
import { reviewActionKey, reviewActionLabel, tileText } from '@/utils/riichiReview.js'

const props = defineProps({ entry: { type: Object, required: true } })
const react = computed(() => props.entry.response.react)
const actualKey = computed(() => reviewActionKey(props.entry.actual))
const phaseLabel = computed(() => {
  const phase = props.entry.phase
  if (phase === 'response') return tr('应对') + ' · ' + tileText(props.entry.triggerTile)
  return tr({ after_reach: '立直宣言后的弃牌', after_call: '鸣牌后的弃牌', draw: '摸牌后的选择' }[phase])
})
const candidates = computed(() => Object.entries(react.value.pi).sort((a, b) => b[1] - a[1]))
const selected = ref('')
watch(() => props.entry, () => { selected.value = react.value.action }, { immediate: true })
const row = computed(() => react.value.info.ukeire.rows.find(r => r.action_name === selected.value))
const percent = value => value > 0 && value < 0.001 ? '<0.1%' : (value * 100).toFixed(1) + '%'
const label = key => tr(reviewActionLabel(key))
</script>

<style scoped>
.satori-decision { margin-top: 9px; }
.satori-decision small { color: #bdc8c8; }
.satori-decision p { margin: 5px 0; }
.satori-candidates { display: flex; align-items: center; gap: 8px; }
.satori-candidates select { max-width: 100%; min-width: 0; font: inherit; }
.satori-tiles { display: flex; flex-wrap: wrap; gap: 3px 9px; max-height: 6em; overflow: auto; }
</style>
